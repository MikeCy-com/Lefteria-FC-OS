using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Atomic counted-cash settlement and paid receipt, disabled unless the
/// independently approved fiscal policies and narrow runtime grants exist.
/// A cashier assertion alone is not a verified third-party card capture.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110221000_PosVerifiedCashTender")]
public sealed class PosVerifiedCashTender : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- The actual money handed over and change returned are recorded rather
      -- than silently treating the browser's amount as card confirmation.
      ALTER TABLE pos_cashier.payment_events
        ADD COLUMN cash_tendered_cents bigint,
        ADD COLUMN cash_change_cents bigint;
      ALTER TABLE pos_cashier.payment_events
        ADD CONSTRAINT ck_pos_cash_tender_totals CHECK (
          (
            payment_kind='card'
            AND cash_tendered_cents IS NULL
            AND cash_change_cents IS NULL
          ) OR (
            payment_kind='cash'
            AND (
              (status<>'verified' AND cash_tendered_cents IS NULL AND cash_change_cents IS NULL)
              OR (status='verified'
                AND cash_tendered_cents BETWEEN 1 AND 100000000
                AND cash_change_cents BETWEEN 0 AND 100000000
                AND cash_tendered_cents=amount_cents+cash_change_cents)
            )
          )
        );

      CREATE FUNCTION pos_cashier.settle_exact_cash_sale(
        _digest text,_owner text,_sale uuid,_request uuid,_tendered bigint
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $cash$
      DECLARE
        v_actor uuid;
        v_sale pos_cashier.sales%ROWTYPE;
        v_shift pos_cashier.shifts%ROWTYPE;
        v_payment pos_cashier.payment_events%ROWTYPE;
        v_receipt jsonb;
        v_change bigint;
        v_reference text;
      BEGIN
        IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _sale IS NULL OR _request IS NULL
          OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _tendered IS NULL OR _tendered NOT BETWEEN 1 AND 100000000
        THEN RAISE EXCEPTION 'cash_tender_input_invalid' USING ERRCODE='22023';
        END IF;

        -- Obtain exact owner/register without assuming the browser can pick
        -- an arbitrary cashier or shift. Authorization is freshly checked
        -- before any shift or sale row lock.
        SELECT * INTO v_sale FROM pos_cashier.sales
          WHERE organization_id=_owner AND id=_sale;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'cash_tender_sale_unavailable' USING ERRCODE='42501';
        END IF;
        v_actor:=pos_cashier.current_cashier_authority(
          _digest,_owner,v_sale.register_id);
        SELECT * INTO v_shift FROM pos_cashier.shifts
          WHERE id=v_sale.shift_id AND organization_id=_owner
            AND register_id=v_sale.register_id AND cashier_id=v_actor
          FOR UPDATE;
        IF NOT FOUND OR v_shift.closed_at IS NOT NULL THEN
          RAISE EXCEPTION 'cash_tender_shift_unavailable' USING ERRCODE='42501';
        END IF;
        SELECT * INTO v_sale FROM pos_cashier.sales
          WHERE organization_id=_owner AND id=_sale FOR UPDATE;
        IF NOT FOUND OR v_sale.cashier_id<>v_actor
          OR v_sale.shift_id<>v_shift.id
          OR v_sale.register_id<>v_shift.register_id THEN
          RAISE EXCEPTION 'cash_tender_scope_changed' USING ERRCODE='42501';
        END IF;

        v_reference:='pos_cash_'||_request::text;
        SELECT * INTO v_payment FROM pos_cashier.payment_events
          WHERE organization_id=_owner
            AND verification_reference=v_reference
            AND payment_kind='cash' FOR UPDATE;
        IF FOUND THEN
          IF v_payment.sale_id<>_sale
            OR v_payment.recorded_by<>v_actor
            OR v_payment.status<>'verified'
            OR v_payment.cash_tendered_cents<>_tendered
            OR v_payment.amount_cents<>v_sale.total_cents
          THEN RAISE EXCEPTION 'cash_tender_replay_mismatch' USING ERRCODE='23505';
          END IF;
          SELECT jsonb_build_object('receipt_id',r.id,
             'receipt_number',r.receipt_number)
            INTO v_receipt FROM pos_cashier.receipts r
            WHERE r.organization_id=_owner AND r.sale_id=_sale;
          IF v_receipt IS NULL THEN
            RAISE EXCEPTION 'cash_tender_receipt_missing' USING ERRCODE='23514';
          END IF;
          RETURN v_receipt||jsonb_build_object(
            'organization_id',_owner,'sale_id',_sale,'replay',true,
            'tendered_cents',v_payment.cash_tendered_cents,
            'change_cents',v_payment.cash_change_cents,
            'total_cents',v_sale.total_cents);
        END IF;

        -- Never accept money if the receipt cannot legally be issued.
        -- Both tables are empty and disabled by default.
        IF NOT EXISTS(SELECT 1 FROM pos_cashier.checkout_policies
          WHERE organization_id=_owner AND enabled
            AND tax_treatment='verified_exempt'
            AND reviewed_at<=clock_timestamp())
          OR NOT EXISTS(SELECT 1 FROM pos_cashier.receipt_policies
          WHERE organization_id=_owner AND enabled
            AND reviewed_at<=clock_timestamp())
        THEN RAISE EXCEPTION 'cash_tender_fiscal_policy_disabled'
          USING ERRCODE='55000'; END IF;

        IF v_sale.status<>'awaiting_payment'
          OR v_sale.total_cents NOT BETWEEN 1 AND 100000000
          OR _tendered<v_sale.total_cents
          OR EXISTS (SELECT 1 FROM pos_cashier.payment_events p
            WHERE p.organization_id=_owner AND p.sale_id=_sale)
          OR EXISTS (SELECT 1 FROM pos_cashier.receipts r
            WHERE r.organization_id=_owner AND r.sale_id=_sale)
        THEN RAISE EXCEPTION 'cash_tender_sale_not_payable'
          USING ERRCODE='42501'; END IF;
        IF NOT EXISTS (SELECT 1 FROM pos_cashier.sale_reservations
          WHERE organization_id=_owner AND sale_id=_sale
            AND released_at IS NULL AND consumed_at IS NULL)
        THEN RAISE EXCEPTION 'cash_tender_inventory_not_reserved'
          USING ERRCODE='23514'; END IF;
        v_change:=_tendered-v_sale.total_cents;

        INSERT INTO pos_cashier.payment_events(
          id,organization_id,sale_id,payment_kind,status,amount_cents,
          verification_reference,recorded_by,verified_at,
          cash_tendered_cents,cash_change_cents)
        VALUES(
          gen_random_uuid(),_owner,_sale,'cash','verified',v_sale.total_cents,
          v_reference,v_actor,clock_timestamp(),_tendered,v_change);
        INSERT INTO pos_cashier.transaction_audit(
          id,organization_id,actor_id,sale_id,action,request_id)
        VALUES(gen_random_uuid(),_owner,v_actor,_sale,'cash_verify',_request);

        -- Same database transaction: paid sale, numbered immutable receipt
        -- and reservation consumption either all succeed or all roll back.
        v_receipt:=pos_cashier.issue_verified_internal_receipt(
          _digest,_owner,_sale,gen_random_uuid());
        RETURN jsonb_build_object(
          'organization_id',_owner,'sale_id',_sale,
          'receipt_id',v_receipt->'id',
          'receipt_number',v_receipt->'receipt_number',
          'replay',false,'total_cents',v_sale.total_cents,
          'tendered_cents',_tendered,'change_cents',v_change);
      END $cash$;

      REVOKE ALL ON FUNCTION pos_cashier.settle_exact_cash_sale(
        text,text,uuid,uuid,bigint) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos_cashier.payment_events
           WHERE cash_tendered_cents IS NOT NULL OR cash_change_cents IS NOT NULL)
        THEN RAISE EXCEPTION 'cash_payment_history_requires_forward_recovery';
        END IF;
      END $guard$;
      DROP FUNCTION pos_cashier.settle_exact_cash_sale(text,text,uuid,uuid,bigint);
      ALTER TABLE pos_cashier.payment_events DROP CONSTRAINT ck_pos_cash_tender_totals;
      ALTER TABLE pos_cashier.payment_events DROP COLUMN cash_change_cents;
      ALTER TABLE pos_cashier.payment_events DROP COLUMN cash_tendered_cents;
    """);
}