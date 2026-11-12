using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Restricted detail reader for the current draft revision. It
/// exposes no private media storage keys, fan subscriptions or youth records.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110200000_HubAdminDraftDetail")]
public sealed class HubAdminDraftDetail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.read_admin_editorial_detail(
              _digest text,_owner text,_id uuid
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $hub_detail$
            DECLARE v_actor uuid; v_role text; v_detail jsonb;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _id IS NULL THEN
                RAISE EXCEPTION 'invalid_hub_detail' USING ERRCODE='22023';
              END IF;
              -- Authorize against the CURRENT session and organization grant.
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
                RAISE EXCEPTION 'hub_detail_permission_denied' USING ERRCODE='42501';
              END IF;

              -- The full body is intentionally available only through this
              -- protected admin reader. Media file paths and bytes never leave
              -- the private asset store; only review-safe descriptors appear.
              SELECT jsonb_build_object(
                'id',c.id,'organization_id',c.organization_id,
                'kind',c.kind,'title',c.title,'status',c.status,
                'version',c.version,'provenance',c.provenance,
                'audience_parent',c.audience_parent,
                'audience_player',c.audience_player,
                'age_min',c.age_min,'age_max',c.age_max,
                'revision_id',r.id,'revision',r.revision,
                'body',r.body_text,
                'text_review',r.text_review,
                'visual_review',r.visual_review,
                'visuals',COALESCE((
                  SELECT jsonb_agg(jsonb_build_object(
                    'id',m.id,'kind',m.kind,'mime_type',m.mime_type,
                    'alt_text',m.alt_text,'moderation',m.moderation,
                    'generated_by_ai',m.generated_by_ai)
                    ORDER BY m.created_at,m.id)
                  FROM hub.media_assets m
                  WHERE m.revision_id=r.id
                ),'[]'::jsonb))
              INTO v_detail
                FROM hub.content c
                JOIN LATERAL (
                  SELECT id,revision,body_text,text_review,visual_review
                    FROM hub.revisions
                    WHERE content_id=c.id
                    ORDER BY revision DESC LIMIT 1
                ) r ON true
               WHERE c.id=_id AND c.organization_id=_owner;
              IF v_detail IS NULL THEN
                RAISE EXCEPTION 'hub_draft_not_found' USING ERRCODE='P0002';
              END IF;
              RETURN v_detail;
            END $hub_detail$;
            REVOKE ALL ON FUNCTION hub.read_admin_editorial_detail(text,text,uuid)
              FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.read_admin_editorial_detail(text,text,uuid);
            """);
}
