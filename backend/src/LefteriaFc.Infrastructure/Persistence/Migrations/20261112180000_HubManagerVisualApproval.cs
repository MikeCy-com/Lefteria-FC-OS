using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Manager-only visual approval for the selected review revision.
/// Does not approve or publish the article; a separate audited decision is required.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112180000_HubManagerVisualApproval")]
public sealed class HubManagerVisualApproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.approve_admin_hub_visual(
              _digest text,_owner text,_content uuid,_version bigint
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $hub_visual_approval$
            DECLARE
              v_actor uuid; v_session uuid; v_role text;
              v_content hub.content%ROWTYPE;
              v_revision hub.revisions%ROWTYPE;
              v_images integer;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _content IS NULL OR _version IS NULL
                OR _version<1 OR _version=9223372036854775807
              THEN
                RAISE EXCEPTION 'invalid_hub_visual_approval' USING ERRCODE='22023';
              END IF;

              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
              IF v_actor IS NULL OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'hub_visual_approval_denied' USING ERRCODE='42501';
              END IF;

              SELECT * INTO v_content FROM hub.content
                WHERE id=_content AND organization_id=_owner FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_draft_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_content.version<>_version OR v_content.status<>'review_required' THEN
                RAISE EXCEPTION 'hub_visual_approval_conflict' USING ERRCODE='40001';
              END IF;
              SELECT * INTO v_revision FROM hub.revisions
                WHERE content_id=_content ORDER BY revision DESC LIMIT 1 FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_revision_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_revision.visual_review<>'pending' THEN
                RAISE EXCEPTION 'hub_visual_approval_conflict' USING ERRCODE='40001';
              END IF;

              -- Lock all assets for this revision before checking their bytes.
              -- Text-only revisions can be explicitly marked as having no images.
              PERFORM 1 FROM hub.media_assets
                WHERE revision_id=v_revision.id FOR UPDATE;
              SELECT count(*) INTO v_images FROM hub.media_assets
                WHERE revision_id=v_revision.id;
              IF EXISTS (
                SELECT 1 FROM hub.media_assets m
                LEFT JOIN hub.quarantined_image_bytes b ON b.media_id=m.id
                WHERE m.revision_id=v_revision.id
                  AND (m.kind<>'image' OR m.mime_type<>'image/jpeg'
                    OR m.moderation<>'quarantined' OR m.generated_by_ai
                    OR m.alt_text IS NULL OR length(btrim(m.alt_text))=0
                    OR b.media_id IS NULL
                    OR m.byte_count<>octet_length(b.jpeg)
                    OR m.sha256<>encode(sha256(b.jpeg),'hex'))
              ) THEN
                RAISE EXCEPTION 'hub_visual_integrity_unverified' USING ERRCODE='40001';
              END IF;
              -- A human must have actually viewed any image, through the
              -- authenticated preview endpoint, before executing this action.
              UPDATE hub.media_assets SET moderation='reviewed'
                WHERE revision_id=v_revision.id AND moderation='quarantined';
              UPDATE hub.revisions SET visual_review='passed'
                WHERE id=v_revision.id AND visual_review='pending';
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_visual_approval_conflict' USING ERRCODE='40001';
              END IF;
              UPDATE hub.content
                SET version=version+1,updated_at=statement_timestamp()
                WHERE id=_content AND organization_id=_owner
                  AND version=_version AND status='review_required';
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_visual_approval_conflict' USING ERRCODE='40001';
              END IF;
              INSERT INTO hub.review_events
                (id,content_id,revision_id,admin_account_id,action,metadata)
              VALUES (gen_random_uuid(),_content,v_revision.id,v_actor,'visual_reviewed',
                jsonb_build_object('decision','passed','images',v_images));
              INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                 action,outcome,organization_id,reason_code)
              VALUES (gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.hub.visual.review','succeeded',_owner,'passed');
              RETURN jsonb_build_object('id',_content,'organization_id',_owner,
                'status','review_required','version',_version+1,
                'revision_id',v_revision.id,'visual_review','passed','images',v_images);
            END $hub_visual_approval$;
            REVOKE ALL ON FUNCTION hub.approve_admin_hub_visual(
              text,text,uuid,bigint) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.approve_admin_hub_visual(text,text,uuid,bigint);
            """);
}
