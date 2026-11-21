using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Audit-only comparison of independent legacy POS and cashier stock ledgers.
/// No balance mutation or checkout readiness approval; a human physical count,
/// auditable opening balances and inventory event integration remain mandatory.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115955000_PosSiteStockReconciliationRead")]
public sealed class PosSiteStockReconciliationRead : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos_cashier.read_admin_site_stock_reconciliation(
        _digest text,_owner text,_location text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $stock_reconciliation$
      DECLARE
        v_actor uuid;
        v_name text;
        v_rows jsonb;
        v_total integer;
        v_matched integer;
        v_discrepant integer;
        v_legacy_missing integer;
        v_cashier_missing integer;
        v_disabled integer;
        v_reserved bigint;
        v_open_shifts bigint;
      BEGIN
        -- Re-check the current authenticated actor against this exact canonical
        -- Academy location (or the separately scoped first-team Super Admin).
        -- Cashiers, other cities, disabled sites and revoked grants are denied.
        v_actor:=pos_cashier.authorize_site_finances(_digest,_owner,_location);
        SELECT l.name INTO STRICT v_name FROM pos.locations l
          WHERE l.organization_id=_owner AND l.id=_location AND l.active;
      
        -- These two ledgers are deliberately NOT treated as one inventory.
        -- Compare exact owner+site+SKU; never mutate either stock on a GET.
        -- A missing SKU is different from a legitimate zero stock balance.
        WITH stock_lines AS (
          SELECT coalesce(legacy.product_id,cashier.product_id) AS product_id,
            coalesce(legacy.variant_id,cashier.variant_id) AS variant_id,
            legacy.on_hand AS legacy_on_hand,
            cashier.on_hand AS cashier_on_hand,
            cashier.reserved AS cashier_reserved,
            (cashier.on_hand-cashier.reserved) AS cashier_available,
            (legacy.on_hand-cashier.on_hand) AS quantity_difference,
            p.name AS product_name,v.name AS variant_name,
            coalesce(lp.enabled,false) AS location_product_enabled,
            CASE
              WHEN legacy.product_id IS NULL THEN 'legacy_missing'
              WHEN cashier.product_id IS NULL THEN 'cashier_missing'
              WHEN NOT coalesce(lp.enabled,false) THEN 'not_assigned'
              WHEN legacy.on_hand<>cashier.on_hand THEN 'quantity_mismatch'
              ELSE 'matched'
            END AS status
          FROM (
            SELECT product_id,variant_id,on_hand FROM pos.location_stock
            WHERE organization_id=_owner AND location_id=_location
          ) legacy
          FULL OUTER JOIN (
            SELECT product_id,variant_id,on_hand,reserved FROM pos_cashier.stock
            WHERE organization_id=_owner AND location_id=_location
          ) cashier USING (product_id,variant_id)
          JOIN pos.products p ON p.organization_id=_owner
            AND p.id=coalesce(legacy.product_id,cashier.product_id)
          JOIN pos.product_variants v ON v.organization_id=_owner
            AND v.product_id=p.id
            AND v.id=coalesce(legacy.variant_id,cashier.variant_id)
          LEFT JOIN pos.location_products lp ON lp.organization_id=_owner
            AND lp.location_id=_location AND lp.product_id=p.id
        )
        SELECT count(*)::integer,
          count(*) FILTER (WHERE status='matched')::integer,
          count(*) FILTER (WHERE status<>'matched')::integer,
          count(*) FILTER (WHERE status='legacy_missing')::integer,
          count(*) FILTER (WHERE status='cashier_missing')::integer,
          count(*) FILTER (WHERE NOT location_product_enabled)::integer,
          coalesce(sum(cashier_reserved),0),
          coalesce(jsonb_agg(jsonb_build_object(
            'product_id',product_id,'variant_id',variant_id,
            'product_name',product_name,'variant_name',variant_name,
            'legacy_on_hand',legacy_on_hand,
            'cashier_on_hand',cashier_on_hand,
            'cashier_reserved',cashier_reserved,
            'cashier_available',cashier_available,
            'quantity_difference',quantity_difference,
            'location_product_enabled',location_product_enabled,
            'status',status)
            ORDER BY product_name,variant_name,product_id,variant_id),'[]'::jsonb)
          INTO v_total,v_matched,v_discrepant,v_legacy_missing,
            v_cashier_missing,v_disabled,v_reserved,v_rows
        FROM stock_lines;
      
        IF v_total>150 THEN
          RAISE EXCEPTION 'pos_site_stock_report_too_large'
            USING ERRCODE='54000';
        END IF;
      
        SELECT count(*) INTO v_open_shifts
        FROM pos_cashier.shifts sh
        JOIN pos_cashier.registers r ON r.organization_id=sh.organization_id
          AND r.id=sh.register_id
        WHERE sh.organization_id=_owner AND r.location_id=_location
          AND sh.closed_at IS NULL;
      
        RETURN jsonb_build_object(
          'organization_id',_owner,'pos_location_id',_location,
          'location_name',v_name,'limit',150,
          'total_skus',v_total,'matched_skus',v_matched,
          'discrepant_skus',v_discrepant,
          'legacy_missing_skus',v_legacy_missing,
          'cashier_missing_skus',v_cashier_missing,
          'disabled_assignments',v_disabled,
          'reserved_units',v_reserved,'open_shifts',v_open_shifts,
          'ledger_balances_match',v_total>0 AND v_discrepant=0,
          -- Equal balances do NOT prove physically verified opening stock or
          -- authorized inventory transfers. Never label a GET a launch sign-off.
          'physical_count_approved',false,
          'stock_ready_for_trading',false,
          'lines',v_rows);
      END $stock_reconciliation$;
      REVOKE ALL ON FUNCTION pos_cashier.read_admin_site_stock_reconciliation(
        text,text,text) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION pos_cashier.read_admin_site_stock_reconciliation(text,text,text);
    """);
}
