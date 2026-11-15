using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Super-Admin-only, organization-scoped fan HUB subscription totals and
/// sandbox plan metadata. No member identity, payer data or write capability.
/// A recorded "active" row is not equivalent to a verified current entitlement.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115910000_HubFanAdminOverview")]
public sealed class HubFanAdminOverview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          CREATE FUNCTION hub.read_admin_fan_subscription_overview(_digest text,_owner text)
          RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $overview$
          DECLARE
            v_actor uuid; v_role text; v_plans jsonb; v_counts jsonb;
            v_verified bigint;
          BEGIN
            IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
              OR _owner IS NULL OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
            THEN
              RAISE EXCEPTION 'invalid_fan_overview_scope' USING ERRCODE='22023';
            END IF;
            -- Revalidate the live, unrevoked administrative session and exact
            -- organization grant. Shared row locks serialize authority checks
            -- with account/session/grant revocation in another transaction.
            SELECT a.id,a.role INTO v_actor,v_role
              FROM identity.admin_sessions s
              JOIN identity.admin_accounts a ON a.id=s.admin_account_id
              JOIN identity.admin_organization_grants g
                ON g.admin_account_id=a.id AND g.organization_id=_owner
              WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                AND s.expires_at>clock_timestamp()
                AND a.is_active AND NOT a.invite_pending
                AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL
              FOR SHARE OF s,a,g;
            -- No accountant, editor or location/city manager may inspect
            -- organization-wide FAN subscription totals at this stage.
            IF v_actor IS NULL OR v_role<>'super_admin' THEN
              RAISE EXCEPTION 'fan_overview_super_admin_only' USING ERRCODE='42501';
            END IF;

            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'track',p.track,'monthly_price_cents',p.monthly_price_cents,
              'is_enabled',p.is_enabled,'currency','EUR','duration_days',30)
              ORDER BY p.track),'[]'::jsonb)
              INTO v_plans FROM hub.fan_plan_prices p
              WHERE p.organization_id=_owner;

            SELECT jsonb_build_object(
              'total_records',count(*),
              'pending',count(*) FILTER (WHERE s.status='pending'),
              'recorded_active',count(*) FILTER (WHERE s.status='active'),
              'past_due',count(*) FILTER (WHERE s.status='past_due'),
              'cancelled',count(*) FILTER (WHERE s.status='cancelled'),
              'expired',count(*) FILTER (WHERE s.status='expired'))
              INTO v_counts
              FROM hub.fan_subscriptions s WHERE s.organization_id=_owner;

            -- A recorded active flag alone is never payment proof. Count only
            -- currently valid fan identities with signed-callback ledger capture,
            -- exact original checkout binding and no recorded refund/reversal.
            SELECT count(*) INTO v_verified
              FROM hub.fan_subscriptions s
              JOIN identity.mobile_identities i
                ON i.id=s.subscriber_identity_id AND i.role='fan' AND i.is_active
              JOIN hub.fan_checkout_links link ON link.subscription_id=s.id
              JOIN finance.payment_requests p ON p.id=link.payment_request_id
              WHERE s.organization_id=_owner
                AND s.status='active' AND s.cancelled_at IS NULL
                AND s.provider='payabl' AND s.verified_at IS NOT NULL
                AND s.expires_at>clock_timestamp()
                AND p.organization_id=_owner AND p.actor_id=s.subscriber_identity_id
                AND p.status='paid' AND p.captured_transaction_id IS NOT NULL
                AND p.captured_transaction_id=s.provider_reference
                AND p.currency='EUR' AND p.amount_cents BETWEEN 100 AND 1000000
                AND p.purpose='fan_hub_'||link.track
                AND p.updated_at<=clock_timestamp()
                AND p.updated_at+interval '30 days'>clock_timestamp()
                AND NOT EXISTS (
                  SELECT 1 FROM finance.payment_notifications n
                  WHERE n.request_id=p.id AND n.event_type IN ('refund','reversal')
                    AND n.error_code=0);
            RETURN jsonb_build_object(
              'organization_id',_owner,'as_of',clock_timestamp(),
              'counts',v_counts||jsonb_build_object('verified_active',v_verified),
              'plans',v_plans);
          END $overview$;
          REVOKE ALL ON FUNCTION hub.read_admin_fan_subscription_overview(text,text)
            FROM PUBLIC;
          """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.read_admin_fan_subscription_overview(text,text);
          """);
}
