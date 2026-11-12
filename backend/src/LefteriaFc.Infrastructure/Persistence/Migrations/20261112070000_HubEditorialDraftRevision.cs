using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Human editor revision of an unpublished draft. No review bypass,
/// visual approval, AI generation, payment, or publication.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112070000_HubEditorialDraftRevision")]
public sealed class HubEditorialDraftRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.revise_admin_draft(
              _digest text,_owner text,_content uuid,_version bigint,
              _title text,_body text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $hub_revise$
            DECLARE
              v_actor uuid; v_session uuid; v_role text;
              v_content hub.content%ROWTYPE;
              v_revision uuid := gen_random_uuid();
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _content IS NULL OR _version IS NULL OR _version<1
                OR _version=9223372036854775807
                OR _title IS NULL OR length(_title) NOT BETWEEN 1 AND 180
                OR btrim(_title)<>_title OR _title ~ '[[:cntrl:]]'
                OR _body IS NULL OR length(_body) NOT BETWEEN 1 AND 20000
                OR btrim(_body)<>_body OR _body ~ '[[:cntrl:]]'
              THEN RAISE EXCEPTION 'invalid_hub_revision' USING ERRCODE='22023';
              END IF;

              -- Authority lock prevents simultaneous session/grant revocation.
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
                RAISE EXCEPTION 'hub_revision_permission_denied' USING ERRCODE='42501';
              END IF;

              SELECT * INTO v_content FROM hub.content
                WHERE id=_content AND organization_id=_owner FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_draft_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_content.version<>_version OR v_content.status<>'ai_draft' THEN
                RAISE EXCEPTION 'hub_revision_conflict' USING ERRCODE='40001';
              END IF;

              -- Append immutable history instead of modifying a reviewed revision.
              INSERT INTO hub.revisions
                (id,content_id,revision,body_text,text_review,visual_review)
              VALUES (v_revision,_content,_version+1,_body,'pending','pending');
              UPDATE hub.content
                SET title=_title,version=version+1,updated_at=statement_timestamp()
                WHERE id=_content AND organization_id=_owner
                  AND version=_version AND status='ai_draft';
              IF NOT FOUND THEN
                RAISE EXCEPTION 'hub_revision_conflict' USING ERRCODE='40001';
              END IF;

              INSERT INTO hub.review_events
                (id,content_id,revision_id,admin_account_id,action,metadata)
              VALUES (gen_random_uuid(),_content,v_revision,v_actor,'generated',
                jsonb_build_object('source','human_admin_revision','previous_version',_version));
              INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                 action,outcome,organization_id,reason_code)
              VALUES (gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.hub.draft.revise','succeeded',_owner,'hub_editorial');

              RETURN jsonb_build_object('id',_content,'organization_id',_owner,
                'status','ai_draft','version',_version+1,'revision_id',v_revision,
                'text_review','pending','visual_review','pending');
            END $hub_revise$;
            REVOKE ALL ON FUNCTION hub.revise_admin_draft(
              text,text,uuid,bigint,text,text) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.revise_admin_draft(text,text,uuid,bigint,text,text);
            """);
}
