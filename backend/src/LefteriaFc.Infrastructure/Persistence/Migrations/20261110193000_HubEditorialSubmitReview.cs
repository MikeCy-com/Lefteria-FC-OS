using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Submit an existing HUB draft for human review. No automatic
/// approval, publication, provider access or member content delivery.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110193000_HubEditorialSubmitReview")]
public sealed class HubEditorialSubmitReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.submit_admin_draft_review(
              _digest text,_owner text,_content uuid,_version bigint
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $hub_submit$
            DECLARE
              v_actor uuid; v_session uuid; v_role text;
              v_content hub.content%ROWTYPE;
              v_revision uuid;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _content IS NULL OR _version IS NULL OR _version<1
              THEN RAISE EXCEPTION 'invalid_hub_review_request' USING ERRCODE='22023';
              END IF;

              -- Hold session/account/organization authority until the entire
              -- record status update and both audit inserts commit.
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
              IF v_actor IS NULL OR v_role NOT IN ('manager','super_admin','editor') THEN
                RAISE EXCEPTION 'hub_review_permission_denied' USING ERRCODE='42501';
              END IF;

              SELECT * INTO v_content
                FROM hub.content
               WHERE id=_content AND organization_id=_owner FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_draft_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_content.version<>_version OR v_content.status<>'ai_draft' THEN
                RAISE EXCEPTION 'hub_draft_version_conflict' USING ERRCODE='40001';
              END IF;
              SELECT id INTO v_revision FROM hub.revisions
                WHERE content_id=_content AND revision=_version;
              IF v_revision IS NULL THEN
                RAISE EXCEPTION 'hub_revision_not_found' USING ERRCODE='P0002';
              END IF;

              UPDATE hub.content
                SET status='review_required',version=version+1,
                    updated_at=statement_timestamp()
                WHERE id=_content AND organization_id=_owner AND version=_version
                  AND status='ai_draft';
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_draft_version_conflict' USING ERRCODE='40001';
              END IF;

              INSERT INTO hub.review_events
                (id,content_id,revision_id,admin_account_id,action,metadata)
              VALUES (gen_random_uuid(),_content,v_revision,v_actor,'submitted',
                jsonb_build_object('source','web_admin'));

              INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,
                 device_session_id,action,outcome,organization_id,reason_code)
              VALUES (gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                v_session,'admin.hub.draft.submit_review','succeeded',_owner,'hub_editorial');

              RETURN jsonb_build_object(
                'id',_content,'organization_id',_owner,
                'status','review_required','version',_version+1
              );
            END $hub_submit$;
            REVOKE ALL ON FUNCTION hub.submit_admin_draft_review(
              text,text,uuid,bigint) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.submit_admin_draft_review(text,text,uuid,bigint);
            """);
}
