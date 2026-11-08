using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private EUR payment-link ledger. No direct card storage, subscriptions
/// charging or unapproved payroll disbursements. Restricted functions perform
/// live admin/organization authorization on every read and mutation.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108330000_PayablFinanceGateway")]
public sealed class PayablFinanceGateway : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA IF NOT EXISTS finance;
        REVOKE ALL ON SCHEMA finance FROM PUBLIC;

        CREATE TABLE finance.payabl_payment_requests (
          id varchar(36) COLLATE "C" PRIMARY KEY
            CHECK(id ~ '^pay_[0-9a-f]{32}$'),
          organization_id varchar(32) NOT NULL
            REFERENCES club.organizations(id) ON DELETE RESTRICT
            CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
          public_token varchar(32) COLLATE "C" UNIQUE NOT NULL
            CHECK(public_token ~ '^[0-9a-f]{32}$'),
          order_id varchar(36) COLLATE "C" UNIQUE NOT NULL
            CHECK(order_id ~ '^lfc_[0-9a-f]{32}$'),
          purpose text NOT NULL
            CHECK(purpose IN ('academy_fee','subscription_invoice',
              'shop_order','event_ticket','donation','other')),
          title varchar(140) NOT NULL CHECK(length(btrim(title)) BETWEEN 2 AND 140),
          amount_cents integer NOT NULL CHECK(amount_cents BETWEEN 1 AND 10000000),
          currency char(3) NOT NULL DEFAULT 'EUR' CHECK(currency='EUR'),
          payer_first_name varchar(50) NOT NULL CHECK(length(payer_first_name)>0),
          payer_last_name varchar(50) NOT NULL CHECK(length(payer_last_name)>0),
          payer_email varchar(50) NOT NULL CHECK(position('@' in payer_email)>1),
          status text NOT NULL DEFAULT 'draft'
            CHECK(status IN ('draft','initiating','checkout_ready','paid','declined','cancelled')),
          version bigint NOT NULL DEFAULT 1 CHECK(version BETWEEN 1 AND 9007199254740991),
          gateway_transaction_id varchar(50),
          checkout_url varchar(2048),
          checkout_lease uuid,
          checkout_lease_until timestamptz,
          checkout_opened_at timestamptz,
          paid_at timestamptz,
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          created_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
          CHECK((checkout_url IS NULL OR checkout_url ~ '^https://') AND
            (gateway_transaction_id IS NULL OR gateway_transaction_id ~ '^[0-9]{1,50}$'))
        );
        CREATE INDEX ix_finance_payabl_owner_recent ON
          finance.payabl_payment_requests(organization_id,created_at DESC);
        REVOKE ALL ON TABLE finance.payabl_payment_requests FROM PUBLIC;

        CREATE TABLE finance.payabl_notification_receipts (
          signature_digest varchar(64) COLLATE "C" PRIMARY KEY
            CHECK(signature_digest ~ '^[0-9a-f]{64}$'),
          order_id varchar(36) COLLATE "C" NOT NULL,
          transaction_id varchar(50) NOT NULL,
          transaction_type varchar(30) NOT NULL,
          error_code varchar(8) NOT NULL,
          sent_at timestamptz NOT NULL,
          received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          outcome text NOT NULL CHECK(outcome IN ('paid','declined','ignored','duplicate'))
        );
        REVOKE ALL ON TABLE finance.payabl_notification_receipts FROM PUBLIC;

        CREATE FUNCTION finance.admin_payabl_actor(
          _digest text,_owner text)
        RETURNS TABLE(actor_id uuid,session_id uuid)
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $admin_finance_actor$
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
            RAISE EXCEPTION 'finance_invalid_scope' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          RETURN QUERY
            SELECT a.id,s.id
            FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            JOIN identity.admin_organization_grants g
              ON g.admin_account_id=a.id AND g.organization_id=_owner
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled
              AND a.password_hash IS NOT NULL
              AND a.role IN ('manager','super_admin')
            LIMIT 1;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'finance_permission_denied' USING ERRCODE='42501';
          END IF;
        END $admin_finance_actor$;
        REVOKE ALL ON FUNCTION finance.admin_payabl_actor(text,text) FROM PUBLIC;

        CREATE FUNCTION finance.create_admin_payabl_payment(
          _digest text,_owner text,_id text,_token text,_purpose text,
          _title text,_amount integer,_first text,_last text,_email text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $create_payment$
        DECLARE v_actor uuid;v_session uuid;
        BEGIN
          IF _id IS NULL OR _id !~ '^pay_[0-9a-f]{32}$'
            OR _token IS NULL OR _token !~ '^[0-9a-f]{32}$'
            OR _purpose NOT IN ('academy_fee','subscription_invoice',
                'shop_order','event_ticket','donation','other')
            OR _title IS NULL OR length(_title) NOT BETWEEN 2 AND 140
            OR btrim(_title)<>_title OR _title ~ '[[:cntrl:]]'
            OR _amount IS NULL OR _amount NOT BETWEEN 1 AND 10000000
            OR _first IS NULL OR length(_first) NOT BETWEEN 1 AND 50
            OR _last IS NULL OR length(_last) NOT BETWEEN 1 AND 50
            OR _email IS NULL OR length(_email) NOT BETWEEN 3 AND 50
            OR _email !~ '^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$'
            OR _first ~ '[[:cntrl:]]' OR _last ~ '[[:cntrl:]]'
            THEN RAISE EXCEPTION 'finance_invalid_payment' USING ERRCODE='22023';
          END IF;
          SELECT actor_id,session_id INTO v_actor,v_session
            FROM finance.admin_payabl_actor(_digest,_owner);
          INSERT INTO finance.payabl_payment_requests(
            id,organization_id,public_token,order_id,purpose,title,
            amount_cents,payer_first_name,payer_last_name,payer_email,created_by)
          VALUES (_id,_owner,_token,'lfc_'||substr(_id,5),
            _purpose,_title,_amount,_first,_last,_email,v_actor);
          INSERT INTO audit.security_events(id,actor_realm,occurred_at,
            actor_identity_id,device_session_id,action,outcome,
            organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            v_session,'admin.finance.payment.create','succeeded',_owner,'payabl');
          RETURN jsonb_build_object('id',_id,'public_token',_token,
            'purpose',_purpose,'title',_title,'amount_cents',_amount,
            'currency','EUR','status','draft','version',1);
        END $create_payment$;
        REVOKE ALL ON FUNCTION finance.create_admin_payabl_payment(
          text,text,text,text,text,text,integer,text,text,text) FROM PUBLIC;

        CREATE FUNCTION finance.read_admin_payabl_payments(
          _digest text,_owner text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $read_finance$
        DECLARE v_actor uuid;v_session uuid;
        BEGIN
          SELECT actor_id,session_id INTO v_actor,v_session
            FROM finance.admin_payabl_actor(_digest,_owner);
          RETURN jsonb_build_object('organization_id',_owner,
            'payments',coalesce((
              SELECT jsonb_agg(jsonb_build_object(
                'id',p.id,'public_token',p.public_token,
                'purpose',p.purpose,'title',p.title,
                'amount_cents',p.amount_cents,'currency',p.currency,
                'status',p.status,'version',p.version,
                'created_at',p.created_at,'paid_at',p.paid_at,
                'gateway_transaction_id',p.gateway_transaction_id
              ) ORDER BY p.created_at DESC,p.id DESC)
              FROM (SELECT * FROM finance.payabl_payment_requests
                WHERE organization_id=_owner
                ORDER BY created_at DESC,id DESC LIMIT 100) p
            ),'[]'::jsonb));
        END $read_finance$;
        REVOKE ALL ON FUNCTION finance.read_admin_payabl_payments(
          text,text) FROM PUBLIC;

        CREATE FUNCTION finance.claim_admin_payabl_checkout(
          _digest text,_owner text,_id text,_lease uuid)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $claim_checkout$
        DECLARE v_actor uuid;v_session uuid;v_row finance.payabl_payment_requests%ROWTYPE;
        BEGIN
          SELECT actor_id,session_id INTO v_actor,v_session
            FROM finance.admin_payabl_actor(_digest,_owner);
          IF _lease IS NULL OR _id IS NULL THEN
            RAISE EXCEPTION 'finance_invalid_checkout' USING ERRCODE='22023';
          END IF;
          UPDATE finance.payabl_payment_requests
          SET status='initiating',checkout_lease=_lease,
            checkout_lease_until=clock_timestamp()+interval '2 minutes',
            version=version+1
          WHERE id=_id AND organization_id=_owner AND status='draft'
          RETURNING * INTO v_row;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'finance_checkout_conflict' USING ERRCODE='40001';
          END IF;
          RETURN jsonb_build_object('id',v_row.id,
            'order_id',v_row.order_id,'amount_cents',v_row.amount_cents,
            'payer_first_name',v_row.payer_first_name,
            'payer_last_name',v_row.payer_last_name,
            'payer_email',v_row.payer_email,
            'lease',_lease);
        END $claim_checkout$;
        REVOKE ALL ON FUNCTION finance.claim_admin_payabl_checkout(
          text,text,text,uuid) FROM PUBLIC;

        CREATE FUNCTION finance.complete_admin_payabl_checkout(
          _digest text,_owner text,_id text,_lease uuid,
          _url text,_transaction text,_success boolean)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $complete_checkout$
        DECLARE v_actor uuid;v_session uuid;v_row finance.payabl_payment_requests%ROWTYPE;
        BEGIN
          SELECT actor_id,session_id INTO v_actor,v_session
            FROM finance.admin_payabl_actor(_digest,_owner);
          IF _success IS NULL OR
            (_success AND (_url IS NULL OR length(_url)>2048
              OR _url !~ '^https://' OR
              _transaction IS NULL OR _transaction !~ '^[0-9]{1,50}$'))
          THEN RAISE EXCEPTION 'finance_invalid_checkout' USING ERRCODE='22023';
          END IF;
          UPDATE finance.payabl_payment_requests
          SET status=CASE WHEN _success THEN 'checkout_ready' ELSE 'draft' END,
            checkout_url=CASE WHEN _success THEN _url ELSE NULL END,
            gateway_transaction_id=CASE WHEN _success THEN _transaction ELSE NULL END,
            checkout_opened_at=CASE WHEN _success THEN clock_timestamp() ELSE NULL END,
            checkout_lease=NULL,checkout_lease_until=NULL,
            version=version+1
          WHERE id=_id AND organization_id=_owner
            AND status='initiating' AND checkout_lease=_lease
            AND checkout_lease_until>clock_timestamp()
          RETURNING * INTO v_row;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'finance_checkout_conflict' USING ERRCODE='40001';
          END IF;
          INSERT INTO audit.security_events(id,actor_realm,occurred_at,
            actor_identity_id,device_session_id,action,outcome,
            organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            v_session,'admin.finance.checkout.session',
            CASE WHEN _success THEN 'succeeded' ELSE 'failed' END,
            _owner,'payabl');
          RETURN jsonb_build_object('id',v_row.id,
            'status',v_row.status,'version',v_row.version,
            'public_token',v_row.public_token);
        END $complete_checkout$;
        REVOKE ALL ON FUNCTION finance.complete_admin_payabl_checkout(
          text,text,text,uuid,text,text,boolean) FROM PUBLIC;

        CREATE FUNCTION finance.read_public_payabl_link(_token text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $public_pay_link$
        DECLARE v_row finance.payabl_payment_requests%ROWTYPE;
        BEGIN
          IF _token IS NULL OR _token !~ '^[0-9a-f]{32}$' THEN
            RAISE EXCEPTION 'finance_link_invalid' USING ERRCODE='22023';
          END IF;
          SELECT * INTO v_row
          FROM finance.payabl_payment_requests p WHERE p.public_token=_token;
          IF NOT FOUND THEN
            RETURN jsonb_build_object('available',false,'status','unavailable');
          END IF;
          RETURN jsonb_build_object('available',true,
            'organization_id',v_row.organization_id,
            'title',v_row.title,'amount_cents',v_row.amount_cents,
            'currency',v_row.currency,
            'status',v_row.status,
            'checkout_url',CASE WHEN v_row.status='checkout_ready'
              AND v_row.checkout_opened_at>clock_timestamp()-interval '30 minutes'
              THEN v_row.checkout_url ELSE NULL END);
        END $public_pay_link$;
        REVOKE ALL ON FUNCTION finance.read_public_payabl_link(text)
          FROM PUBLIC;

        CREATE FUNCTION finance.record_payabl_notification(
          _order text,_transaction text,_type text,_error text,
          _when timestamptz,_digest text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $verified_payment_callback$
        DECLARE v_row finance.payabl_payment_requests%ROWTYPE;v_result text;
        BEGIN
          -- The ASP.NET boundary verifies the provider SHA-256 using the
          -- merchant secret; the DB runtime cannot read the merchant secret.
          IF _order IS NULL OR _order !~ '^lfc_[0-9a-f]{32}$'
            OR _transaction IS NULL OR _transaction !~ '^[0-9]{1,50}$'
            OR _type IS NULL OR _type !~ '^[a-z_]{1,30}$'
            OR _error IS NULL OR _error !~ '^-?[0-9]{1,8}$'
            OR _when IS NULL OR _digest IS NULL
            OR _digest !~ '^[0-9a-f]{64}$'
          THEN RAISE EXCEPTION 'finance_invalid_callback' USING ERRCODE='22023';
          END IF;
          SELECT * INTO v_row FROM finance.payabl_payment_requests
            WHERE order_id=_order FOR UPDATE;
          IF NOT FOUND THEN
            RETURN jsonb_build_object('accepted',true,'changed',false);
          END IF;
          INSERT INTO finance.payabl_notification_receipts(
            signature_digest,order_id,transaction_id,transaction_type,
            error_code,sent_at,outcome)
          VALUES(_digest,_order,_transaction,_type,_error,_when,'ignored')
          ON CONFLICT(signature_digest) DO NOTHING;
          IF NOT FOUND THEN
            RETURN jsonb_build_object('accepted',true,'changed',false);
          END IF;
          IF v_row.status='paid' THEN
            RETURN jsonb_build_object('accepted',true,'changed',false);
          END IF;
          IF v_row.status NOT IN ('checkout_ready','declined') THEN
            RETURN jsonb_build_object('accepted',true,'changed',false);
          END IF;
          v_result:=CASE WHEN _type='capture' AND _error='0' THEN 'paid'
            WHEN _type='capture' AND _error<>'0' THEN 'declined'
            ELSE 'ignored' END;
          IF v_result='ignored' THEN
            RETURN jsonb_build_object('accepted',true,'changed',false);
          END IF;
          UPDATE finance.payabl_payment_requests SET
            status=v_result,
            paid_at=CASE WHEN v_result='paid' THEN clock_timestamp() ELSE paid_at END,
            gateway_transaction_id=CASE WHEN v_result='paid' THEN _transaction
                ELSE gateway_transaction_id END,
            version=version+1
          WHERE id=v_row.id;
          UPDATE finance.payabl_notification_receipts SET outcome=v_result
            WHERE signature_digest=_digest;
          RETURN jsonb_build_object('accepted',true,'changed',true);
        END $verified_payment_callback$;
        REVOKE ALL ON FUNCTION finance.record_payabl_notification(
          text,text,text,text,timestamptz,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION finance.record_payabl_notification(
          text,text,text,text,timestamptz,text);
        DROP FUNCTION finance.read_public_payabl_link(text);
        DROP FUNCTION finance.complete_admin_payabl_checkout(
          text,text,text,uuid,text,text,boolean);
        DROP FUNCTION finance.claim_admin_payabl_checkout(
          text,text,text,uuid);
        DROP FUNCTION finance.read_admin_payabl_payments(text,text);
        DROP FUNCTION finance.create_admin_payabl_payment(
          text,text,text,text,text,text,integer,text,text,text);
        DROP FUNCTION finance.admin_payabl_actor(text,text);
        DROP TABLE finance.payabl_notification_receipts;
        DROP TABLE finance.payabl_payment_requests;
        """);
}
