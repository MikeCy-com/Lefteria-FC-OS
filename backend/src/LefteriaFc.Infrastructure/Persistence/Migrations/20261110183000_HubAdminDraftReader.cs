using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Read-only, minimized admin HUB editorial listing. No publication,
/// generation, subscription, or privileged general table access.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110183000_HubAdminDraftReader")]
public sealed class HubAdminDraftReader : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.read_admin_editorial(_digest text, _owner text)
            RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $hub_read$
            DECLARE v_actor uuid; v_role text; v_result jsonb;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                 OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
                RAISE EXCEPTION 'invalid_hub_query' USING ERRCODE='22023';
              END IF;
              SELECT a.id,a.role INTO v_actor,v_role
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
              IF v_actor IS NULL OR v_role NOT IN ('manager','super_admin','editor') THEN
                RAISE EXCEPTION 'hub_permission_denied' USING ERRCODE='42501';
              END IF;
              SELECT COALESCE(jsonb_agg(entry ORDER BY changed_at DESC,id), '[]'::jsonb)
                INTO v_result FROM (
                  SELECT c.id,c.organization_id,c.kind,c.title,c.status,c.version,
                         c.provenance,c.audience_parent,c.audience_player,
                         c.age_min,c.age_max,c.updated_at AS changed_at,
                         c.approved_at,c.published_at
                    FROM hub.content c
                   WHERE c.organization_id=_owner
                   ORDER BY c.updated_at DESC,c.id LIMIT 100
                ) AS entry;
              RETURN jsonb_build_object('organization_id',_owner,'items',v_result);
            END $hub_read$;
            REVOKE ALL ON FUNCTION hub.read_admin_editorial(text,text) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION hub.read_admin_editorial(text,text);");
    }
}
