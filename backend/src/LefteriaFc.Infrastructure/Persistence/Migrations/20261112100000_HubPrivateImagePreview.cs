using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Manager-only, integrity-checked read of quarantined JPEG bytes.
/// No direct table grants, public access, scanner decision, or publication.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112100000_HubPrivateImagePreview")]
public sealed class HubPrivateImagePreview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.read_admin_quarantined_image(
              _digest text,_owner text,_content uuid,_media uuid
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $hub_image_preview$
            DECLARE
              v_actor uuid; v_role text;
              v_bytes bytea; v_hash text;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _content IS NULL OR _media IS NULL
              THEN RAISE EXCEPTION 'invalid_hub_image_preview' USING ERRCODE='22023';
              END IF;

              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,a.role INTO v_actor,v_role
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
              IF v_actor IS NULL OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'hub_preview_permission_denied' USING ERRCODE='42501';
              END IF;

              SELECT q.jpeg,m.sha256 INTO v_bytes,v_hash
                FROM hub.content c
                JOIN hub.revisions r ON r.content_id=c.id
                JOIN hub.media_assets m ON m.revision_id=r.id
                JOIN hub.quarantined_image_bytes q ON q.media_id=m.id
                WHERE c.id=_content AND c.organization_id=_owner
                  AND m.id=_media AND m.kind='image'
                  AND m.mime_type='image/jpeg' AND m.moderation='quarantined'
                  AND m.generated_by_ai=false
                  AND m.byte_count=octet_length(q.jpeg)
                FOR SHARE OF c,r,m,q;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_image_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_hash<>encode(sha256(v_bytes),'hex') THEN
                RAISE EXCEPTION 'hub_image_integrity_failure' USING ERRCODE='22023';
              END IF;
              RETURN jsonb_build_object('id',_media,'content_id',_content,
                'organization_id',_owner,'mime_type','image/jpeg',
                'sha256',v_hash,'byte_count',octet_length(v_bytes),
                'base64',encode(v_bytes,'base64'));
            END $hub_image_preview$;
            REVOKE ALL ON FUNCTION hub.read_admin_quarantined_image(
              text,text,uuid,uuid) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.read_admin_quarantined_image(text,text,uuid,uuid);
            """);
}
