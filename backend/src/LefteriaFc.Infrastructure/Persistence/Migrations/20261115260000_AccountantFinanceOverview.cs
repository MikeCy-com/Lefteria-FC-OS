using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Enables the already provisionable accountant role at the finance SQL boundary.
/// Provides an isolated, aggregate-only financial snapshot; no payment provider,
/// expense, bank, or live settlement operation is added.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115260000_AccountantFinanceOverview")]
public sealed class AccountantFinanceOverview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- All ten existing finance entrypoints verify the signed-in account,
        -- unexpired session and the exact organization grant. Only extend their
        -- role allowlist; fail closed if any function definition has drifted.
        DO $accountant_grants$
        DECLARE
          v_signature text;
          v_oid oid;
          v_definition text;
          v_old constant text := 'v_role NOT IN (''manager'',''super_admin'')';
          v_new constant text := 'v_role NOT IN (''manager'',''super_admin'',''accountant'')';
        BEGIN
          FOREACH v_signature IN ARRAY ARRAY[
            'finance.create_admin_payment_request(text,text,uuid,uuid,text,text,bigint,text,text,text)',
            'finance.ready_admin_payment_request(text,text,uuid,text,text)',
            'finance.read_admin_payment_requests(text,text)',
            'finance.issue_admin_training_fee(text,text,uuid,text,date,bigint,text,text,text)',
            'finance.attach_admin_training_fee_payment(text,text,uuid,uuid)',
            'finance.read_admin_training_fees(text,text)',
            'finance.reserve_admin_training_fee_sms(text,text,uuid,uuid)',
            'finance.finish_admin_training_fee_sms(text,text,uuid,boolean,text)',
            'finance.record_admin_training_fee_cash(text,text,uuid,uuid,text)',
            'finance.read_admin_invoice_pdf(text,text,uuid[])'
          ] LOOP
            v_oid := to_regprocedure(v_signature);
            IF v_oid IS NULL OR NOT EXISTS(
              SELECT 1 FROM pg_proc p JOIN pg_language l ON l.oid=p.prolang
              WHERE p.oid=v_oid AND p.prosecdef AND p.provolatile='v'
                AND l.lanname='plpgsql'
            ) THEN
              RAISE EXCEPTION 'finance_accountant_function_boundary_missing: %',v_signature;
            END IF;
            v_definition := pg_get_functiondef(v_oid);
            IF length(v_definition)-length(replace(v_definition,v_old,''))<>length(v_old)
               OR v_definition LIKE '%accountant%accountant%' THEN
              RAISE EXCEPTION 'finance_accountant_function_changed: %',v_signature;
            END IF;
            EXECUTE replace(v_definition,v_old,v_new);
          END LOOP;
        END $accountant_grants$;

        -- Snapshot metrics are deliberately segregated by source ledger.
        -- No grand total is exposed: historical general charges, training
        -- fees and unattached Payabl checkout requests are not a proven
        -- double-entry accounting journal and may overlap.
        CREATE FUNCTION finance.read_admin_finance_overview(
          _digest text,_owner text
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $finance_overview$
        DECLARE
          v_role text;
          v_fees jsonb;
          v_charges jsonb;
          v_requests jsonb;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          THEN RAISE EXCEPTION 'finance_overview_invalid_scope'
            USING ERRCODE='22023'; END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.role INTO v_role
            FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            JOIN identity.admin_organization_grants g
              ON g.admin_account_id=a.id AND g.organization_id=_owner
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp()
              AND a.is_active AND NOT a.invite_pending
              AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
          -- A location manager must never receive organization-wide sums.
          -- Scoped location reports require their own location grant.
          IF NOT FOUND OR v_role NOT IN ('super_admin','accountant')
          THEN RAISE EXCEPTION 'finance_overview_denied'
            USING ERRCODE='42501'; END IF;

          SELECT jsonb_build_object(
            'count',count(*),
            'issued_cents',coalesce(sum(amount_cents),0),
            'paid_cents',coalesce(sum(amount_cents) FILTER (WHERE status='paid'),0),
            'outstanding_cents',coalesce(sum(amount_cents) FILTER
              (WHERE status IN ('issued','review_required')),0),
            'review_cents',coalesce(sum(amount_cents) FILTER
              (WHERE status='review_required'),0)
          ) INTO v_fees
          FROM finance.training_fee_issues
          WHERE organization_id=_owner;

          SELECT jsonb_build_object(
            'count',count(*) FILTER (WHERE status<>'cancelled'),
            'cancelled_count',count(*) FILTER (WHERE status='cancelled'),
            'issued_cents',coalesce(sum(amount_cents) FILTER
              (WHERE status<>'cancelled'),0),
            'collected_cents',coalesce(sum(paid_cents) FILTER
              (WHERE status<>'cancelled'),0),
            'outstanding_cents',coalesce(sum(due_cents) FILTER
              (WHERE status<>'cancelled'),0),
            'overdue_cents',coalesce(sum(due_cents) FILTER
              (WHERE status='overdue'),0)
          ) INTO v_charges
          FROM finance.general_charge_balances
          WHERE organization_id=_owner;

          SELECT jsonb_build_object(
            'count',count(*),
            'settled_cents',coalesce(sum(p.amount_cents) FILTER
              (WHERE p.status='paid'),0),
            'pending_cents',coalesce(sum(p.amount_cents) FILTER
              (WHERE p.status IN ('initiating','ready','awaiting_settlement')),0),
            'review_cents',coalesce(sum(p.amount_cents) FILTER
              (WHERE p.status='review_required'),0)
          ) INTO v_requests
          FROM finance.payment_requests p
          WHERE p.organization_id=_owner
            AND NOT EXISTS (
              SELECT 1 FROM finance.training_fee_issues f
              WHERE f.payment_request_id=p.id
            );

          RETURN jsonb_build_object(
            'organization_id',_owner,
            'currency','EUR',
            'report_basis','all_time_separate_ledgers',
            'environment','sandbox',
            'generated_at',clock_timestamp(),
            'training_fees',v_fees,
            'general_charges',v_charges,
            'unattached_payabl_requests',v_requests,
            'expenses_available',false,
            'reconciliation_available',false
          );
        END $finance_overview$;
        REVOKE ALL ON FUNCTION finance.read_admin_finance_overview(text,text)
          FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION finance.read_admin_finance_overview(text,text);
        DO $accountant_rollback$
        DECLARE
          v_signature text;
          v_oid oid;
          v_definition text;
          v_old constant text := 'v_role NOT IN (''manager'',''super_admin'',''accountant'')';
          v_new constant text := 'v_role NOT IN (''manager'',''super_admin'')';
        BEGIN
          FOREACH v_signature IN ARRAY ARRAY[
            'finance.create_admin_payment_request(text,text,uuid,uuid,text,text,bigint,text,text,text)',
            'finance.ready_admin_payment_request(text,text,uuid,text,text)',
            'finance.read_admin_payment_requests(text,text)',
            'finance.issue_admin_training_fee(text,text,uuid,text,date,bigint,text,text,text)',
            'finance.attach_admin_training_fee_payment(text,text,uuid,uuid)',
            'finance.read_admin_training_fees(text,text)',
            'finance.reserve_admin_training_fee_sms(text,text,uuid,uuid)',
            'finance.finish_admin_training_fee_sms(text,text,uuid,boolean,text)',
            'finance.record_admin_training_fee_cash(text,text,uuid,uuid,text)',
            'finance.read_admin_invoice_pdf(text,text,uuid[])'
          ] LOOP
            v_oid := to_regprocedure(v_signature);
            IF v_oid IS NULL THEN
              RAISE EXCEPTION 'finance_accountant_rollback_missing: %',v_signature;
            END IF;
            v_definition := pg_get_functiondef(v_oid);
            IF length(v_definition)-length(replace(v_definition,v_old,''))<>length(v_old)
            THEN RAISE EXCEPTION 'finance_accountant_rollback_changed: %',v_signature;
            END IF;
            EXECUTE replace(v_definition,v_old,v_new);
          END LOOP;
        END $accountant_rollback$;
        """);
}
