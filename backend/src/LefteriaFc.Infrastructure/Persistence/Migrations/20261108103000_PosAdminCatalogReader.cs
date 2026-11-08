using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// A single reviewed, cookie-session/owner-checked read into the private POS
/// catalog. Runtime roles never gain direct SELECT, cost or write privileges.
/// This is not a checkout, register or payment service.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108103000_PosAdminCatalogReader")]
public sealed class PosAdminCatalogReader : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION pos.read_admin_catalog(_digest text, _owner text)
            RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $pos_catalog_read$
            DECLARE
              v_role text;
              v_categories integer;
              v_products integer;
              v_variants integer;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                 OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
                RAISE EXCEPTION 'pos_catalog_invalid_scope' USING ERRCODE='22023';
              END IF;

              -- Serializes with organization grant/session invalidation, as
              -- the established club editor does. The caller never supplies
              -- an arbitrary role or grants list.
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.role INTO v_role
              FROM identity.admin_sessions s
              JOIN identity.admin_accounts a ON a.id=s.admin_account_id
              JOIN identity.admin_organization_grants g
                ON g.admin_account_id=a.id AND g.organization_id=_owner
              WHERE s.token_digest=_digest
                AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()
                AND a.is_active AND NOT a.invite_pending
                AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'pos_catalog_permission_denied' USING ERRCODE='42501';
              END IF;

              SELECT count(*) INTO v_categories FROM pos.categories WHERE organization_id=_owner;
              SELECT count(*) INTO v_products FROM pos.products WHERE organization_id=_owner;
              SELECT count(*) INTO v_variants FROM pos.product_variants WHERE organization_id=_owner;
              IF v_categories>100 OR v_products>250 OR v_variants>1000 THEN
                RAISE EXCEPTION 'pos_catalog_too_large' USING ERRCODE='54000';
              END IF;

              RETURN jsonb_build_object(
                'organization_id',_owner,
                'categories',COALESCE((
                  SELECT jsonb_agg(jsonb_build_object(
                    'id',c.id,'name',c.name,'active',c.active,'version',c.version
                  ) ORDER BY c.id)
                  FROM pos.categories c WHERE c.organization_id=_owner
                ),'[]'::jsonb),
                'products',COALESCE((
                  SELECT jsonb_agg(jsonb_build_object(
                    'id',p.id,'category_id',p.category_id,
                    'name',p.name,'image_url',p.image_url,
                    'active',p.active,'version',p.version,
                    'variants',COALESCE((
                      SELECT jsonb_agg(jsonb_build_object(
                        'id',v.id,'name',v.name,
                        'cost_cents',v.cost_cents,'price_cents',v.price_cents
                      ) ORDER BY v.id)
                      FROM pos.product_variants v
                      WHERE v.organization_id=p.organization_id AND v.product_id=p.id
                    ),'[]'::jsonb)
                  ) ORDER BY p.id)
                  FROM pos.products p WHERE p.organization_id=_owner
                ),'[]'::jsonb)
              );
            END
            $pos_catalog_read$;

            -- PostgreSQL functions otherwise default to PUBLIC EXECUTE.
            -- A dedicated migration explicitly revokes that default; only
            -- verified web-admin-auth grant provisioning may opt in.
            REVOKE ALL ON FUNCTION pos.read_admin_catalog(text,text) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP FUNCTION pos.read_admin_catalog(text,text);");
}
