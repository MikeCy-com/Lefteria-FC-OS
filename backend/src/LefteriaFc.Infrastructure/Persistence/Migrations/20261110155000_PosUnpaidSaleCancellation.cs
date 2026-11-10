using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Idempotent, inventory-safe cancellation of uncollected POS sales.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110155000_PosUnpaidSaleCancellation")]
public sealed class PosUnpaidSaleCancellation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE pos.sales ADD COLUMN void_request_id uuid;
      CREATE UNIQUE INDEX uq_pos_void_request
        ON pos.sales(organization_id,void_request_id)
        WHERE void_request_id IS NOT NULL;
      ALTER TABLE pos.transaction_audit
        DROP CONSTRAINT transaction_audit_action_check;
      ALTER TABLE pos.transaction_audit
        ADD CONSTRAINT transaction_audit_action_check CHECK(action IN (
          'shift_open','shift_close','sale_create','sale_void','cash_verify',
          'card_capture_verify','sale_paid','receipt_issue','return_request',
          'return_approve','return_reject','refund_verify'));

      CREATE FUNCTION pos.cancel_unpaid_cashier_sale(
        _digest text,_owner text,_sale uuid,_request uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $cancel$
      DECLARE v_sale pos.sales%ROWTYPE; v_actor uuid; v_row record;
        v_register text; v_shift uuid;
      BEGIN
        IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _sale IS NULL OR _request IS NULL
          OR _request='00000000-0000-0000-0000-000000000000'::uuid
        THEN RAISE EXCEPTION 'sale_cancel_invalid' USING ERRCODE='22023'; END IF;

        SELECT s.register_id,s.shift_id INTO v_register,v_shift FROM pos.sales s
          WHERE organization_id=_owner AND id=_sale;
        IF NOT FOUND THEN RAISE EXCEPTION 'sale_cancel_unavailable' USING ERRCODE='42501'; END IF;
        v_actor:=pos.current_cashier_authority(_digest,_owner,v_register);
        PERFORM 1 FROM pos.shifts sh WHERE sh.id=v_shift AND sh.organization_id=_owner
          AND sh.register_id=v_register AND sh.cashier_id=v_actor FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'sale_cancel_wrong_cashier' USING ERRCODE='42501'; END IF;
        SELECT * INTO v_sale FROM pos.sales s
          WHERE s.organization_id=_owner AND s.id=_sale FOR UPDATE;
        IF NOT FOUND OR v_sale.cashier_id<>v_actor OR
          v_sale.register_id<>v_register OR v_sale.shift_id<>v_shift THEN
          RAISE EXCEPTION 'sale_cancel_changed' USING ERRCODE='42501';
        END IF;
        IF v_sale.status='voided' THEN
          IF v_sale.void_request_id IS DISTINCT FROM _request THEN
            RAISE EXCEPTION 'sale_cancel_replay_mismatch' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('organization_id',_owner,
            'sale_id',_sale,'status','voided','replay',true);
        END IF;
        IF v_sale.status<>'awaiting_payment'
          OR EXISTS(SELECT 1 FROM pos.payment_events p
            WHERE p.organization_id=_owner AND p.sale_id=_sale
              AND p.status IN ('pending','verified'))
          OR EXISTS(SELECT 1 FROM pos.receipts r
            WHERE r.organization_id=_owner AND r.sale_id=_sale)
        THEN RAISE EXCEPTION 'paid_or_pending_sale_cannot_be_cancelled'
          USING ERRCODE='42501';
        END IF;

        FOR v_row IN
          SELECT * FROM pos.sale_reservations
          WHERE organization_id=_owner AND sale_id=_sale
            AND released_at IS NULL AND consumed_at IS NULL
          ORDER BY location_id,product_id,variant_id
          FOR UPDATE
        LOOP
          UPDATE pos.stock SET reserved=reserved-v_row.quantity,version=version+1
            WHERE organization_id=_owner AND location_id=v_row.location_id
              AND product_id=v_row.product_id AND variant_id=v_row.variant_id
              AND reserved>=v_row.quantity;
          IF NOT FOUND THEN RAISE EXCEPTION 'reservation_stock_mismatch'
            USING ERRCODE='23514'; END IF;
          UPDATE pos.sale_reservations SET released_at=clock_timestamp()
            WHERE sale_id=v_row.sale_id AND line_number=v_row.line_number;
        END LOOP;
        UPDATE pos.sales SET status='voided',voided_at=clock_timestamp(),
          void_request_id=_request WHERE id=_sale AND organization_id=_owner;
        INSERT INTO pos.transaction_audit(
          id,organization_id,actor_id,sale_id,action,request_id)
        VALUES(gen_random_uuid(),_owner,v_actor,_sale,'sale_void',_request);
        RETURN jsonb_build_object('organization_id',_owner,
          'sale_id',_sale,'status','voided','replay',false);
      END $cancel$;
      REVOKE ALL ON FUNCTION pos.cancel_unpaid_cashier_sale(
        text,text,uuid,uuid) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos.sales WHERE void_request_id IS NOT NULL)
        THEN RAISE EXCEPTION 'sale_void_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION pos.cancel_unpaid_cashier_sale(text,text,uuid,uuid);
      ALTER TABLE pos.transaction_audit DROP CONSTRAINT transaction_audit_action_check;
      ALTER TABLE pos.transaction_audit ADD CONSTRAINT transaction_audit_action_check
        CHECK(action IN ('shift_open','shift_close','sale_create','cash_verify',
          'card_capture_verify','sale_paid','receipt_issue',
          'return_request','return_approve','return_reject','refund_verify'));
      DROP INDEX pos.uq_pos_void_request;
      ALTER TABLE pos.sales DROP COLUMN void_request_id;
    """);
}
