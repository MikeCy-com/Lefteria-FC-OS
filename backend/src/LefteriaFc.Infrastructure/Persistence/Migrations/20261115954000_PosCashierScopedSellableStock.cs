using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private, read-only, site-specific cashier inventory. A named user must
/// own the exact open shift and assigned terminal. Pricing remains server
/// authoritative and checkout remains disabled without explicit approval.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115954000_PosCashierScopedSellableStock")]
public sealed class PosCashierScopedSellableStock : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos_cashier.read_assigned_cashier_sellable_stock(
        _digest text,_owner text,_register text,_shift uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $sellable$
      DECLARE
        v_actor uuid;
        v_location text;
        v_location_name text;
        v_items jsonb;
        v_fiscal_policies_approved boolean;
      BEGIN
        IF _shift IS NULL OR _shift='00000000-0000-0000-0000-000000000000'::uuid
        THEN RAISE EXCEPTION 'cashier_catalog_shift_required' USING ERRCODE='22023'; END IF;
        v_actor:=pos_cashier.current_cashier_authority(_digest,_owner,_register);
        SELECT r.location_id,l.name INTO v_location,v_location_name
          FROM pos_cashier.registers r
          JOIN pos.locations l ON l.organization_id=r.organization_id
            AND l.id=r.location_id AND l.active
          WHERE r.organization_id=_owner AND r.id=_register AND r.is_active
          FOR SHARE OF r,l;
        IF NOT FOUND THEN RAISE EXCEPTION 'cashier_catalog_register_unavailable'
          USING ERRCODE='42501'; END IF;
        PERFORM 1 FROM pos_cashier.shifts sh
          WHERE sh.id=_shift AND sh.organization_id=_owner
            AND sh.register_id=_register AND sh.cashier_id=v_actor
            AND sh.closed_at IS NULL FOR SHARE;
        IF NOT FOUND THEN RAISE EXCEPTION 'cashier_catalog_shift_not_owned'
          USING ERRCODE='42501'; END IF;
      
        -- Checkout stock is the dedicated pos_cashier.stock inventory (not a
        -- fabricated merge with pos.location_stock). Never expose supplier cost,
        -- other sites, parent/child details or pending payment credentials.
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'product_id',items.product_id,'variant_id',items.variant_id,
          'product_name',items.product_name,'variant_name',items.variant_name,
          'price_cents',items.price_cents,'available',items.available)
          ORDER BY items.product_name,items.variant_name,items.product_id,items.variant_id),
          '[]'::jsonb) INTO v_items
        FROM (
          SELECT st.product_id,st.variant_id,p.name AS product_name,
            v.name AS variant_name,v.price_cents,st.on_hand-st.reserved AS available
          FROM pos_cashier.stock st
          JOIN pos.products p ON p.organization_id=st.organization_id
            AND p.id=st.product_id AND p.active
          JOIN pos.categories c ON c.organization_id=p.organization_id
            AND c.id=p.category_id AND c.active
          JOIN pos.product_variants v ON v.organization_id=st.organization_id
            AND v.product_id=st.product_id AND v.id=st.variant_id
            AND v.price_cents>0
          WHERE st.organization_id=_owner AND st.location_id=v_location
          ORDER BY p.name,v.name,st.product_id,st.variant_id
          LIMIT 150
        ) items;
      
        SELECT EXISTS (
          SELECT 1 FROM pos_cashier.checkout_policies p
          WHERE p.organization_id=_owner AND p.enabled
            AND p.tax_treatment='verified_exempt'
            AND p.reviewed_at<=clock_timestamp()
        ) AND EXISTS (
          SELECT 1 FROM pos_cashier.receipt_policies p
          WHERE p.organization_id=_owner AND p.enabled
            AND p.reviewed_at<=clock_timestamp()
        ) INTO v_fiscal_policies_approved;
        RETURN jsonb_build_object(
          'organization_id',_owner,'register_id',_register,'shift_id',_shift,
          'location_id',v_location,'location_name',v_location_name,
          'fiscal_policies_approved',v_fiscal_policies_approved,
          'items',v_items,'limit',150);
      END $sellable$;
      REVOKE ALL ON FUNCTION pos_cashier.read_assigned_cashier_sellable_stock(
        text,text,text,uuid) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION pos_cashier.read_assigned_cashier_sellable_stock(
        text,text,text,uuid);
    """);
}
