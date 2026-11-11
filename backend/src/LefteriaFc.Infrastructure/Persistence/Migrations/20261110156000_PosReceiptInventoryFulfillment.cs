using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Commit reserved inventory only when a verified paid sale receives its
/// unique receipt, in the same PostgreSQL transaction. No runtime grants.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110156000_PosReceiptInventoryFulfillment")]
public sealed class PosReceiptInventoryFulfillment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos.fulfill_receipt_reserved_inventory()
      RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,pg_temp AS $fulfill$
      DECLARE
        v_sale pos.sales%ROWTYPE;
        v_line_count integer;
        v_reserved_count integer;
        v_row record;
      BEGIN
        SELECT * INTO v_sale FROM pos.sales
          WHERE organization_id=NEW.organization_id AND id=NEW.sale_id FOR UPDATE;
        IF NOT FOUND OR v_sale.status<>'paid' THEN
          RAISE EXCEPTION 'inventory_fulfillment_requires_paid_sale'
            USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_line_count FROM pos.sale_lines
          WHERE organization_id=NEW.organization_id AND sale_id=NEW.sale_id;
        SELECT count(*) INTO v_reserved_count FROM pos.sale_reservations
          WHERE organization_id=NEW.organization_id AND sale_id=NEW.sale_id
            AND released_at IS NULL AND consumed_at IS NULL;
        -- Count equality alone is insufficient: every reserved line must
        -- refer to the exact priced product, variant and quantity sold.
        IF EXISTS(
          SELECT 1 FROM pos.sale_reservations rs
          JOIN pos.sale_lines sl ON sl.sale_id=rs.sale_id
            AND sl.line_number=rs.line_number
          WHERE rs.organization_id=NEW.organization_id
            AND rs.sale_id=NEW.sale_id
            AND (rs.product_id<>sl.product_id
              OR rs.variant_id<>sl.variant_id
              OR rs.quantity<>sl.quantity)
        ) THEN
          RAISE EXCEPTION 'inventory_receipt_reservation_line_mismatch'
            USING ERRCODE='23514';
        END IF;
        IF v_line_count=0 OR v_line_count<>v_reserved_count THEN
          RAISE EXCEPTION 'inventory_receipt_reservations_incomplete'
            USING ERRCODE='23514';
        END IF;
        FOR v_row IN
          SELECT rs.location_id,rs.product_id,rs.variant_id,rs.quantity,
                 rs.line_number,rs.sale_id
          FROM pos.sale_reservations rs
          WHERE rs.organization_id=NEW.organization_id AND rs.sale_id=NEW.sale_id
            AND rs.released_at IS NULL AND rs.consumed_at IS NULL
          ORDER BY rs.location_id,rs.product_id,rs.variant_id
          FOR UPDATE
        LOOP
          UPDATE pos.stock
            SET on_hand=on_hand-v_row.quantity,
                reserved=reserved-v_row.quantity,
                version=version+1
            WHERE organization_id=NEW.organization_id
              AND location_id=v_row.location_id
              AND product_id=v_row.product_id
              AND variant_id=v_row.variant_id
              AND reserved>=v_row.quantity AND on_hand>=v_row.quantity;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'inventory_receipt_stock_mismatch'
              USING ERRCODE='23514';
          END IF;
          UPDATE pos.sale_reservations SET consumed_at=clock_timestamp()
            WHERE sale_id=v_row.sale_id AND line_number=v_row.line_number
              AND released_at IS NULL AND consumed_at IS NULL;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'inventory_receipt_double_consumption'
              USING ERRCODE='23505';
          END IF;
        END LOOP;
        RETURN NEW;
      END $fulfill$;

      CREATE TRIGGER pos_receipt_fulfill_inventory
        AFTER INSERT ON pos.receipts FOR EACH ROW
        EXECUTE FUNCTION pos.fulfill_receipt_reserved_inventory();
      REVOKE ALL ON FUNCTION pos.fulfill_receipt_reserved_inventory() FROM PUBLIC;
    """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos.sale_reservations
          WHERE consumed_at IS NOT NULL) THEN
          RAISE EXCEPTION 'fulfilled_stock_requires_forward_recovery'; END IF;
      END $guard$;
      DROP TRIGGER pos_receipt_fulfill_inventory ON pos.receipts;
      DROP FUNCTION pos.fulfill_receipt_reserved_inventory();
    """);
}