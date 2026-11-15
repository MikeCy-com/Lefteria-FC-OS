using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Read-only, bounded, site-authorized POS transaction history.
/// A pending sale is not a payment or fiscal receipt.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115912000_PosSiteSalesHistory")]
public sealed class PosSiteSalesHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos_cashier.read_admin_pos_site_sales(
        _digest text,_owner text,_location text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $site_sales$
      DECLARE v_actor uuid;v_history jsonb;
      BEGIN
        v_actor:=pos_cashier.authorize_site_finances(_digest,_owner,_location);
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'sale_id',history.id,'register_id',history.register_id,
          'register_label',history.label,'status',history.status,
          'total_cents',history.total_cents,'created_at',history.created_at,
          'paid_at',history.paid_at,'receipt_number',history.receipt_number,
          'receipt_id',history.receipt_id)
          ORDER BY history.created_at DESC,history.id DESC),'[]'::jsonb)
          INTO v_history
        FROM (
          SELECT s.id,s.register_id,r.label,s.status,s.total_cents,
            s.created_at,s.paid_at,receipt.receipt_number,
            receipt.id AS receipt_id
          FROM pos_cashier.sales s
          JOIN pos_cashier.registers r
            ON r.organization_id=s.organization_id
              AND r.id=s.register_id AND r.location_id=_location
          LEFT JOIN pos_cashier.receipts receipt
            ON receipt.organization_id=s.organization_id
              AND receipt.sale_id=s.id
          WHERE s.organization_id=_owner
          ORDER BY s.created_at DESC,s.id DESC
          LIMIT 50
        ) history;
        RETURN jsonb_build_object('organization_id',_owner,
          'pos_location_id',_location,'limit',50,'sales',v_history);
      END $site_sales$;
      REVOKE ALL ON FUNCTION pos_cashier.read_admin_pos_site_sales(
        text,text,text) FROM PUBLIC;
    """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION pos_cashier.read_admin_pos_site_sales(text,text,text);
    """);
}
