using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Academy city/location authorization is checked inside PostgreSQL on every
/// supplier-payable report read. Only totals, never vendor or evidence details.
/// These are manual obligation positions, not bank activity or GL balances.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115269000_FinanceSupplierSitePayablesWithReversals")]
public sealed class FinanceSupplierSitePayablesWithReversals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION finance.read_admin_supplier_site_payables(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $payables$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner IS DISTINCT FROM 'lefteria_fc_academy'
          OR _month IS NULL OR _month NOT BETWEEN
            DATE '2020-01-01' AND DATE '2100-12-01'
          OR extract(day FROM _month)<>1
        THEN RAISE EXCEPTION 'supplier_site_payables_invalid'
          USING ERRCODE='22023'; END IF;

        -- Only locations explicitly permitted to this actor enter the
        -- projection. All amounts are calculated from bill-owned location
        -- snapshots; credit and payment records inherit that same bill owner.
        RETURN jsonb_build_object(
          'organization_id',_owner,'month',_month,'currency','EUR',
          'reporting_basis','supplier_bill_date',
          'approval_reversal_adjusted',true,
          'bank_verified',false,'is_cash_payment',false,
          'posted_to_general_ledger',false,
          'locations',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'city_id',l.city_id,'location_id',l.id,
              'bill_count',m.bill_count,
              'gross_amount_cents',m.gross_cents,
              'approved_credit_cents',m.credit_cents,
              'pending_credit_cents',m.pending_credit_cents,
              'net_payables_cents',m.gross_cents-m.credit_cents,
              'approved_manual_payment_cents',m.payment_cents,
              'pending_payment_evidence_cents',m.pending_payment_cents,
              'remaining_cents',m.gross_cents-m.credit_cents-m.payment_cents)
              ORDER BY l.city_id,l.id),'[]'::jsonb)
            FROM club.football_locations l
            JOIN club.football_operational_scopes s
              ON s.academy_city_id=l.city_id
              AND s.organization_id='lefteria_fc_academy'
            CROSS JOIN LATERAL (
              SELECT count(*)::int AS bill_count,
                coalesce(sum(b.gross_amount_cents),0)::bigint AS gross_cents,
                coalesce(sum(credits.approved_cents),0)::bigint AS credit_cents,
                coalesce(sum(credits.pending_cents),0)::bigint AS pending_credit_cents,
                coalesce(sum(payments.approved_cents),0)::bigint AS payment_cents,
                coalesce(sum(payments.pending_cents),0)::bigint AS pending_payment_cents
              FROM finance.supplier_bills b
              CROSS JOIN LATERAL (
                SELECT
                  coalesce(sum(r.amount_cents) FILTER (
                    WHERE d.decision='approved_manual'
                      AND NOT EXISTS (
                        SELECT 1 FROM finance.supplier_review_reversal_requests rev
                        JOIN finance.supplier_review_reversal_decisions revd
                          ON revd.request_id=rev.id AND revd.decision='approved_manual'
                        WHERE rev.organization_id=b.organization_id
                          AND rev.bill_id=b.id
                          AND rev.target_kind='payment'
                          AND rev.target_id=r.id
                      )),0)::bigint AS approved_cents,
                  coalesce(sum(r.amount_cents) FILTER (
                    WHERE d.request_id IS NULL),0)::bigint AS pending_cents
                FROM finance.supplier_payment_requests r
                LEFT JOIN finance.supplier_payment_decisions d
                  ON d.request_id=r.id
                WHERE r.bill_id=b.id
                  AND r.organization_id='lefteria_fc_academy'
              ) payments
              CROSS JOIN LATERAL (
                SELECT
                  coalesce(sum(c.amount_cents) FILTER (
                    WHERE d.decision='approved_manual'
                      AND NOT EXISTS (
                        SELECT 1 FROM finance.supplier_review_reversal_requests rev
                        JOIN finance.supplier_review_reversal_decisions revd
                          ON revd.request_id=rev.id AND revd.decision='approved_manual'
                        WHERE rev.organization_id=b.organization_id
                          AND rev.bill_id=b.id
                          AND rev.target_kind='credit'
                          AND rev.target_id=c.id
                      )),0)::bigint AS approved_cents,
                  coalesce(sum(c.amount_cents) FILTER (
                    WHERE d.request_id IS NULL),0)::bigint AS pending_cents
                FROM finance.supplier_credit_requests c
                LEFT JOIN finance.supplier_credit_decisions d
                  ON d.request_id=c.id
                WHERE c.bill_id=b.id
                  AND c.organization_id='lefteria_fc_academy'
              ) credits
              WHERE b.organization_id='lefteria_fc_academy'
                AND b.location_id=l.id AND b.city_id=l.city_id
                AND b.bill_date>=_month
                AND b.bill_date<(_month+interval '1 month')::date
            ) m
            WHERE club.admin_football_allowed(
              v_actor,v_role,_owner,s.id,l.id,false)
          )
        );
      END $payables$;
      REVOKE ALL ON FUNCTION finance.read_admin_supplier_site_payables(
        text,text,date) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION finance.read_admin_supplier_site_payables(text,text,date);
          """);
}
