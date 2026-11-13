using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Sandbox Payabl fan checkout. Prices are provisioned by an operator; no
/// client-supplied price or self-reported successful payment grants access.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113170000_HubFanPayablCheckout")]
public sealed class HubFanPayablCheckout : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          CREATE TABLE hub.fan_plan_prices (
            organization_id varchar(32) NOT NULL REFERENCES club.organizations(id),
            track varchar(8) NOT NULL CHECK(track IN ('parent','player','both')),
            monthly_price_cents bigint NOT NULL CHECK(monthly_price_cents BETWEEN 100 AND 1000000),
            is_enabled boolean NOT NULL DEFAULT false,
            updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(organization_id,track)
          );
          -- No prices are seeded, and the runtime cannot change pricing.
          -- Provision and enable plans deliberately in an isolated sandbox.
          CREATE TABLE hub.fan_checkout_links (
            subscription_id uuid PRIMARY KEY REFERENCES hub.fan_subscriptions(id),
            payment_request_id uuid NOT NULL UNIQUE REFERENCES finance.payment_requests(id),
            track varchar(8) NOT NULL CHECK(track IN ('parent','player','both')),
            created_at timestamptz NOT NULL DEFAULT clock_timestamp()
          );
          ALTER TABLE hub.fan_plan_prices ENABLE ROW LEVEL SECURITY;
          ALTER TABLE hub.fan_checkout_links ENABLE ROW LEVEL SECURITY;
          REVOKE ALL ON hub.fan_plan_prices,hub.fan_checkout_links FROM PUBLIC;

          CREATE FUNCTION hub.assert_mobile_fan(
            _identity uuid,_phone text,_role text,_expires timestamptz)
          RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $fan_identity$
          BEGIN
            IF _identity IS NULL OR _phone IS NULL
              OR _phone !~ '^[1-9][0-9]{7,14}$'
              OR _role IS DISTINCT FROM 'fan'
              OR _expires IS NULL OR _expires<=clock_timestamp()
            THEN RAISE EXCEPTION 'fan_scope_denied' USING ERRCODE='42501'; END IF;
            PERFORM 1 FROM identity.mobile_identities i
              WHERE i.id=_identity AND i.normalized_phone=_phone
                AND i.role='fan' AND i.is_active FOR SHARE;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'fan_identity_denied' USING ERRCODE='42501';
            END IF;
          END $fan_identity$;
          REVOKE ALL ON FUNCTION hub.assert_mobile_fan(
            uuid,text,text,timestamptz) FROM PUBLIC;

          -- Called on reads and during a new checkout to reconcile ONLY
          -- previously Payabl-captured, fully bound finance ledger payments.
          -- Checkout redirects, client tokens and unsigned browser messages
          -- can never activate a subscription.
          CREATE FUNCTION hub.sync_mobile_fan_payabl(
            _identity uuid,_owner text)
          RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $fan_sync$
          BEGIN
            UPDATE hub.fan_subscriptions s
              SET status='active',provider_reference=p.captured_transaction_id,
                verified_at=p.updated_at,expires_at=p.updated_at+interval '30 days'
            FROM hub.fan_checkout_links link
            JOIN finance.payment_requests p ON p.id=link.payment_request_id
            WHERE s.id=link.subscription_id
              AND s.subscriber_identity_id=_identity
              AND s.organization_id=_owner
              AND s.status='pending' AND s.cancelled_at IS NULL
              AND p.organization_id=_owner AND p.actor_id=_identity
              AND p.status='paid' AND p.captured_transaction_id IS NOT NULL
              AND p.currency='EUR'
              -- Original checkout amount was immutable and server-priced;
              -- later operator price changes never change an old invoice.
              AND p.amount_cents BETWEEN 100 AND 1000000
              AND p.purpose='fan_hub_'||link.track
              AND p.updated_at<=clock_timestamp()
              AND p.updated_at+interval '30 days'>clock_timestamp()
              AND NOT EXISTS (
                SELECT 1 FROM finance.payment_notifications refund
                WHERE refund.request_id=p.id
                  AND refund.event_type IN ('refund','reversal')
                  AND refund.error_code=0);
            -- Any Payabl-signed successful refund or reversal revokes
            -- already paid fan access even when finance retains its
            -- historical "paid" capture ledger state.
            UPDATE hub.fan_subscriptions s
              SET status='cancelled',cancelled_at=clock_timestamp()
            FROM hub.fan_checkout_links link
            JOIN finance.payment_requests p ON p.id=link.payment_request_id
            WHERE s.id=link.subscription_id
              AND s.subscriber_identity_id=_identity
              AND s.organization_id=_owner AND s.status='active'
              AND EXISTS (
                SELECT 1 FROM finance.payment_notifications refund
                WHERE refund.request_id=p.id
                  AND refund.event_type IN ('refund','reversal')
                  AND refund.error_code=0);
            UPDATE hub.fan_subscriptions s SET status='expired'
              WHERE s.subscriber_identity_id=_identity
                AND s.organization_id=_owner AND s.status='active'
                AND s.expires_at<=clock_timestamp();
          END $fan_sync$;
          REVOKE ALL ON FUNCTION hub.sync_mobile_fan_payabl(uuid,text) FROM PUBLIC;

          CREATE FUNCTION hub.read_mobile_fan_subscription(
            _identity uuid,_phone text,_role text,_expires timestamptz,_owner text)
          RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $fan_status$
          DECLARE v_status jsonb;
          BEGIN
            PERFORM hub.assert_mobile_fan(_identity,_phone,_role,_expires);
            IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
              RAISE EXCEPTION 'fan_owner_denied' USING ERRCODE='42501';
            END IF;
            PERFORM hub.sync_mobile_fan_payabl(_identity,_owner);
            SELECT jsonb_build_object(
              'organization_id',_owner,'status',s.status,
              'audience_parent',s.audience_parent,
              'audience_player',s.audience_player,
              'expires_at',s.expires_at,
              'checkout_url',CASE WHEN p.status='ready'
                AND (p.checkout_url LIKE 'https://sandbox.payabl.com/%'
                  OR p.checkout_url LIKE 'https://pay4.sandbox.payabl.com/%')
                  THEN p.checkout_url ELSE NULL END)
              INTO v_status
              FROM hub.fan_subscriptions s
              JOIN hub.fan_checkout_links link ON link.subscription_id=s.id
              JOIN finance.payment_requests p ON p.id=link.payment_request_id
              WHERE s.subscriber_identity_id=_identity
                AND s.organization_id=_owner
                AND s.status IN ('pending','active','past_due')
              ORDER BY s.created_at DESC LIMIT 1;
            RETURN coalesce(v_status,jsonb_build_object(
              'organization_id',_owner,'status','none',
              'audience_parent',false,'audience_player',false,
              'expires_at',NULL,'checkout_url',NULL));
          END $fan_status$;
          REVOKE ALL ON FUNCTION hub.read_mobile_fan_subscription(
            uuid,text,text,timestamptz,text) FROM PUBLIC;

          CREATE FUNCTION hub.read_mobile_fan_plans(
            _identity uuid,_phone text,_role text,_expires timestamptz,_owner text
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $fan_plans$
          DECLARE v_items jsonb;
          BEGIN
            PERFORM hub.assert_mobile_fan(_identity,_phone,_role,_expires);
            IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
              RAISE EXCEPTION 'fan_plan_scope_denied' USING ERRCODE='42501';
            END IF;
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'track',p.track,'monthly_price_cents',p.monthly_price_cents,
              'currency','EUR','duration_days',30)
              ORDER BY p.track),'[]'::jsonb)
            INTO v_items FROM hub.fan_plan_prices p
            WHERE p.organization_id=_owner AND p.is_enabled;
            RETURN jsonb_build_object('organization_id',_owner,'plans',v_items);
          END $fan_plans$;
          REVOKE ALL ON FUNCTION hub.read_mobile_fan_plans(
            uuid,text,text,timestamptz,text) FROM PUBLIC;

          CREATE FUNCTION hub.reserve_mobile_fan_payabl(
            _identity uuid,_phone text,_role text,_expires timestamptz,
            _owner text,_track text,_expected_cents bigint,_payment uuid,_nonce_digest text,
            _email text,_first text,_last text)
          RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $fan_reserve$
          DECLARE v_price bigint; v_previous hub.fan_subscriptions%ROWTYPE;
            v_id uuid; v_status text;
          BEGIN
            PERFORM hub.assert_mobile_fan(_identity,_phone,_role,_expires);
            IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
              OR _track NOT IN ('parent','player','both')
              OR _payment IS NULL OR _nonce_digest !~ '^[0-9a-f]{64}$'
              OR _email IS NULL OR length(_email) NOT BETWEEN 3 AND 50
              OR _email !~ '^[ -~]+$' OR position('@' in _email)=0
              OR _first IS NULL OR length(_first) NOT BETWEEN 1 AND 50
              OR _first !~ '^[ -~]+$'
              OR _last IS NULL OR length(_last) NOT BETWEEN 1 AND 50
              OR _last !~ '^[ -~]+$'
            THEN RAISE EXCEPTION 'fan_checkout_invalid' USING ERRCODE='22023'; END IF;
            SELECT monthly_price_cents INTO v_price
              FROM hub.fan_plan_prices WHERE organization_id=_owner
                AND track=_track AND is_enabled FOR SHARE;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'fan_plan_unavailable' USING ERRCODE='42501';
            END IF;
            IF _expected_cents IS NULL OR _expected_cents<>v_price THEN
              RAISE EXCEPTION 'fan_price_changed' USING ERRCODE='40001';
            END IF;
            -- Serialize concurrent duplicate checkout attempts by fan identity.
            PERFORM 1 FROM identity.mobile_identities i
              WHERE i.id=_identity FOR UPDATE;
            PERFORM hub.sync_mobile_fan_payabl(_identity,_owner);
            SELECT * INTO v_previous FROM hub.fan_subscriptions
              WHERE subscriber_identity_id=_identity AND organization_id=_owner
                AND status IN ('pending','active','past_due')
              ORDER BY created_at DESC LIMIT 1 FOR UPDATE;
            IF FOUND THEN
              RETURN jsonb_build_object('id',v_previous.id,
                'status',v_previous.status,'created',false);
            END IF;
            v_id:=gen_random_uuid();
            INSERT INTO finance.payment_requests(
              id,organization_id,actor_id,client_request_id,order_id,purpose,
              amount_cents,currency,payer_email,payer_first_name,payer_last_name,
              callback_digest,status)
            VALUES(_payment,_owner,_identity,_payment,
              'LFC'||substring(replace(_payment::text,'-',''),1,27),
              'fan_hub_'||_track,v_price,'EUR',_email,_first,_last,
              _nonce_digest,'initiating');
            INSERT INTO hub.fan_subscriptions(
              id,subscriber_identity_id,organization_id,status,
              audience_parent,audience_player,provider,consent_at)
            VALUES(v_id,_identity,_owner,'pending',
              _track IN ('parent','both'),_track IN ('player','both'),
              'payabl',clock_timestamp());
            INSERT INTO hub.fan_checkout_links(subscription_id,payment_request_id,track)
              VALUES(v_id,_payment,_track);
            RETURN jsonb_build_object('id',v_id,'status','pending','created',true,
              'payment_id',_payment,'amount_cents',v_price);
          END $fan_reserve$;
          REVOKE ALL ON FUNCTION hub.reserve_mobile_fan_payabl(
            uuid,text,text,timestamptz,text,text,bigint,uuid,text,text,text,text) FROM PUBLIC;

          CREATE FUNCTION hub.ready_mobile_fan_payabl(
            _identity uuid,_phone text,_role text,_expires timestamptz,
            _owner text,_payment uuid,_url text,_transaction text)
          RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $fan_ready$
          BEGIN
            PERFORM hub.assert_mobile_fan(_identity,_phone,_role,_expires);
            IF _url !~ '^https://(sandbox[.]payabl[.]com|pay4[.]sandbox[.]payabl[.]com)/'
              OR _transaction !~ '^[0-9]{1,40}$'
              OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
              RAISE EXCEPTION 'fan_ready_invalid' USING ERRCODE='22023';
            END IF;
            UPDATE finance.payment_requests p
              SET status='ready',checkout_url=_url,
                initial_transaction_id=_transaction,updated_at=clock_timestamp()
              FROM hub.fan_checkout_links link
              JOIN hub.fan_subscriptions s ON s.id=link.subscription_id
              WHERE p.id=_payment AND p.id=link.payment_request_id
                AND p.actor_id=_identity AND p.organization_id=_owner
                AND s.subscriber_identity_id=_identity AND s.status='pending'
                AND p.status='initiating';
            RETURN FOUND;
          END $fan_ready$;
          REVOKE ALL ON FUNCTION hub.ready_mobile_fan_payabl(
            uuid,text,text,timestamptz,text,uuid,text,text) FROM PUBLIC;
          """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.ready_mobile_fan_payabl(uuid,text,text,timestamptz,text,uuid,text,text);
          DROP FUNCTION hub.read_mobile_fan_plans(
            uuid,text,text,timestamptz,text);
          DROP FUNCTION hub.reserve_mobile_fan_payabl(uuid,text,text,timestamptz,text,text,bigint,uuid,text,text,text,text);
          DROP FUNCTION hub.read_mobile_fan_subscription(uuid,text,text,timestamptz,text);
          DROP FUNCTION hub.sync_mobile_fan_payabl(uuid,text);
          DROP FUNCTION hub.assert_mobile_fan(uuid,text,text,timestamptz);
          DROP TABLE hub.fan_checkout_links;
          DROP TABLE hub.fan_plan_prices;
          """);
}
