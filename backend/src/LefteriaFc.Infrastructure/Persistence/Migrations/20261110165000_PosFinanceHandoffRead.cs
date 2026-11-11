using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Aggregated outstanding POS-to-finance events for an authenticated manager.
/// The report never claims that a finance journal has been posted.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110233000_PosFinanceHandoffRead")]
public sealed class PosFinanceHandoffRead : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos_cashier.read_admin_finance_handoff(
        _digest text,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $finance_status$
      DECLARE
        v_actor uuid;
        v_role text;
        v_events bigint;
        v_sales bigint;
        v_refunds bigint;
        v_gross bigint;
        v_refunded bigint;
        v_earliest timestamptz;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
        THEN RAISE EXCEPTION 'pos_finance_handoff_scope_invalid'
          USING ERRCODE='22023'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.id,a.role INTO v_actor,v_role
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g
          ON g.admin_account_id=a.id AND g.organization_id=_owner
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL
          AND s.expires_at>clock_timestamp() AND a.is_active
          AND NOT a.invite_pending AND NOT a.otp2fa_enabled
          AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin')
        THEN RAISE EXCEPTION 'pos_finance_handoff_access_denied'
          USING ERRCODE='42501'; END IF;
        SELECT count(*),
          count(*) FILTER(WHERE event_kind='sale_receipted'),
          count(*) FILTER(WHERE event_kind='refund_verified'),
          coalesce(sum(amount_cents) FILTER(
            WHERE event_kind='sale_receipted'),0),
          coalesce(sum(amount_cents) FILTER(
            WHERE event_kind='refund_verified'),0),
          min(created_at)
          INTO v_events,v_sales,v_refunds,v_gross,v_refunded,v_earliest
        FROM pos_cashier.finance_posting_outbox WHERE organization_id=_owner;
        RETURN jsonb_build_object(
          'organization_id',_owner,
          'posting_status','awaiting_verified_finance_consumer',
          'total_events',v_events,'sale_receipt_events',v_sales,
          'verified_refund_events',v_refunds,
          'sale_gross_cents',v_gross,'refund_gross_cents',v_refunded,
          'net_event_cents',v_gross-v_refunded,
          'oldest_event_at',v_earliest,
          'journal_entries_confirmed',0,
          'finance_posting_active',false
        );
      END $finance_status$;
      REVOKE ALL ON FUNCTION pos_cashier.read_admin_finance_handoff(text,text)
        FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION pos_cashier.read_admin_finance_handoff(text,text);
    """);
}