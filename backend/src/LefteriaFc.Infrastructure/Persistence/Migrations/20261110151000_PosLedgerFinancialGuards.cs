using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Enforce core financial boundaries in the database, not in a cashier UI.
/// Trigger functions are private and never exposed to runtime callers.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110151000_PosLedgerFinancialGuards")]
public sealed class PosLedgerFinancialGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos.reject_transaction_history_edit()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      BEGIN
        RAISE EXCEPTION 'pos_financial_history_is_immutable' USING ERRCODE='42501';
      END $$;
      CREATE TRIGGER pos_sale_lines_immutable
        BEFORE UPDATE OR DELETE ON pos.sale_lines
        FOR EACH ROW EXECUTE FUNCTION pos.reject_transaction_history_edit();
      CREATE TRIGGER pos_receipts_immutable
        BEFORE UPDATE OR DELETE ON pos.receipts
        FOR EACH ROW EXECUTE FUNCTION pos.reject_transaction_history_edit();
      CREATE TRIGGER pos_audit_immutable
        BEFORE UPDATE OR DELETE ON pos.transaction_audit
        FOR EACH ROW EXECUTE FUNCTION pos.reject_transaction_history_edit();
      CREATE TRIGGER pos_return_lines_immutable
        BEFORE UPDATE OR DELETE ON pos.return_lines
        FOR EACH ROW EXECUTE FUNCTION pos.reject_transaction_history_edit();
      CREATE FUNCTION pos.guard_refund_confirmation()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      BEGIN
        IF TG_OP='DELETE' THEN
          RAISE EXCEPTION 'refund_delete_denied' USING ERRCODE='42501';
        END IF;
        IF OLD.status<>'pending' OR NEW.status NOT IN ('verified','failed')
          OR (to_jsonb(NEW)-'status'-'verified_at'-'provider_evidence_sha256'-'provider_refund_id')
             IS DISTINCT FROM
             (to_jsonb(OLD)-'status'-'verified_at'-'provider_evidence_sha256'-'provider_refund_id')
        THEN RAISE EXCEPTION 'refund_confirmation_invalid' USING ERRCODE='42501';
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER pos_refund_events_immutable
        BEFORE UPDATE OR DELETE ON pos.refund_events
        FOR EACH ROW EXECUTE FUNCTION pos.guard_refund_confirmation();

      CREATE FUNCTION pos.guard_sale_line_catalog_price()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      DECLARE v_sale pos.sales%ROWTYPE; v_product pos.products%ROWTYPE;
        v_variant pos.product_variants%ROWTYPE;
      BEGIN
        SELECT * INTO v_sale FROM pos.sales
          WHERE organization_id=NEW.organization_id AND id=NEW.sale_id FOR UPDATE;
        IF NOT FOUND OR v_sale.status<>'awaiting_payment'
          OR NOT EXISTS (SELECT 1 FROM pos.shifts sh
            WHERE sh.id=v_sale.shift_id AND sh.organization_id=NEW.organization_id
              AND sh.register_id=v_sale.register_id AND sh.closed_at IS NULL)
        THEN RAISE EXCEPTION 'cashier_sale_not_open' USING ERRCODE='42501';
        END IF;
        SELECT * INTO v_product FROM pos.products
          WHERE organization_id=NEW.organization_id
            AND id=NEW.product_id AND active FOR SHARE;
        IF NOT FOUND OR NOT EXISTS (
          SELECT 1 FROM pos.categories c
          WHERE c.organization_id=NEW.organization_id
            AND c.id=v_product.category_id AND c.active
        ) THEN RAISE EXCEPTION 'catalog_product_unavailable' USING ERRCODE='22023';
        END IF;
        SELECT * INTO v_variant FROM pos.product_variants
          WHERE organization_id=NEW.organization_id AND
            product_id=NEW.product_id AND id=NEW.variant_id FOR SHARE;
        IF NOT FOUND OR NEW.unit_price_cents<>v_variant.price_cents
           OR NEW.unit_cost_cents<>v_variant.cost_cents
           OR NEW.product_name<>v_product.name OR NEW.variant_name<>v_variant.name
        THEN RAISE EXCEPTION 'client_price_or_snapshot_not_authoritative'
          USING ERRCODE='22023'; END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER pos_sale_line_server_price
        BEFORE INSERT ON pos.sale_lines FOR EACH ROW
        EXECUTE FUNCTION pos.guard_sale_line_catalog_price();

      CREATE FUNCTION pos.guard_payment_evidence()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      DECLARE v_sale pos.sales%ROWTYPE; v_total bigint;
      BEGIN
        IF TG_OP='UPDATE' THEN
          IF OLD.status<>'pending' OR NEW.status NOT IN ('verified','failed')
             OR (to_jsonb(NEW)-'status'-'verified_at'-'provider_evidence_sha256')
                IS DISTINCT FROM
                (to_jsonb(OLD)-'status'-'verified_at'-'provider_evidence_sha256') THEN
            RAISE EXCEPTION 'payment_event_mutation_denied' USING ERRCODE='42501';
          END IF;
        END IF;
        SELECT * INTO v_sale FROM pos.sales
          WHERE organization_id=NEW.organization_id AND id=NEW.sale_id FOR UPDATE;
        IF NOT FOUND OR v_sale.status IN ('paid','voided') THEN
          RAISE EXCEPTION 'sale_not_payable' USING ERRCODE='42501';
        END IF;
        IF NEW.status='verified' THEN
          SELECT coalesce(sum(amount_cents),0) INTO v_total FROM pos.payment_events
            WHERE organization_id=NEW.organization_id
              AND sale_id=NEW.sale_id AND status='verified'
              AND (TG_OP='INSERT' OR id<>NEW.id);
          IF v_total+NEW.amount_cents>v_sale.total_cents THEN
            RAISE EXCEPTION 'verified_payments_exceed_sale_total' USING ERRCODE='23514';
          END IF;
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER pos_payment_verified_guard
        BEFORE INSERT OR UPDATE ON pos.payment_events
        FOR EACH ROW EXECUTE FUNCTION pos.guard_payment_evidence();
      CREATE TRIGGER pos_payment_no_delete
        BEFORE DELETE ON pos.payment_events
        FOR EACH ROW EXECUTE FUNCTION pos.reject_transaction_history_edit();

      CREATE FUNCTION pos.guard_sale_paid()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      DECLARE v_total bigint; v_lines bigint;
      BEGIN
        IF TG_OP='UPDATE' THEN
          IF OLD.status='paid' OR OLD.status='voided' OR
            NEW.organization_id<>OLD.organization_id OR
            NEW.register_id<>OLD.register_id OR
            NEW.shift_id<>OLD.shift_id OR
            NEW.cashier_id<>OLD.cashier_id OR
            NEW.request_id<>OLD.request_id OR
            NEW.subtotal_cents<>OLD.subtotal_cents OR
            NEW.discount_cents<>OLD.discount_cents OR
            NEW.tax_cents<>OLD.tax_cents OR
            NEW.total_cents<>OLD.total_cents THEN
            RAISE EXCEPTION 'sale_financial_snapshot_immutable' USING ERRCODE='42501';
          END IF;
        END IF;
        SELECT coalesce(sum(amount_cents),0) INTO v_total
          FROM pos.payment_events WHERE organization_id=NEW.organization_id
            AND sale_id=NEW.id AND status='verified';
        IF NEW.status='paid' THEN
          SELECT coalesce(sum(line_total_cents),0) INTO v_lines
          FROM pos.sale_lines WHERE sale_id=NEW.id;
          IF v_total<>NEW.total_cents OR v_lines<>NEW.subtotal_cents OR
            NEW.paid_at IS NULL THEN
            RAISE EXCEPTION 'sale_not_reconciled' USING ERRCODE='23514';
          END IF;
        ELSIF NEW.status='partially_paid' THEN
          IF v_total<=0 OR v_total>=NEW.total_cents THEN
            RAISE EXCEPTION 'partial_payment_status_not_reconciled'
              USING ERRCODE='23514';
          END IF;
        ELSIF NEW.status IN ('awaiting_payment','voided') THEN
          IF v_total<>0 THEN
            RAISE EXCEPTION 'paid_sale_cannot_be_unpaid_or_voided'
              USING ERRCODE='23514';
          END IF;
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER pos_sale_reconcile
        BEFORE UPDATE ON pos.sales
        FOR EACH ROW EXECUTE FUNCTION pos.guard_sale_paid();
      CREATE TRIGGER pos_sale_no_delete
        BEFORE DELETE ON pos.sales
        FOR EACH ROW EXECUTE FUNCTION pos.reject_transaction_history_edit();

      CREATE FUNCTION pos.guard_receipt_issue()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      DECLARE v_sale pos.sales%ROWTYPE; v_verified bigint; v_lines bigint;
      BEGIN
        SELECT * INTO v_sale FROM pos.sales WHERE id=NEW.sale_id
          AND organization_id=NEW.organization_id FOR UPDATE;
        IF NOT FOUND OR v_sale.status<>'paid' OR
           v_sale.register_id<>NEW.register_id OR v_sale.paid_at IS NULL THEN
          RAISE EXCEPTION 'receipt_requires_paid_sale' USING ERRCODE='42501';
        END IF;
        SELECT coalesce(sum(amount_cents),0) INTO v_verified
          FROM pos.payment_events WHERE sale_id=NEW.sale_id
          AND organization_id=NEW.organization_id AND status='verified';
        SELECT coalesce(sum(line_total_cents),0) INTO v_lines
          FROM pos.sale_lines WHERE sale_id=NEW.sale_id;
        IF v_verified<>v_sale.total_cents OR v_lines<>v_sale.subtotal_cents
          OR NEW.immutable_payload->>'currency' IS DISTINCT FROM 'EUR'
          OR (NEW.immutable_payload->>'total_cents') IS DISTINCT FROM
            v_sale.total_cents::text
          OR NEW.payload_sha256 IS DISTINCT FROM
            encode(sha256(convert_to(NEW.immutable_payload::text,'UTF8')),'hex')
        THEN RAISE EXCEPTION 'receipt_snapshot_not_reconciled' USING ERRCODE='23514';
        END IF;
        NEW.issued_at:=clock_timestamp();
        RETURN NEW;
      END $$;
      CREATE TRIGGER pos_receipt_paid_guard
        BEFORE INSERT ON pos.receipts FOR EACH ROW EXECUTE FUNCTION pos.guard_receipt_issue();

      CREATE FUNCTION pos.guard_return_line()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      DECLARE v_sale uuid; v_sold integer; v_returned integer; v_status text;
      BEGIN
        SELECT r.sale_id,r.status INTO v_sale,v_status
          FROM pos.returns r
          WHERE r.organization_id=NEW.organization_id AND r.id=NEW.return_id
          FOR UPDATE;
        IF NOT FOUND OR v_status<>'requested' OR v_sale<>NEW.sale_id THEN
          RAISE EXCEPTION 'return_sale_mismatch' USING ERRCODE='42501';
        END IF;
        SELECT quantity INTO v_sold FROM pos.sale_lines
          WHERE sale_id=NEW.sale_id AND line_number=NEW.sale_line_number;
        SELECT coalesce(sum(l.quantity),0) INTO v_returned
          FROM pos.return_lines l JOIN pos.returns r
            ON r.organization_id=l.organization_id AND r.id=l.return_id
          WHERE l.sale_id=NEW.sale_id
            AND l.sale_line_number=NEW.sale_line_number
            AND r.status IN ('approved','refunded','requested');
        IF v_sold IS NULL OR v_returned+NEW.quantity>v_sold THEN
          RAISE EXCEPTION 'return_quantity_exceeds_sale' USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER pos_return_quantity_guard
        BEFORE INSERT ON pos.return_lines FOR EACH ROW EXECUTE FUNCTION pos.guard_return_line();

      CREATE FUNCTION pos.guard_refund_amount()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      DECLARE
        v_return pos.returns%ROWTYPE; v_payment pos.payment_events%ROWTYPE;
        v_refunded bigint; v_returned_cents bigint; v_return_refunds bigint;
      BEGIN
        SELECT * INTO v_return FROM pos.returns
          WHERE organization_id=NEW.organization_id AND id=NEW.return_id FOR UPDATE;
        SELECT * INTO v_payment FROM pos.payment_events
          WHERE id=NEW.payment_event_id FOR UPDATE;
        IF NOT FOUND OR v_return.id IS NULL OR v_return.status<>'approved'
          OR v_payment.organization_id<>NEW.organization_id
          OR v_payment.sale_id<>v_return.sale_id
          OR v_payment.payment_kind<>NEW.refund_kind
          OR v_payment.status<>'verified' THEN
          RAISE EXCEPTION 'refund_payment_mismatch' USING ERRCODE='42501';
        END IF;
        SELECT coalesce(sum(amount_cents),0) INTO v_refunded FROM pos.refund_events
          WHERE payment_event_id=NEW.payment_event_id AND status IN ('pending','verified');
        SELECT coalesce(sum(l.quantity::bigint*s.unit_price_cents),0)
          INTO v_returned_cents
          FROM pos.return_lines l
          JOIN pos.sale_lines s ON s.sale_id=l.sale_id
            AND s.line_number=l.sale_line_number
          WHERE l.organization_id=NEW.organization_id
            AND l.return_id=NEW.return_id AND l.sale_id=v_return.sale_id;
        SELECT coalesce(sum(amount_cents),0) INTO v_return_refunds
          FROM pos.refund_events
          WHERE organization_id=NEW.organization_id
            AND return_id=NEW.return_id
            AND status IN ('pending','verified');
        IF v_refunded+NEW.amount_cents>v_payment.amount_cents
           OR v_return_refunds+NEW.amount_cents>v_returned_cents THEN
          RAISE EXCEPTION 'refund_exceeds_verified_payment_or_return_value'
            USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER pos_refund_original_payment_guard
        BEFORE INSERT ON pos.refund_events FOR EACH ROW EXECUTE FUNCTION pos.guard_refund_amount();

      REVOKE ALL ON FUNCTION pos.guard_sale_line_catalog_price() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.guard_refund_confirmation() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.reject_transaction_history_edit() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.guard_payment_evidence() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.guard_sale_paid() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.guard_receipt_issue() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.guard_return_line() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.guard_refund_amount() FROM PUBLIC;
    """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP TRIGGER pos_sale_line_server_price ON pos.sale_lines;
      DROP TRIGGER pos_refund_original_payment_guard ON pos.refund_events;
      DROP TRIGGER pos_return_quantity_guard ON pos.return_lines;
      DROP TRIGGER pos_receipt_paid_guard ON pos.receipts;
      DROP TRIGGER pos_sale_no_delete ON pos.sales;
      DROP TRIGGER pos_sale_reconcile ON pos.sales;
      DROP TRIGGER pos_payment_no_delete ON pos.payment_events;
      DROP TRIGGER pos_payment_verified_guard ON pos.payment_events;
      DROP TRIGGER pos_return_lines_immutable ON pos.return_lines;
      DROP TRIGGER pos_refund_events_immutable ON pos.refund_events;
      DROP TRIGGER pos_audit_immutable ON pos.transaction_audit;
      DROP TRIGGER pos_receipts_immutable ON pos.receipts;
      DROP TRIGGER pos_sale_lines_immutable ON pos.sale_lines;
      DROP FUNCTION pos.guard_refund_amount();
      DROP FUNCTION pos.guard_return_line();
      DROP FUNCTION pos.guard_receipt_issue();
      DROP FUNCTION pos.guard_sale_paid();
      DROP FUNCTION pos.guard_payment_evidence();
      DROP FUNCTION pos.guard_sale_line_catalog_price();
      DROP FUNCTION pos.guard_refund_confirmation();
      DROP FUNCTION pos.reject_transaction_history_edit();
    """);
}