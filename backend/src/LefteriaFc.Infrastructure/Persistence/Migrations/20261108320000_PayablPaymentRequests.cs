using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private, currency-precise payment requests and signed provider events.
/// Payments are never inferred from redirects. No card data or bank data
/// is stored. The OS remains separate from the old production payments.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108320000_PayablPaymentRequests")]
public sealed class PayablPaymentRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA IF NOT EXISTS finance;
        REVOKE ALL ON SCHEMA finance FROM PUBLIC;
        CREATE TABLE finance.payment_requests (
          id uuid PRIMARY KEY,
          organization_id varchar(32) NOT NULL REFERENCES club.organizations(id),
          actor_id uuid NOT NULL,
          client_request_id uuid NOT NULL,
          order_id varchar(40) NOT NULL UNIQUE,
          purpose varchar(140) NOT NULL,
          amount_cents bigint NOT NULL CHECK (amount_cents BETWEEN 1 AND 100000000),
          currency char(3) NOT NULL DEFAULT 'EUR' CHECK (currency='EUR'),
          payer_email varchar(50) NOT NULL,
          payer_first_name varchar(50) NOT NULL,
          payer_last_name varchar(50) NOT NULL,
          callback_digest varchar(64) COLLATE "C" NOT NULL UNIQUE
            CHECK (callback_digest ~ '^[0-9a-f]{64}$'),
          status varchar(24) NOT NULL DEFAULT 'initiating'
            CHECK (status IN ('initiating','ready','paid','failed','review_required')),
          checkout_url text,
          initial_transaction_id varchar(40),
          captured_transaction_id varchar(40),
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          UNIQUE (organization_id,actor_id,client_request_id)
        );
        CREATE INDEX ix_finance_payment_owner
          ON finance.payment_requests(organization_id,created_at DESC);
        CREATE TABLE finance.payment_notifications (
          request_id uuid NOT NULL REFERENCES finance.payment_requests(id),
          transaction_id varchar(40) COLLATE "C" NOT NULL,
          event_type varchar(24) NOT NULL,
          error_code integer NOT NULL,
          provider_timestamp bigint NOT NULL,
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(request_id,transaction_id,event_type,error_code,provider_timestamp)
        );
        REVOKE ALL ON TABLE finance.payment_requests,finance.payment_notifications FROM PUBLIC;
        ALTER TABLE finance.payment_requests ENABLE ROW LEVEL SECURITY;
        ALTER TABLE finance.payment_notifications ENABLE ROW LEVEL SECURITY;

        CREATE FUNCTION finance.create_admin_payment_request(
          _digest text,_owner text,_id uuid,_client uuid,_nonce_digest text,
          _purpose text,_cents bigint,_email text,_first text,_last text
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $payment_create$
        DECLARE v_actor uuid; v_role text; v_session uuid;
          v_row finance.payment_requests%ROWTYPE; v_created boolean:=false;
        BEGIN
          IF _digest !~ '^[0-9a-f]{64}$' OR _owner NOT IN
            ('lefteria_fc','lefteria_fc_academy') OR _id IS NULL OR _client IS NULL
            OR _nonce_digest !~ '^[0-9a-f]{64}$'
            OR length(_purpose) NOT BETWEEN 3 AND 140
            OR _purpose<>btrim(_purpose) OR _purpose ~ '[[:cntrl:]]'
            OR _cents NOT BETWEEN 1 AND 100000000
            OR length(_email) NOT BETWEEN 3 AND 50 OR _email !~ '^[ -~]+$'
            OR position('@' in _email)=0
            OR length(_first) NOT BETWEEN 1 AND 50 OR _first !~ '^[ -~]+$'
            OR length(_last) NOT BETWEEN 1 AND 50 OR _last !~ '^[ -~]+$'
          THEN RAISE EXCEPTION 'payment_request_invalid' USING ERRCODE='22023'; END IF;
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
          IF NOT FOUND OR v_role NOT IN ('manager','super_admin')
            THEN RAISE EXCEPTION 'payment_access_denied' USING ERRCODE='42501'; END IF;
          INSERT INTO finance.payment_requests(
            id,organization_id,actor_id,client_request_id,order_id,purpose,
            amount_cents,payer_email,payer_first_name,payer_last_name,callback_digest)
          VALUES(_id,_owner,v_actor,_client,'LFC'||replace(_id::text,'-',''),
            _purpose,_cents,_email,_first,_last,_nonce_digest)
          ON CONFLICT (organization_id,actor_id,client_request_id) DO NOTHING
          RETURNING * INTO v_row;
          IF FOUND THEN v_created:=true; END IF;
          IF NOT v_created THEN
            SELECT * INTO v_row FROM finance.payment_requests
            WHERE organization_id=_owner AND actor_id=v_actor
              AND client_request_id=_client;
            IF NOT FOUND OR v_row.purpose<>_purpose OR v_row.amount_cents<>_cents
              OR v_row.payer_email<>_email OR v_row.payer_first_name<>_first
              OR v_row.payer_last_name<>_last THEN
              RAISE EXCEPTION 'payment_idempotency_conflict' USING ERRCODE='23505';
            END IF;
          END IF;
          IF v_created THEN
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,device_session_id,
              action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
              'admin.finance.payabl.request','succeeded',_owner,'payment_request');
          END IF;
          RETURN jsonb_build_object('id',v_row.id,'created',v_created,
            'status',v_row.status,'checkout_url',v_row.checkout_url,
            'amount_cents',v_row.amount_cents,'currency',v_row.currency);
        END $payment_create$;

        CREATE FUNCTION finance.ready_admin_payment_request(
          _digest text,_owner text,_id uuid,_checkout text,_transaction text
        ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $payment_ready$
        DECLARE v_actor uuid; v_role text;
        BEGIN
          IF _digest !~ '^[0-9a-f]{64}$' OR _owner NOT IN
             ('lefteria_fc','lefteria_fc_academy') OR _checkout IS NULL
             OR length(_checkout)>2048 OR _checkout !~ '^https://'
             OR _transaction !~ '^[0-9]{1,40}$' THEN
            RAISE EXCEPTION 'payment_ready_invalid' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s JOIN identity.admin_accounts a
            ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
            AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
          IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
            RAISE EXCEPTION 'payment_ready_denied' USING ERRCODE='42501';
          END IF;
          UPDATE finance.payment_requests SET status='ready',
            checkout_url=_checkout,initial_transaction_id=_transaction,
            updated_at=clock_timestamp()
          WHERE id=_id AND organization_id=_owner AND actor_id=v_actor
            AND status='initiating';
          IF NOT FOUND THEN
            RAISE EXCEPTION 'payment_state_conflict' USING ERRCODE='P0001';
          END IF;
          RETURN true;
        END $payment_ready$;

        CREATE FUNCTION finance.read_admin_payment_requests(
          _digest text,_owner text
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $payment_read$
        DECLARE v_role text; v_items jsonb;
        BEGIN
          IF _digest !~ '^[0-9a-f]{64}$' OR _owner NOT IN
            ('lefteria_fc','lefteria_fc_academy') THEN
            RAISE EXCEPTION 'payment_scope_invalid' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.role INTO v_role FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
            AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
          IF NOT FOUND OR v_role NOT IN ('manager','super_admin')
            THEN RAISE EXCEPTION 'payment_read_denied' USING ERRCODE='42501'; END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',p.id,'purpose',p.purpose,'amount_cents',p.amount_cents,
            'currency',p.currency,'status',p.status,'checkout_url',p.checkout_url,
            'payer_email',p.payer_email,'created_at',p.created_at)
            ORDER BY p.created_at DESC,p.id DESC),'[]'::jsonb)
          INTO v_items FROM (
            SELECT * FROM finance.payment_requests
            WHERE organization_id=_owner ORDER BY created_at DESC,id DESC LIMIT 50
          ) p;
          RETURN jsonb_build_object('organization_id',_owner,'payments',v_items);
        END $payment_read$;

        -- Called by the separate restricted runtime role only AFTER HTTPS
        -- callback signature verification. The nonce hash binds a callback to
        -- one order because payabl's simplified signature omits orderid.
        CREATE FUNCTION finance.apply_payabl_notification(
          _id uuid,_nonce_digest text,_transaction text,_kind text,
          _code integer,_stamp bigint,_order_id text
        ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $payment_notice$
        DECLARE v_status text; v_owner text; v_expected_order text; v_changed boolean:=false;
        BEGIN
          IF _id IS NULL OR _nonce_digest !~ '^[0-9a-f]{64}$'
            OR _transaction !~ '^[0-9]{1,40}$'
            OR _kind NOT IN ('capture','order','refund','cft','reversal')
            OR _stamp NOT BETWEEN 1500000000 AND 4102444800
            OR _order_id IS NULL OR _order_id !~ '^LFC[0-9a-f]{32}
            RAISE EXCEPTION 'payment_notice_invalid' USING ERRCODE='22023';
          END IF;
          SELECT status,organization_id,order_id INTO v_status,v_owner,v_expected_order
          FROM finance.payment_requests
          WHERE id=_id AND callback_digest=_nonce_digest FOR UPDATE;
          IF NOT FOUND OR v_status='initiating'
            OR v_expected_order<>_order_id THEN
            RAISE EXCEPTION 'payment_notice_not_ready' USING ERRCODE='42501';
          END IF;
          INSERT INTO finance.payment_notifications(
            request_id,transaction_id,event_type,error_code,provider_timestamp)
          VALUES(_id,_transaction,_kind,_code,_stamp)
          ON CONFLICT DO NOTHING;
          IF NOT FOUND THEN RETURN false; END IF;
          -- Capture is the ONLY positive receipt. All other messages require
          -- reconciliation rather than assuming payment. Never downgrade PAID.
          IF v_status<>'paid' AND _kind='capture' AND _code=0 THEN
            UPDATE finance.payment_requests SET status='paid',
              captured_transaction_id=_transaction,updated_at=clock_timestamp()
            WHERE id=_id; v_changed:=true;
          ELSIF v_status NOT IN ('paid','review_required') THEN
            UPDATE finance.payment_requests SET status='review_required',
              updated_at=clock_timestamp() WHERE id=_id;
          END IF;
          RETURN v_changed;
        END $payment_notice$;
        REVOKE ALL ON FUNCTION finance.create_admin_payment_request(
          text,text,uuid,uuid,text,text,bigint,text,text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.ready_admin_payment_request(
          text,text,uuid,text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.read_admin_payment_requests(
          text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.apply_payabl_notification(
          uuid,text,text,text,integer,bigint,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $payment_rollback$ BEGIN
          IF EXISTS(SELECT 1 FROM finance.payment_requests LIMIT 1) THEN
            RAISE EXCEPTION 'payment_data_requires_forward_recovery';
          END IF;
        END $payment_rollback$;
        DROP FUNCTION finance.apply_payabl_notification(
          uuid,text,text,text,integer,bigint,text);
        DROP FUNCTION finance.read_admin_payment_requests(text,text);
        DROP FUNCTION finance.ready_admin_payment_request(text,text,uuid,text,text);
        DROP FUNCTION finance.create_admin_payment_request(
          text,text,uuid,uuid,text,text,bigint,text,text,text);
        DROP TABLE finance.payment_notifications;
        DROP TABLE finance.payment_requests;
        """);
}
 THEN
            RAISE EXCEPTION 'payment_notice_invalid' USING ERRCODE='22023';
          END IF;
          SELECT status,organization_id,initial_transaction_id INTO v_status,v_owner,v_initial_transaction
          FROM finance.payment_requests
          WHERE id=_id AND callback_digest=_nonce_digest FOR UPDATE;
          IF NOT FOUND OR v_status='initiating' THEN
            RAISE EXCEPTION 'payment_notice_not_ready' USING ERRCODE='42501';
          END IF;
          INSERT INTO finance.payment_notifications(
            request_id,transaction_id,event_type,error_code,provider_timestamp)
          VALUES(_id,_transaction,_kind,_code,_stamp)
          ON CONFLICT DO NOTHING;
          IF NOT FOUND THEN RETURN false; END IF;
          -- Capture is the ONLY positive receipt. All other messages require
          -- reconciliation rather than assuming payment. Never downgrade PAID.
          IF v_status<>'paid' AND _kind='capture' AND _code=0
              AND v_initial_transaction=_transaction THEN
            UPDATE finance.payment_requests SET status='paid',
              captured_transaction_id=_transaction,updated_at=clock_timestamp()
            WHERE id=_id; v_changed:=true;
          ELSIF v_status NOT IN ('paid','review_required') THEN
            UPDATE finance.payment_requests SET status='review_required',
              updated_at=clock_timestamp() WHERE id=_id;
          END IF;
          RETURN v_changed;
        END $payment_notice$;
        REVOKE ALL ON FUNCTION finance.create_admin_payment_request(
          text,text,uuid,uuid,text,text,bigint,text,text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.ready_admin_payment_request(
          text,text,uuid,text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.read_admin_payment_requests(
          text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION finance.apply_payabl_notification(
          uuid,text,text,text,integer,bigint,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $payment_rollback$ BEGIN
          IF EXISTS(SELECT 1 FROM finance.payment_requests LIMIT 1) THEN
            RAISE EXCEPTION 'payment_data_requires_forward_recovery';
          END IF;
        END $payment_rollback$;
        DROP FUNCTION finance.apply_payabl_notification(
          uuid,text,text,text,integer,bigint,text);
        DROP FUNCTION finance.read_admin_payment_requests(text,text);
        DROP FUNCTION finance.ready_admin_payment_request(text,text,uuid,text,text);
        DROP FUNCTION finance.create_admin_payment_request(
          text,text,uuid,uuid,text,text,bigint,text,text,text);
        DROP TABLE finance.payment_notifications;
        DROP TABLE finance.payment_requests;
        """);
}
