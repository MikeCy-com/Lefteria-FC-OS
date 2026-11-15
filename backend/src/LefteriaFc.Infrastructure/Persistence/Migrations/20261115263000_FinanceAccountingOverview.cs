using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Read-only, separately authorized finance overview. These are independent
/// source ledgers, NOT a combined trial balance or statutory income statement.
/// No raw finance table rights are granted to the admin runtime.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115263000_FinanceAccountingOverview")]
public sealed class FinanceAccountingOverview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION finance.read_admin_accounting_overview(
          _digest text, _owner text, _month date
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $overview$
        DECLARE v_actor uuid; v_role text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _month IS NULL OR _month NOT BETWEEN DATE '2020-01-01' AND DATE '2100-12-01'
            OR extract(day FROM _month)<>1
          THEN RAISE EXCEPTION 'accounting_overview_invalid' USING ERRCODE='22023'; END IF;

          -- This helper locks the current session and organization grant.
          -- A role alone does not authorize a different organization.
          BEGIN
            PERFORM identity.lock_admin_club_authority(_digest,_owner);
          EXCEPTION WHEN raise_exception THEN
            RAISE EXCEPTION 'accounting_overview_denied'
              USING ERRCODE='42501';
          END;
          SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp()
            AND a.is_active AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
          -- Location/city managers MUST use the site-scoped finance reader.
          -- Owner-wide PII-free aggregates are for an explicitly granted
          -- accountant or Super Admin only, not a general "manager" grant.
          IF v_actor IS NULL OR v_role NOT IN ('super_admin','accountant')
          THEN RAISE EXCEPTION 'accounting_overview_denied' USING ERRCODE='42501'; END IF;

          RETURN jsonb_build_object(
            'organization_id',_owner,'month',_month,'currency','EUR',
            'scope','organization','is_statutory_statement',false,
            'academy_training_fees',(
              SELECT jsonb_build_object(
                'issued_count',count(*)::int,
                'issued_cents',coalesce(sum(f.amount_cents),0)::bigint,
                'paid_cents',coalesce(sum(f.amount_cents)
                  FILTER(WHERE f.status='paid'),0)::bigint,
                'open_cents',coalesce(sum(f.amount_cents)
                  FILTER(WHERE f.status='issued'),0)::bigint,
                'review_cents',coalesce(sum(f.amount_cents)
                  FILTER(WHERE f.status='review_required'),0)::bigint)
              FROM finance.training_fee_issues f
              WHERE _owner='lefteria_fc_academy'
                AND f.organization_id=_owner AND f.fee_month=_month
            ),
            'general_charges',(
              SELECT jsonb_build_object(
                'issued_count',count(*)::int,
                'issued_cents',coalesce(sum(g.amount_cents),0)::bigint,
                'collected_cents',coalesce(sum(g.paid_cents),0)::bigint,
                'outstanding_cents',coalesce(sum(g.due_cents),0)::bigint,
                'overdue_count',count(*) FILTER(WHERE g.status='overdue')::int)
              FROM finance.general_charge_balances g
              WHERE g.organization_id=_owner AND g.cancelled_at IS NULL
                AND g.issued_at>=(_month::timestamp AT TIME ZONE 'Asia/Nicosia')
                AND g.issued_at<((_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia')
            ),
            'payabl_sandbox',(
              SELECT jsonb_build_object(
                'request_count',count(*)::int,
                'confirmed_count',count(*) FILTER(WHERE p.status='paid')::int,
                'review_count',count(*) FILTER(WHERE p.status='review_required')::int)
              FROM finance.payment_requests p
              WHERE p.organization_id=_owner
                AND p.created_at>=(_month::timestamp AT TIME ZONE 'Asia/Nicosia')
                AND p.created_at<((_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia')
            )
          );
        END $overview$;
        REVOKE ALL ON FUNCTION finance.read_admin_accounting_overview(text,text,date)
          FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION finance.read_admin_accounting_overview(text,text,date);
            """);
}
