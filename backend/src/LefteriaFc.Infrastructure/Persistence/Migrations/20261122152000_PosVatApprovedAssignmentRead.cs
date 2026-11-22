using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Read only the independently reviewed product-variant VAT assignments for
/// the current organization's finance staff. Retains disabled policies and
/// prevents confusing a stored review with authorized fiscal checkout.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261122152000_PosVatApprovedAssignmentRead")]
public sealed class PosVatApprovedAssignmentRead : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos_cashier.read_admin_vat_assignments(_digest text,_owner text)
      RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $vat_assignments$
      DECLARE v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
        THEN RAISE EXCEPTION 'pos_vat_assignments_denied' USING ERRCODE='42501'; END IF;
        SELECT a.role INTO v_role
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g
          ON g.admin_account_id=a.id AND g.organization_id=_owner
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL
          AND s.expires_at>clock_timestamp()
          AND a.is_active AND NOT a.invite_pending
          AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('super_admin','accountant')
        THEN RAISE EXCEPTION 'pos_vat_assignments_denied' USING ERRCODE='42501'; END IF;

        RETURN jsonb_build_object(
          'organization_id',_owner,
          'checkout_enabled',false,
          'fiscal_policies_approved',false,
          'assignments',COALESCE((
            SELECT jsonb_agg(jsonb_build_object(
              'product_id',v.product_id,'variant_id',v.variant_id,
              'treatment_code',v.treatment_code,
              'treatment_revision',v.treatment_revision,
              'basis_points',t.basis_points,
              'effective_from',t.effective_from,
              'effective_until',t.effective_until,
              'enabled',t.enabled,
              'approved_at',v.approved_at,
              'authority_reference',t.authority_reference)
              ORDER BY v.approved_at DESC,v.product_id,v.variant_id)
            FROM (
              SELECT * FROM pos_cashier.variant_vat_assignments
              WHERE organization_id=_owner
              ORDER BY approved_at DESC,product_id,variant_id LIMIT 100
            ) v
            JOIN pos_cashier.vat_treatments t
              ON t.organization_id=v.organization_id
             AND t.code=v.treatment_code
             AND t.policy_revision=v.treatment_revision
            WHERE t.organization_id=_owner
          ),'[]'::jsonb));
      END $vat_assignments$;
      REVOKE ALL ON FUNCTION pos_cashier.read_admin_vat_assignments(text,text) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION pos_cashier.read_admin_vat_assignments(text,text);
        """);
}
