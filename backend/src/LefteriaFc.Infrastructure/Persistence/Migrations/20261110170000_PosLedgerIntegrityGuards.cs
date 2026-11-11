using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Transaction-level POS integrity: exact receipt totals, serialized stock
/// changes and evidence-linked settlement ceilings. No checkout API grants.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110170000_PosLedgerIntegrityGuards")]
public sealed class PosLedgerIntegrityGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Deferred check allows the future single-transaction checkout to add
      -- the sale header then 1–100 lines, without permitting mismatched totals.
      CREATE FUNCTION pos.verify_sale_line_total()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      DECLARE
        v_sale uuid;
        v_total bigint;
        v_actual bigint;
        v_count integer;
      BEGIN
        v_sale:=CASE WHEN TG_TABLE_NAME='sales' THEN NEW.id ELSE NEW.sale_id END;
        SELECT total_cents INTO v_total FROM pos.sales WHERE id=v_sale;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'pos_sale_missing' USING ERRCODE='23503';
        END IF;
        SELECT count(*),coalesce(sum(line_total_cents),0)
          INTO v_count,v_actual FROM pos.sale_lines WHERE sale_id=v_sale;
        IF v_count NOT BETWEEN 1 AND 100 OR v_total<>v_actual THEN
          RAISE EXCEPTION 'pos_sale_total_mismatch' USING ERRCODE='23514';
        END IF;
        RETURN NULL;
      END $$;
      CREATE CONSTRAINT TRIGGER pos_verify_sale_header
        AFTER INSERT ON pos.sales
        DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
        EXECUTE FUNCTION pos.verify_sale_line_total();
      CREATE CONSTRAINT TRIGGER pos_verify_sale_lines
        AFTER INSERT ON pos.sale_lines
        DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
        EXECUTE FUNCTION pos.verify_sale_line_total();

      -- A stock event must match the actual locked row. History becomes the
      -- evidence, not an unaudited mutable quantity adjustment.
      CREATE FUNCTION pos.apply_stock_movement()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      DECLARE v_on_hand bigint;
      BEGIN
        SELECT on_hand INTO v_on_hand FROM pos.location_stock
          WHERE organization_id=NEW.organization_id
            AND location_id=NEW.location_id
            AND product_id=NEW.product_id
            AND variant_id=NEW.variant_id
          FOR UPDATE;
        IF NOT FOUND OR v_on_hand<>NEW.before_quantity
          OR v_on_hand+NEW.delta<>NEW.after_quantity
          OR NEW.after_quantity>100000000 THEN
          RAISE EXCEPTION 'pos_stock_conflict' USING ERRCODE='23505';
        END IF;
        UPDATE pos.location_stock
          SET on_hand=NEW.after_quantity,version=version+1
          WHERE organization_id=NEW.organization_id
            AND location_id=NEW.location_id
            AND product_id=NEW.product_id
            AND variant_id=NEW.variant_id;
        RETURN NEW;
      END $$;
      CREATE TRIGGER pos_stock_movement_apply
        BEFORE INSERT ON pos.stock_movements FOR EACH ROW
        EXECUTE FUNCTION pos.apply_stock_movement();

      -- One sale row is the serialization lock for all cash/card evidence
      -- and refunds. Provider captures still require a separately authorized,
      -- verified integration rather than a caller-supplied payment flag.
      CREATE FUNCTION pos.guard_sale_payment_event()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      DECLARE
        v_sale_total bigint;
        v_captured bigint;
        v_refunded bigint;
        v_original pos.sale_payment_events%ROWTYPE;
      BEGIN
        SELECT total_cents INTO v_sale_total FROM pos.sales
          WHERE id=NEW.sale_id FOR UPDATE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'pos_sale_not_found' USING ERRCODE='23503';
        END IF;
        SELECT
          coalesce(sum(amount_cents) FILTER(
            WHERE event_type IN ('cash_received','card_capture')),0),
          coalesce(sum(amount_cents) FILTER(
            WHERE event_type IN ('cash_refund','card_refund')),0)
          INTO v_captured,v_refunded
          FROM pos.sale_payment_events WHERE sale_id=NEW.sale_id;
        IF NEW.event_type IN ('cash_received','card_capture') THEN
          IF v_captured+NEW.amount_cents>v_sale_total THEN
            RAISE EXCEPTION 'pos_overpayment_denied' USING ERRCODE='23514';
          END IF;
        ELSE
          SELECT * INTO v_original FROM pos.sale_payment_events
            WHERE id=NEW.reversed_event_id AND sale_id=NEW.sale_id;
          IF NOT FOUND OR
             (NEW.event_type='cash_refund' AND
              v_original.event_type<>'cash_received') OR
             (NEW.event_type='card_refund' AND
              v_original.event_type<>'card_capture') OR
             NEW.amount_cents>v_original.amount_cents OR
             v_refunded+NEW.amount_cents>v_captured THEN
            RAISE EXCEPTION 'pos_refund_evidence_invalid' USING ERRCODE='23514';
          END IF;
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER pos_sale_payment_guard
        BEFORE INSERT ON pos.sale_payment_events FOR EACH ROW
        EXECUTE FUNCTION pos.guard_sale_payment_event();

      REVOKE ALL ON FUNCTION pos.verify_sale_line_total() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.apply_stock_movement() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.guard_sale_payment_event() FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP TRIGGER pos_sale_payment_guard ON pos.sale_payment_events;
      DROP TRIGGER pos_stock_movement_apply ON pos.stock_movements;
      DROP TRIGGER pos_verify_sale_lines ON pos.sale_lines;
      DROP TRIGGER pos_verify_sale_header ON pos.sales;
      DROP FUNCTION pos.guard_sale_payment_event();
      DROP FUNCTION pos.apply_stock_movement();
      DROP FUNCTION pos.verify_sale_line_total();
    """);
}