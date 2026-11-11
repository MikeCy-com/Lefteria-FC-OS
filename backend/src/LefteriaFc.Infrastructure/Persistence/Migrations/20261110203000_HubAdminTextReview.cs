using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Manager-only human review of the currently selected HUB text revision.
/// No image approval or publishing side effects.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110203000_HubAdminTextReview")]
public sealed class HubAdminTextReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.review_admin_text(
              _digest text,_owner text,_content uuid,_version bigint,_decision text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $hub_text_review$
            DECLARE
              v_actor uuid; v_session uuid; v_role text;
              v_content hub.content%ROWTYPE;
              v_revision hub.revisions%ROWTYPE;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _content IS NULL OR _version IS NULL OR _version<1
                OR _decision NOT IN ('passed','rejected')
              THEN
                RAISE EXCEPTION 'invalid_hub_text_review' USING ERRCODE='22023';
              END IF;

              -- Lock session, administrator and exact owner grant until the
              -- review decision and audit commit. No AI or editor principal.
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp()
                  AND a.is_active AND NOT a.invite_pending
                  AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
              IF v_actor IS NULL OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'hub_text_review_permission_denied' USING ERRCODE='42501';
              END IF;

              SELECT * INTO v_content
                FROM hub.content
               WHERE id=_content AND organization_id=_owner
               FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_draft_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_content.version<>_version OR v_content.status<>'review_required' THEN
                RAISE EXCEPTION 'hub_review_version_conflict' USING ERRCODE='40001';
              END IF;
              SELECT * INTO v_revision FROM hub.revisions
                WHERE content_id=_content
                ORDER BY revision DESC LIMIT 1
                FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_revision_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_revision.text_review<>'pending' THEN
                RAISE EXCEPTION 'hub_text_already_reviewed' USING ERRCODE='40001';
              END IF;

              UPDATE hub.revisions SET text_review=_decision WHERE id=v_revision.id;
              UPDATE hub.content
                SET version=version+1,updated_at=statement_timestamp()
                WHERE id=_content AND organization_id=_owner
                  AND version=_version AND status='review_required';
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_review_version_conflict' USING ERRCODE='40001';
              END IF;

              INSERT INTO hub.review_events
                (id,content_id,revision_id,admin_account_id,action,metadata)
              VALUES (gen_random_uuid(),_content,v_revision.id,v_actor,'text_reviewed',
                jsonb_build_object('decision',_decision));

              INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                 action,outcome,organization_id,reason_code)
              VALUES (gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.hub.text.review','succeeded',_owner,_decision);

              RETURN jsonb_build_object('id',_content,
                'organization_id',_owner,'status','review_required',
                'version',_version+1,'revision_id',v_revision.id,
                'text_review',_decision,'visual_review',v_revision.visual_review);
            END $hub_text_review$;
            REVOKE ALL ON FUNCTION hub.review_admin_text(
              text,text,uuid,bigint,text) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.review_admin_text(text,text,uuid,bigint,text);
            """);
}
