using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Bounded, scoped POS reconciliation for an authorized assigned cashier.
/// No payment changes, private children records, or raw table grants.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110158000_PosCashierReconciliationRead")]
public sealed class PosCashierReconciliationRead : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos.read_cashier_shift_reconciliation(
        _digest text,_owner text,_shift uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $report$
      DECLARE
        v_actor uuid;
        v_shift pos.shifts%ROWTYPE;
        v_sales bigint; v_paid bigint; v_unpaid bigint;
        v_cash bigint; v_card bigint;
        v_refunds_cash bigint; v_refunds_card bigint;
        v_receipts bigint; v_reservations bigint; v_inconsistent bigint;
        v_expected_cash bigint;
        v_card_pending bigint;
      BEGIN
        IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy') OR _shift IS NULL
        THEN RAISE EXCEPTION 'pos_shift_report_invalid' USING ERRCODE='22023';
        END IF;
        SELECT * INTO v_shift FROM pos.shifts
          WHERE organization_id=_owner AND id=_shift;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'pos_shift_report_unavailable' USING ERRCODE='42501';
        END IF;
        v_actor:=pos.current_cashier_authority(
          _digest,_owner,v_shift.register_id);
        IF v_shift.cashier_id<>v_actor THEN
          RAISE EXCEPTION 'pos_shift_report_access_denied' USING ERRCODE='42501';
        END IF;
        SELECT count(*),
          count(*) FILTER(WHERE status='paid'),
          count(*) FILTER(WHERE status IN ('awaiting_payment','partially_paid'))
          INTO v_sales,v_paid,v_unpaid
        FROM pos.sales WHERE organization_id=_owner AND shift_id=_shift;
        IF v_sales>10000 THEN
          RAISE EXCEPTION 'pos_shift_report_too_large' USING ERRCODE='54000';
        END IF;
        SELECT
          coalesce(sum(p.amount_cents) FILTER(
            WHERE p.status='verified' AND p.payment_kind='cash'),0),
          coalesce(sum(p.amount_cents) FILTER(
            WHERE p.status='verified' AND p.payment_kind='card'),0),
          count(*) FILTER(
            WHERE p.status='pending' AND p.payment_kind='card')
          INTO v_cash,v_card,v_card_pending
        FROM pos.payment_events p
        JOIN pos.sales s ON s.organization_id=p.organization_id AND s.id=p.sale_id
        WHERE s.organization_id=_owner AND s.shift_id=_shift;
        SELECT
          coalesce(sum(f.amount_cents) FILTER(
            WHERE f.status='verified' AND f.refund_kind='cash'),0),
          coalesce(sum(f.amount_cents) FILTER(
            WHERE f.status='verified' AND f.refund_kind='card'),0)
          INTO v_refunds_cash,v_refunds_card
        FROM pos.refund_events f
        JOIN pos.returns r ON r.organization_id=f.organization_id
          AND r.id=f.return_id
        JOIN pos.sales s ON s.organization_id=r.organization_id
          AND s.id=r.sale_id
        WHERE s.organization_id=_owner AND s.shift_id=_shift;
        SELECT count(*) INTO v_receipts FROM pos.receipts r
          JOIN pos.sales s ON s.organization_id=r.organization_id
            AND s.id=r.sale_id
          WHERE s.organization_id=_owner AND s.shift_id=_shift;
        SELECT count(*) INTO v_reservations FROM pos.sale_reservations rs
          JOIN pos.sales s ON s.organization_id=rs.organization_id
            AND s.id=rs.sale_id
          WHERE s.organization_id=_owner AND s.shift_id=_shift
            AND rs.released_at IS NULL AND rs.consumed_at IS NULL;
        SELECT count(*) INTO v_inconsistent FROM pos.sales s
          WHERE s.organization_id=_owner AND s.shift_id=_shift
            AND (
              (s.status='paid' AND NOT EXISTS (
                SELECT 1 FROM pos.receipts r
                WHERE r.organization_id=s.organization_id AND r.sale_id=s.id))
              OR (s.status<>'paid' AND EXISTS(
                SELECT 1 FROM pos.receipts r
                WHERE r.organization_id=s.organization_id AND r.sale_id=s.id))
              OR (s.status='voided' AND EXISTS(
                SELECT 1 FROM pos.sale_reservations rs
                WHERE rs.organization_id=s.organization_id AND rs.sale_id=s.id
                  AND rs.released_at IS NULL AND rs.consumed_at IS NULL))
            );
        v_expected_cash:=v_shift.opening_cash_cents+v_cash-v_refunds_cash;
        RETURN jsonb_build_object(
          'organization_id',_owner,'register_id',v_shift.register_id,
          'shift_id',_shift,'is_closed',v_shift.closed_at IS NOT NULL,
          'sales_total',v_sales,'sales_paid',v_paid,'sales_pending',v_unpaid,
          'receipts_issued',v_receipts,'cash_verified_cents',v_cash,
          'card_verified_cents',v_card,'pending_card_events',v_card_pending,
          'cash_refunded_cents',v_refunds_cash,
          'card_refunded_cents',v_refunds_card,
          'active_reservations',v_reservations,
          'inconsistent_sales',v_inconsistent,
          'opening_cash_cents',v_shift.opening_cash_cents,
          'expected_cash_cents',v_expected_cash,
          'counted_cash_cents',v_shift.counted_cash_cents,
          'cash_variance_cents',CASE
            WHEN v_shift.counted_cash_cents IS NULL THEN NULL
            ELSE v_shift.counted_cash_cents-v_expected_cash END,
          'requires_review',v_unpaid>0 OR v_card_pending>0
            OR v_inconsistent>0 OR
            (v_shift.closed_at IS NOT NULL
              AND v_shift.counted_cash_cents<>v_expected_cash)
        );
      END $report$;

      REVOKE ALL ON FUNCTION pos.read_cashier_shift_reconciliation(
        text,text,uuid) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION pos.read_cashier_shift_reconciliation(text,text,uuid);
    """);
}
