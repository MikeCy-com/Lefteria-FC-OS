using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Finance issues actual academy training months, never twelve implicit
/// installments. Parent access uses current canonical verified guardian
/// links, and provider status remains the sole source of settlement.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261109100000_AcademyTrainingFeeBilling")]
public sealed class AcademyTrainingFeeBilling : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE finance.training_fee_issues (
          id uuid PRIMARY KEY,
          organization_id varchar(32) NOT NULL REFERENCES club.organizations(id),
          player_id varchar(128) COLLATE "C" NOT NULL REFERENCES club.players(id),
          payer_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id),
          fee_month date NOT NULL CHECK (EXTRACT(day FROM fee_month)=1),
          invoice_due_date date NOT NULL CHECK (invoice_due_date>=fee_month
            AND invoice_due_date<(fee_month+interval '1 month')::date),
          amount_cents bigint NOT NULL CHECK (amount_cents BETWEEN 1 AND 100000000),
          currency char(3) NOT NULL DEFAULT 'EUR' CHECK (currency='EUR'),
          payer_email varchar(50) NOT NULL,
          payer_first_name varchar(50) NOT NULL,
          payer_last_name varchar(50) NOT NULL,
          status varchar(24) NOT NULL DEFAULT 'issued'
            CHECK (status IN ('issued','paid','review_required')),
          payment_request_id uuid UNIQUE REFERENCES finance.payment_requests(id),
          issued_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
          issued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          paid_at timestamptz,
          UNIQUE (organization_id,player_id,fee_month),
          CHECK ((status='paid')=(paid_at IS NOT NULL))
        );
        CREATE INDEX ix_training_fee_payer
          ON finance.training_fee_issues(payer_identity_id,fee_month DESC);

        CREATE TABLE finance.parent_fee_preferences (
          parent_identity_id uuid PRIMARY KEY REFERENCES identity.mobile_identities(id),
          preferred_day smallint NOT NULL DEFAULT 5 CHECK (preferred_day BETWEEN 1 AND 31),
          save_card_requested boolean NOT NULL DEFAULT false,
          automatic_collection_requested boolean NOT NULL DEFAULT false,
          automatic_collection_enabled boolean NOT NULL DEFAULT false
            CHECK (automatic_collection_enabled=false),
          consent_recorded_at timestamptz,
          updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
        );

        CREATE FUNCTION finance.set_training_invoice_due_date()
        RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $fee_due$
        DECLARE v_preferred int;
        BEGIN
          IF NEW.invoice_due_date IS NOT NULL THEN
            RAISE EXCEPTION 'invoice_due_date_is_server_issued' USING ERRCODE='22023';
          END IF;
          SELECT preferred_day INTO v_preferred FROM finance.parent_fee_preferences
            WHERE parent_identity_id=NEW.payer_identity_id;
          NEW.invoice_due_date:=make_date(EXTRACT(year FROM NEW.fee_month)::int,
            EXTRACT(month FROM NEW.fee_month)::int,
            LEAST(COALESCE(v_preferred,5),
              EXTRACT(day FROM (NEW.fee_month+interval '1 month'-interval '1 day'))::int));
          RETURN NEW;
        END $fee_due$;
        CREATE TRIGGER trg_training_fee_invoice_due
          BEFORE INSERT ON finance.training_fee_issues
          FOR EACH ROW EXECUTE FUNCTION finance.set_training_invoice_due_date();


        CREATE TABLE finance.training_fee_sms_outbox (
          id uuid PRIMARY KEY,
          fee_issue_id uuid NOT NULL REFERENCES finance.training_fee_issues(id),
          client_request_id uuid NOT NULL,
          organization_id varchar(32) NOT NULL REFERENCES club.organizations(id),
          actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id),
          state varchar(16) NOT NULL DEFAULT 'reserved'
            CHECK (state IN ('reserved','accepted','unknown')),
          provider_sid varchar(34),
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          UNIQUE (fee_issue_id,client_request_id)
        );
        REVOKE ALL ON TABLE finance.training_fee_issues,
          finance.parent_fee_preferences,finance.training_fee_sms_outbox FROM PUBLIC;
        ALTER TABLE finance.training_fee_issues ENABLE ROW LEVEL SECURITY;
        ALTER TABLE finance.parent_fee_preferences ENABLE ROW LEVEL SECURITY;
        ALTER TABLE finance.training_fee_sms_outbox ENABLE ROW LEVEL SECURITY;

        CREATE FUNCTION finance.issue_admin_training_fee(
          _digest text,_owner text,_id uuid,_player text,_month date,
          _cents bigint,_email text,_first text,_last text
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $fee_issue$
        DECLARE
          v_actor uuid; v_session uuid; v_role text;
          v_payer uuid; v_row finance.training_fee_issues%ROWTYPE;
          v_created boolean:=false;
        BEGIN
          IF _digest !~ '^[0-9a-f]{64}$'
            OR _owner<>'lefteria_fc_academy' OR _id IS NULL
            OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
            OR _month IS NULL OR EXTRACT(day FROM _month)<>1
            OR _month < DATE '2020-01-01' OR _month > DATE '2100-12-01'
            OR _cents NOT BETWEEN 1 AND 100000000
            OR length(_email) NOT BETWEEN 3 AND 50
            OR _email !~ '^[ -~]+$' OR position('@' IN _email)=0
            OR length(_first) NOT BETWEEN 1 AND 50 OR _first !~ '^[ -~]+$'
            OR length(_last) NOT BETWEEN 1 AND 50 OR _last !~ '^[ -~]+$' THEN
            RAISE EXCEPTION 'invalid_training_fee_issue' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
          IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
            RAISE EXCEPTION 'fee_access_denied' USING ERRCODE='42501';
          END IF;

          SELECT i.id INTO v_payer
          FROM club.players p
          JOIN identity.mobile_record_links l
            ON l.collection='players' AND l.record_id=p.id
            AND l.kind='guardian' AND l.role='parent'
          JOIN identity.mobile_identities i
            ON i.id=l.identity_id AND i.normalized_phone=l.normalized_phone
            AND i.role='parent' AND i.is_active
          JOIN identity.canonical_contact_bindings b
            ON b.collection=l.collection AND b.source_id=l.record_id
            AND b.kind='guardian' AND b.normalized_phone=i.normalized_phone
          WHERE p.id=_player AND p.organization_id=_owner
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          ORDER BY l.is_primary DESC,i.id LIMIT 1;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'verified_parent_and_child_required' USING ERRCODE='42501';
          END IF;

          INSERT INTO finance.training_fee_issues(
            id,organization_id,player_id,payer_identity_id,fee_month,
            amount_cents,payer_email,payer_first_name,payer_last_name,issued_by)
          VALUES(_id,_owner,_player,v_payer,_month,_cents,_email,_first,_last,v_actor)
          ON CONFLICT (organization_id,player_id,fee_month) DO NOTHING
          RETURNING * INTO v_row;
          IF FOUND THEN v_created:=true; END IF;
          IF NOT v_created THEN
            SELECT * INTO v_row FROM finance.training_fee_issues
              WHERE organization_id=_owner AND player_id=_player
                AND fee_month=_month FOR UPDATE;
            IF NOT FOUND OR v_row.amount_cents<>_cents
              OR v_row.payer_identity_id<>v_payer
              OR v_row.payer_email<>_email OR v_row.payer_first_name<>_first
              OR v_row.payer_last_name<>_last THEN
              RAISE EXCEPTION 'fee_month_already_issued_differently'
                USING ERRCODE='23505';
            END IF;
          END IF;
          IF v_created THEN
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,device_session_id,
              action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
              'admin.finance.training_fee.issue','succeeded',_owner,'explicit_month');
          END IF;
          RETURN jsonb_build_object('id',v_row.id,'created',v_created,
            'status',v_row.status,'fee_month',v_row.fee_month,
            'amount_cents',v_row.amount_cents,
            'payer_email',v_row.payer_email,
            'payer_first_name',v_row.payer_first_name,
            'payer_last_name',v_row.payer_last_name,
            'player_id',v_row.player_id,
            'payment_request_id',v_row.payment_request_id);
        END $fee_issue$;

        CREATE FUNCTION finance.attach_admin_training_fee_payment(
          _digest text,_owner text,_issue uuid,_request uuid
        ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $fee_attach$
        DECLARE v_actor uuid; v_role text; v_fee finance.training_fee_issues%ROWTYPE;
                v_payment finance.payment_requests%ROWTYPE;
        BEGIN
          IF _digest !~ '^[0-9a-f]{64}$'
            OR _owner<>'lefteria_fc_academy' OR _issue IS NULL OR _request IS NULL
            THEN RAISE EXCEPTION 'fee_attach_invalid' USING ERRCODE='22023'; END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.id,a.role INTO v_actor,v_role FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
              AND g.organization_id=_owner
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled
              AND a.password_hash IS NOT NULL;
          IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
            RAISE EXCEPTION 'fee_attach_denied' USING ERRCODE='42501'; END IF;
          SELECT * INTO v_fee FROM finance.training_fee_issues
            WHERE id=_issue AND organization_id=_owner FOR UPDATE;
          SELECT * INTO v_payment FROM finance.payment_requests
            WHERE id=_request AND organization_id=_owner AND actor_id=v_actor FOR UPDATE;
          IF v_fee.id IS NULL OR v_payment.id IS NULL
            OR v_fee.status<>'issued'
            OR (v_fee.payment_request_id IS NOT NULL
              AND v_fee.payment_request_id<>_request)
            OR v_fee.amount_cents<>v_payment.amount_cents
            OR v_fee.payer_email<>v_payment.payer_email
            OR v_fee.payer_first_name<>v_payment.payer_first_name
            OR v_fee.payer_last_name<>v_payment.payer_last_name
            OR v_payment.currency<>'EUR'
            OR v_payment.purpose<>('Academy training '||to_char(v_fee.fee_month,'YYYY-MM'))
          THEN RAISE EXCEPTION 'fee_payment_mismatch' USING ERRCODE='23505'; END IF;
          UPDATE finance.training_fee_issues
            SET payment_request_id=_request WHERE id=_issue;
          RETURN true;
        END $fee_attach$;

        CREATE FUNCTION finance.mark_training_fee_from_capture()
        RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $fee_paid$
        BEGIN
          -- This fires ONLY when the signed gateway callback changes the
          -- matching request to paid, never on browser redirect or URL issue.
          IF NEW.status='paid' AND OLD.status IS DISTINCT FROM NEW.status THEN
            UPDATE finance.training_fee_issues f
            SET status=CASE WHEN f.amount_cents=NEW.amount_cents
                      AND NEW.currency='EUR' AND f.organization_id=NEW.organization_id
                      AND f.payer_email=NEW.payer_email
                      AND f.payer_first_name=NEW.payer_first_name
                      AND f.payer_last_name=NEW.payer_last_name THEN 'paid'
                      ELSE 'review_required' END,
                paid_at=CASE WHEN f.amount_cents=NEW.amount_cents
                      AND NEW.currency='EUR' AND f.organization_id=NEW.organization_id
                      AND f.payer_email=NEW.payer_email
                      AND f.payer_first_name=NEW.payer_first_name
                      AND f.payer_last_name=NEW.payer_last_name THEN clock_timestamp()
                      ELSE NULL END
            WHERE f.payment_request_id=NEW.id AND f.status='issued';
          ELSIF NEW.status='review_required' AND OLD.status IS DISTINCT FROM NEW.status THEN
            UPDATE finance.training_fee_issues
            SET status='review_required'
            WHERE payment_request_id=NEW.id AND status='issued';
          END IF;
          RETURN NEW;
        END $fee_paid$;
        CREATE TRIGGER trg_training_fee_payment_state
          AFTER UPDATE OF status ON finance.payment_requests
          FOR EACH ROW EXECUTE FUNCTION finance.mark_training_fee_from_capture();

        CREATE FUNCTION finance.read_admin_training_fees(_digest text,_owner text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $fee_admin_read$
        DECLARE v_role text; v_fees jsonb;
        BEGIN
          IF _digest !~ '^[0-9a-f]{64}$' OR _owner<>'lefteria_fc_academy'
            THEN RAISE EXCEPTION 'fee_scope_invalid' USING ERRCODE='22023'; END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.role INTO v_role FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
              AND g.organization_id=_owner
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled
              AND a.password_hash IS NOT NULL;
          IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
            RAISE EXCEPTION 'fee_admin_read_denied' USING ERRCODE='42501'; END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',f.id,'player_id',f.player_id,'child_name',p.name,
              'fee_month',f.fee_month,'amount_cents',f.amount_cents,
              'currency',f.currency,'status',f.status,
              'payer_email',f.payer_email,
              'payer_first_name',f.payer_first_name,
              'payer_last_name',f.payer_last_name,
              'payment_request_id',f.payment_request_id,
              'payment_status',COALESCE(r.status,'not_created'),
              'checkout_url',CASE WHEN r.status='ready' THEN r.checkout_url ELSE NULL END,
              'sms_status',(SELECT o.state FROM finance.training_fee_sms_outbox o
                  WHERE o.fee_issue_id=f.id ORDER BY o.created_at DESC LIMIT 1))
              ORDER BY f.fee_month DESC,f.issued_at DESC,f.id DESC),'[]'::jsonb)
            INTO v_fees
            FROM (SELECT * FROM finance.training_fee_issues
              WHERE organization_id=_owner
              ORDER BY fee_month DESC,issued_at DESC,id DESC LIMIT 100) f
            JOIN club.players p ON p.id=f.player_id
            LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id;
          RETURN jsonb_build_object('organization_id',_owner,'fees',v_fees);
        END $fee_admin_read$;

        CREATE FUNCTION finance.read_parent_training_fees(
          _identity uuid,_phone text,_role text,_expires timestamptz
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $fee_parent_read$
        DECLARE v_fees jsonb; v_day integer; v_save boolean; v_auto boolean;
        BEGIN
          IF _identity IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
            OR _role<>'parent' OR _expires<=clock_timestamp()
            OR NOT EXISTS(SELECT 1 FROM identity.mobile_identities i
              WHERE i.id=_identity AND i.normalized_phone=_phone
                AND i.role='parent' AND i.is_active)
          THEN RAISE EXCEPTION 'parent_fee_access_denied' USING ERRCODE='42501'; END IF;
          SELECT preferred_day,save_card_requested,automatic_collection_requested
            INTO v_day,v_save,v_auto FROM finance.parent_fee_preferences
            WHERE parent_identity_id=_identity;
          v_day:=COALESCE(v_day,5); v_save:=COALESCE(v_save,false);
          v_auto:=COALESCE(v_auto,false);
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',f.id,'player_id',f.player_id,'child_name',p.name,
            'fee_month',f.fee_month,'amount_cents',f.amount_cents,
            'currency',f.currency,'status',f.status,
            'payment_status',coalesce(r.status,'not_created'),
            'checkout_url',CASE WHEN r.status='ready' AND f.status='issued'
              THEN r.checkout_url ELSE NULL END,
            'due_date',f.invoice_due_date
            ORDER BY f.fee_month DESC,f.id DESC),'[]'::jsonb)
          INTO v_fees
          FROM (SELECT * FROM finance.training_fee_issues
            WHERE payer_identity_id=_identity
            ORDER BY fee_month DESC,id DESC LIMIT 100) f
          JOIN club.players p ON p.id=f.player_id
            AND p.organization_id=f.organization_id
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          JOIN identity.mobile_record_links l
            ON l.collection='players' AND l.record_id=f.player_id
            AND l.kind='guardian' AND l.role='parent' AND l.identity_id=_identity
            AND l.normalized_phone=_phone
          JOIN identity.canonical_contact_bindings b
            ON b.collection=l.collection AND b.source_id=l.record_id
            AND b.kind='guardian' AND b.normalized_phone=_phone
          LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id;
          RETURN jsonb_build_object('fees',v_fees,
            'preferred_day',v_day,'save_card_requested',v_save,
            'automatic_collection_requested',v_auto,
            'automatic_collection_active',false,
            'saved_card_available',false);
        END $fee_parent_read$;

        CREATE FUNCTION finance.set_parent_fee_preferences(
          _identity uuid,_phone text,_role text,_expires timestamptz,
          _day integer,_save boolean,_automatic boolean
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $fee_pref$
        BEGIN
          IF _identity IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
            OR _role<>'parent' OR _expires<=clock_timestamp()
            OR _day NOT BETWEEN 1 AND 31 OR _save IS NULL OR _automatic IS NULL
            OR (_automatic AND NOT _save)
            OR NOT EXISTS(SELECT 1 FROM identity.mobile_identities i
              WHERE i.id=_identity AND i.normalized_phone=_phone
                AND i.role='parent' AND i.is_active)
          THEN RAISE EXCEPTION 'invalid_parent_fee_preferences'
            USING ERRCODE='42501'; END IF;
          INSERT INTO finance.parent_fee_preferences(
            parent_identity_id,preferred_day,save_card_requested,
            automatic_collection_requested,consent_recorded_at)
          VALUES(_identity,_day,_save,_automatic,
            CASE WHEN _automatic THEN clock_timestamp() ELSE NULL END)
          ON CONFLICT (parent_identity_id) DO UPDATE
            SET preferred_day=EXCLUDED.preferred_day,
            save_card_requested=EXCLUDED.save_card_requested,
            automatic_collection_requested=EXCLUDED.automatic_collection_requested,
            consent_recorded_at=CASE WHEN EXCLUDED.automatic_collection_requested
              THEN clock_timestamp() ELSE NULL END,
            updated_at=clock_timestamp();
          RETURN jsonb_build_object('preferred_day',_day,
            'save_card_requested',_save,'automatic_collection_requested',_automatic,
            'automatic_collection_active',false,'saved_card_available',false);
        END $fee_pref$;

        CREATE FUNCTION finance.reserve_admin_training_fee_sms(
          _digest text,_owner text,_fee uuid,_request uuid
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $sms_reserve$
        DECLARE v_actor uuid; v_session uuid; v_role text;
          v_fee finance.training_fee_issues%ROWTYPE;
          v_payment finance.payment_requests%ROWTYPE;
          v_phone text; v_row finance.training_fee_sms_outbox%ROWTYPE;
          v_created boolean:=false;
        BEGIN
          IF _digest !~ '^[0-9a-f]{64}$'
            OR _owner<>'lefteria_fc_academy' OR _fee IS NULL OR _request IS NULL
            THEN RAISE EXCEPTION 'fee_sms_invalid' USING ERRCODE='22023'; END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
            FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
              AND g.organization_id=_owner
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled
              AND a.password_hash IS NOT NULL;
          IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
            RAISE EXCEPTION 'fee_sms_denied' USING ERRCODE='42501'; END IF;
          SELECT * INTO v_fee FROM finance.training_fee_issues
            WHERE id=_fee AND organization_id=_owner FOR UPDATE;
          SELECT * INTO v_payment FROM finance.payment_requests
            WHERE id=v_fee.payment_request_id;
          SELECT i.normalized_phone INTO v_phone
            FROM identity.mobile_identities i
            JOIN identity.mobile_record_links l ON l.identity_id=i.id
              AND l.normalized_phone=i.normalized_phone
              AND l.role='parent' AND l.kind='guardian'
              AND l.collection='players' AND l.record_id=v_fee.player_id
            JOIN identity.canonical_contact_bindings b ON b.collection=l.collection
              AND b.source_id=l.record_id AND b.kind='guardian'
              AND b.normalized_phone=i.normalized_phone
            WHERE i.id=v_fee.payer_identity_id AND i.role='parent' AND i.is_active;
          IF v_fee.id IS NULL OR v_fee.status<>'issued'
            OR v_payment.status<>'ready'
            OR v_payment.checkout_url IS NULL
            OR v_payment.amount_cents<>v_fee.amount_cents
            OR v_payment.organization_id<>_owner
            OR v_phone !~ '^[1-9][0-9]{7,14}$'
            THEN RAISE EXCEPTION 'fee_sms_unavailable' USING ERRCODE='42501'; END IF;
          INSERT INTO finance.training_fee_sms_outbox(
            id,fee_issue_id,client_request_id,organization_id,actor_id)
          VALUES(gen_random_uuid(),_fee,_request,_owner,v_actor)
          ON CONFLICT(fee_issue_id,client_request_id) DO NOTHING
          RETURNING * INTO v_row;
          IF FOUND THEN v_created:=true; END IF;
          IF NOT v_created THEN
            SELECT * INTO v_row FROM finance.training_fee_sms_outbox
              WHERE fee_issue_id=_fee AND client_request_id=_request;
          ELSE
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,device_session_id,
              action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
              'admin.finance.training_fee.sms','succeeded',_owner,'reserved_only');
          END IF;
          RETURN jsonb_build_object('id',v_row.id,'new',v_created,
            'state',v_row.state,'phone','+'||v_phone,
            'checkout_url',v_payment.checkout_url,
            'amount_cents',v_fee.amount_cents,'fee_month',v_fee.fee_month);
        END $sms_reserve$;

        CREATE FUNCTION finance.finish_admin_training_fee_sms(
          _digest text,_owner text,_id uuid,_accepted boolean,_sid text
        ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $sms_finish$
        DECLARE v_actor uuid; v_role text;
        BEGIN
          IF _digest !~ '^[0-9a-f]{64}$'
            OR _owner<>'lefteria_fc_academy' OR _id IS NULL
            OR _accepted IS NULL
            OR (_accepted AND (_sid IS NULL OR _sid !~ '^SM[0-9a-fA-F]{32}$'))
          THEN RAISE EXCEPTION 'fee_sms_finish_invalid' USING ERRCODE='22023'; END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.id,a.role INTO v_actor,v_role FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
              AND g.organization_id=_owner
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled
              AND a.password_hash IS NOT NULL;
          IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
            RAISE EXCEPTION 'fee_sms_finish_denied' USING ERRCODE='42501'; END IF;
          UPDATE finance.training_fee_sms_outbox
            SET state=CASE WHEN _accepted THEN 'accepted' ELSE 'unknown' END,
              provider_sid=CASE WHEN _accepted THEN _sid ELSE NULL END,
              updated_at=clock_timestamp()
            WHERE id=_id AND organization_id=_owner
              AND actor_id=v_actor AND state='reserved';
          RETURN FOUND;
        END $sms_finish$;

        REVOKE ALL ON FUNCTION finance.set_training_invoice_due_date() FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.issue_admin_training_fee(
          text,text,uuid,text,date,bigint,text,text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.attach_admin_training_fee_payment(
          text,text,uuid,uuid) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.mark_training_fee_from_capture() FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.read_admin_training_fees(
          text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.read_parent_training_fees(
          uuid,text,text,timestamptz) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.set_parent_fee_preferences(
          uuid,text,text,timestamptz,integer,boolean,boolean) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.reserve_admin_training_fee_sms(
          text,text,uuid,uuid) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.finish_admin_training_fee_sms(
          text,text,uuid,boolean,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $fee_rollback$ BEGIN
          IF EXISTS(SELECT 1 FROM finance.training_fee_issues LIMIT 1)
             OR EXISTS(SELECT 1 FROM finance.parent_fee_preferences LIMIT 1)
             OR EXISTS(SELECT 1 FROM finance.training_fee_sms_outbox LIMIT 1) THEN
            RAISE EXCEPTION 'finance_data_requires_forward_recovery';
          END IF;
        END $fee_rollback$;
        DROP FUNCTION finance.finish_admin_training_fee_sms(text,text,uuid,boolean,text);
        DROP FUNCTION finance.reserve_admin_training_fee_sms(text,text,uuid,uuid);
        DROP FUNCTION finance.set_parent_fee_preferences(uuid,text,text,timestamptz,integer,boolean,boolean);
        DROP FUNCTION finance.read_parent_training_fees(uuid,text,text,timestamptz);
        DROP FUNCTION finance.read_admin_training_fees(text,text);
        DROP TRIGGER trg_training_fee_payment_state ON finance.payment_requests;
        DROP FUNCTION finance.mark_training_fee_from_capture();
        DROP FUNCTION finance.attach_admin_training_fee_payment(text,text,uuid,uuid);
        DROP FUNCTION finance.issue_admin_training_fee(text,text,uuid,text,date,bigint,text,text,text);
        DROP TABLE finance.training_fee_sms_outbox;
        DROP TABLE finance.parent_fee_preferences;
        DROP TRIGGER trg_training_fee_invoice_due ON finance.training_fee_issues;
        DROP FUNCTION finance.set_training_invoice_due_date();
        DROP TABLE finance.training_fee_issues;
        """);
}
