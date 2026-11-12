using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Manager returns a rejected text review for human correction.
/// Revision history and original decision remain immutable; publishing is forbidden.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112080000_HubReturnRejectedDraft")]
public sealed class HubReturnRejectedDraft : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.return_rejected_admin_draft(
              _digest text,_owner text,_content uuid,_version bigint
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $hub_return$
            DECLARE
              v_actor uuid; v_session uuid; v_role text;
              v_content hub.content%ROWTYPE;
              v_revision hub.revisions%ROWTYPE;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _content IS NULL OR _version IS NULL
                OR _version<1 OR _version=9223372036854775807
              THEN RAISE EXCEPTION 'invalid_hub_return' USING ERRCODE='22023';
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
                RAISE EXCEPTION 'hub_return_permission_denied' USING ERRCODE='42501';
              END IF;

              SELECT * INTO v_content FROM hub.content
                WHERE id=_content AND organization_id=_owner FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_draft_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_content.version<>_version OR v_content.status<>'review_required' THEN
                RAISE EXCEPTION 'hub_return_version_conflict' USING ERRCODE='40001';
              END IF;
              SELECT * INTO v_revision FROM hub.revisions
                WHERE content_id=_content ORDER BY revision DESC LIMIT 1 FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_revision_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_revision.text_review<>'rejected' THEN
                RAISE EXCEPTION 'hub_return_requires_rejection' USING ERRCODE='40001';
              END IF;

              UPDATE hub.content
                SET status='ai_draft',version=version+1,updated_at=statement_timestamp()
                WHERE id=_content AND organization_id=_owner
                  AND version=_version AND status='review_required';
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_return_version_conflict' USING ERRCODE='40001';
              END IF;

              -- Do not reset approvals or overwrite review evidence. No new
              -- revision exists at version+1: an editor must revise before
              -- submit-for-review can succeed again.
              INSERT INTO hub.review_events
                (id,content_id,revision_id,admin_account_id,action,metadata)
              VALUES (gen_random_uuid(),_content,v_revision.id,v_actor,'revised',
                jsonb_build_object('source','manager_return','decision','rejected',
                                   'previous_version',_version));
              INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                 action,outcome,organization_id,reason_code)
              VALUES (gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.hub.review.return_for_revision','succeeded',_owner,'text_rejected');

              RETURN jsonb_build_object('id',_content,'organization_id',_owner,
                'status','ai_draft','version',_version+1,
                'requires_revision',true,'last_text_review','rejected');
            END $hub_return$;
            REVOKE ALL ON FUNCTION hub.return_rejected_admin_draft(
              text,text,uuid,bigint) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.return_rejected_admin_draft(text,text,uuid,bigint);
            """);
}
