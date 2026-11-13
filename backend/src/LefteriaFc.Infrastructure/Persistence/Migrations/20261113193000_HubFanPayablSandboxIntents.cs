using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Sandbox hosted checkout for fan HUB interest. The callback records
/// reconciliation evidence only; it cannot grant a paid subscription.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113193000_HubFanPayablSandboxIntents")]
public sealed class HubFanPayablSandboxIntents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          CREATE TABLE hub.fan_payabl_checkout_intents (
            id uuid PRIMARY KEY,
            subscriber_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
            organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT
              CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
            audience varchar(12) NOT NULL CHECK(audience IN ('fan_parent','fan_player','fan_both')),
            client_request_id uuid NOT NULL,
            amount_cents bigint NOT NULL CHECK(amount_cents BETWEEN 1 AND 100000000),
            currency char(3) NOT NULL DEFAULT 'EUR' CHECK(currency='EUR'),
            callback_digest char(64) NOT NULL UNIQUE CHECK(callback_digest ~ '^[a-f0-9]{64}$'),
            order_id varchar(30) NOT NULL UNIQUE,
            status varchar(24) NOT NULL DEFAULT 'initiating'
              CHECK(status IN ('initiating','ready','review_required','failed')),
            checkout_url varchar(2048),
            initial_transaction_id varchar(40),
            created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            UNIQUE(subscriber_identity_id,organization_id,client_request_id),
            CONSTRAINT ck_hub_fan_checkout_ready CHECK(
              status<>'ready' OR
                (checkout_url IS NOT NULL AND initial_transaction_id IS NOT NULL))
          );
          CREATE TABLE hub.fan_payabl_checkout_notices (
            intent_id uuid NOT NULL REFERENCES hub.fan_payabl_checkout_intents(id) ON DELETE RESTRICT,
            transaction_id varchar(40) NOT NULL,
            event_type varchar(20) NOT NULL,
            error_code integer NOT NULL,
            provider_timestamp bigint NOT NULL,
            amount_cents bigint,
            currency varchar(3),
            payment_state varchar(32) NOT NULL DEFAULT '',
            recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(intent_id,transaction_id,event_type,error_code,provider_timestamp,payment_state)
          );
          CREATE INDEX ix_hub_fan_checkout_identity ON hub.fan_payabl_checkout_intents(
            subscriber_identity_id,organization_id,created_at DESC);
          ALTER TABLE hub.fan_payabl_checkout_intents ENABLE ROW LEVEL SECURITY;
          ALTER TABLE hub.fan_payabl_checkout_notices ENABLE ROW LEVEL SECURITY;
          REVOKE ALL ON hub.fan_payabl_checkout_intents,hub.fan_payabl_checkout_notices FROM PUBLIC;

          CREATE FUNCTION hub.begin_mobile_fan_payabl_checkout(
            _identity uuid,_phone text,_role text,_expires timestamptz,
            _owner text,_audience text,_id uuid,_client uuid,
            _nonce_digest text,_amount bigint
          ) RETURNS jsonb
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $fan_checkout_begin$
          DECLARE v_row hub.fan_payabl_checkout_intents%ROWTYPE;
            v_created boolean:=false;
          BEGIN
            IF _identity IS NULL OR _phone IS NULL
              OR _phone !~ '^[1-9][0-9]{7,14}$'
              OR _role IS DISTINCT FROM 'fan'
              OR _expires IS NULL OR _expires<=clock_timestamp()
              OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
              OR _audience NOT IN ('fan_parent','fan_player','fan_both')
              OR _id IS NULL OR _client IS NULL
              OR _nonce_digest !~ '^[0-9a-f]{64}$'
              OR _amount NOT BETWEEN 1 AND 100000000
            THEN RAISE EXCEPTION 'fan_checkout_invalid' USING ERRCODE='22023'; END IF;
            PERFORM 1 FROM identity.mobile_identities i
              WHERE i.id=_identity AND i.normalized_phone=_phone
                AND i.role='fan' AND i.is_active FOR SHARE;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'fan_checkout_identity_denied' USING ERRCODE='42501';
            END IF;

            INSERT INTO hub.fan_payabl_checkout_intents(
              id,subscriber_identity_id,organization_id,audience,
              client_request_id,amount_cents,callback_digest,order_id)
            VALUES(_id,_identity,_owner,_audience,_client,_amount,
              _nonce_digest,'LFC'||left(replace(_id::text,'-',''),27))
            ON CONFLICT(subscriber_identity_id,organization_id,client_request_id)
              DO NOTHING RETURNING * INTO v_row;
            IF FOUND THEN v_created:=true; END IF;
            IF NOT v_created THEN
              SELECT * INTO STRICT v_row FROM hub.fan_payabl_checkout_intents
              WHERE subscriber_identity_id=_identity AND organization_id=_owner
                AND client_request_id=_client;
              IF v_row.audience<>_audience OR v_row.amount_cents<>_amount
              THEN
                RAISE EXCEPTION 'fan_checkout_idempotency_mismatch' USING ERRCODE='40001';
              END IF;
            END IF;
            RETURN jsonb_build_object('id',v_row.id,'created',v_created,
              'organization_id',_owner,'audience',v_row.audience,
              'amount_cents',v_row.amount_cents,'currency','EUR',
              'status',v_row.status,'checkout_url',v_row.checkout_url);
          END $fan_checkout_begin$;
          REVOKE ALL ON FUNCTION hub.begin_mobile_fan_payabl_checkout(
            uuid,text,text,timestamptz,text,text,uuid,uuid,text,bigint) FROM PUBLIC;

          CREATE FUNCTION hub.ready_mobile_fan_payabl_checkout(
            _identity uuid,_phone text,_role text,_expires timestamptz,
            _owner text,_id uuid,_url text,_transaction text
          ) RETURNS boolean
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $fan_checkout_ready$
          BEGIN
            IF _id IS NULL OR _url !~ '^https://' OR length(_url)>2048
              OR _transaction !~ '^[0-9]{1,40}$'
            THEN RAISE EXCEPTION 'fan_checkout_ready_invalid' USING ERRCODE='22023'; END IF;
            PERFORM 1 FROM identity.mobile_identities i
              WHERE i.id=_identity AND i.normalized_phone=_phone
                AND i.role='fan' AND i.is_active AND _role='fan'
                  AND _expires>clock_timestamp()
              FOR SHARE;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'fan_checkout_ready_denied' USING ERRCODE='42501';
            END IF;
            UPDATE hub.fan_payabl_checkout_intents
              SET status='ready',checkout_url=_url,
                initial_transaction_id=_transaction,
                updated_at=clock_timestamp()
            WHERE id=_id AND subscriber_identity_id=_identity
              AND organization_id=_owner AND status='initiating';
            IF NOT FOUND THEN
              RAISE EXCEPTION 'fan_checkout_ready_conflict' USING ERRCODE='40001';
            END IF;
            RETURN true;
          END $fan_checkout_ready$;
          REVOKE ALL ON FUNCTION hub.ready_mobile_fan_payabl_checkout(
            uuid,text,text,timestamptz,text,uuid,text,text) FROM PUBLIC;

          -- Verified provider callback is only a signal for manual/external
          -- settlement reconciliation. It cannot activate a subscription,
          -- set verified_at or supply an entitlement.
          CREATE FUNCTION hub.record_fan_payabl_checkout_notice(
            _id uuid,_nonce_digest text,_transaction text,_kind text,
            _code integer,_stamp bigint,_order text,_amount bigint,
            _currency text,_state text
          ) RETURNS boolean
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $fan_checkout_notice$
          DECLARE v_row hub.fan_payabl_checkout_intents%ROWTYPE;
          BEGIN
            IF _id IS NULL OR _nonce_digest !~ '^[0-9a-f]{64}$'
              OR _transaction !~ '^[0-9]{1,40}$'
              OR _kind NOT IN ('capture','order','refund','cft','reversal')
              OR _stamp IS NULL OR _order IS NULL
              OR _state IS NULL OR _state !~ '^[a-z_]{0,32}$'
              OR (_currency IS NOT NULL AND _currency<>'EUR')
              OR (_amount IS NOT NULL AND _amount<0)
            THEN RAISE EXCEPTION 'fan_checkout_notice_invalid' USING ERRCODE='22023'; END IF;
            SELECT * INTO STRICT v_row FROM hub.fan_payabl_checkout_intents
              WHERE id=_id AND callback_digest=_nonce_digest
                AND order_id=_order FOR UPDATE;
            IF _amount IS NOT NULL AND _amount<>v_row.amount_cents THEN
              RAISE EXCEPTION 'fan_checkout_amount_mismatch' USING ERRCODE='22023';
            END IF;
            INSERT INTO hub.fan_payabl_checkout_notices(
              intent_id,transaction_id,event_type,error_code,provider_timestamp,
              amount_cents,currency,payment_state)
            VALUES(_id,_transaction,_kind,_code,_stamp,_amount,_currency,_state)
            ON CONFLICT DO NOTHING;
            IF v_row.status IN ('initiating','ready') THEN
              UPDATE hub.fan_payabl_checkout_intents
                SET status=CASE WHEN _kind='capture' AND _code=0
                    THEN 'review_required' ELSE 'failed' END,
                    updated_at=clock_timestamp()
                WHERE id=_id;
            END IF;
            RETURN true;
          EXCEPTION WHEN no_data_found THEN
            RAISE EXCEPTION 'fan_checkout_notice_unknown' USING ERRCODE='42501';
          END $fan_checkout_notice$;
          REVOKE ALL ON FUNCTION hub.record_fan_payabl_checkout_notice(
            uuid,text,text,text,integer,bigint,text,bigint,text,text) FROM PUBLIC;
          """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DO $guard$
          BEGIN
            IF EXISTS(SELECT 1 FROM hub.fan_payabl_checkout_intents)
              OR EXISTS(SELECT 1 FROM hub.fan_payabl_checkout_notices) THEN
              RAISE EXCEPTION 'Refusing to destroy recorded fan checkout history';
            END IF;
          END $guard$;
          DROP FUNCTION hub.record_fan_payabl_checkout_notice(
            uuid,text,text,text,integer,bigint,text,bigint,text,text);
          DROP FUNCTION hub.ready_mobile_fan_payabl_checkout(
            uuid,text,text,timestamptz,text,uuid,text,text);
          DROP FUNCTION hub.begin_mobile_fan_payabl_checkout(
            uuid,text,text,timestamptz,text,text,uuid,uuid,text,bigint);
          DROP TABLE hub.fan_payabl_checkout_notices;
          DROP TABLE hub.fan_payabl_checkout_intents;
          """);
}
