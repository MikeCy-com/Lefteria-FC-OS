using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Authorized, transactional draft and initial-revision creation only.
/// This migration does not add a publish, AI, media or payment capability.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110190000_HubAdminDraftCreate")]
public sealed class HubAdminDraftCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.create_admin_draft(
              _digest text, _owner text, _id uuid, _revision_id uuid,
              _kind text, _title text, _body text,
              _parent boolean, _player boolean,
              _age_min smallint, _age_max smallint
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $hub_create$
            DECLARE v_actor uuid; v_session uuid; v_role text;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _id IS NULL OR _revision_id IS NULL
                OR _kind NOT IN ('article','drill','video','visual','course')
                OR _title IS NULL OR length(_title) NOT BETWEEN 1 AND 180
                OR btrim(_title) <> _title OR _title ~ '[[:cntrl:]]'
                OR _body IS NULL OR length(_body) NOT BETWEEN 1 AND 20000
                OR _body <> btrim(_body) OR _body ~ '[[:cntrl:]]'
                OR _parent IS NULL OR _player IS NULL OR NOT (_parent OR _player)
                OR ((_age_min IS NULL) <> (_age_max IS NULL))
                OR (_age_min IS NOT NULL AND
                    (_age_min < 4 OR _age_max > 99 OR _age_max < _age_min))
              THEN
                RAISE EXCEPTION 'invalid_hub_draft' USING ERRCODE='22023';
              END IF;

              -- Serialize the write with an active session, account and the
              -- requested organization grant. Revocation cannot race this write.
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
                RAISE EXCEPTION 'hub_draft_permission_denied' USING ERRCODE='42501';
              END IF;

              -- One initial human-created, unpublished revision. No approval
              -- or published view is granted by this operation.
              INSERT INTO hub.content
                (id,organization_id,kind,status,title,audience_parent,
                 audience_player,age_min,age_max,provenance,version)
              VALUES
                (_id,_owner,_kind,'ai_draft',_title,_parent,_player,
                 _age_min,_age_max,'human',1);

              INSERT INTO hub.revisions
                (id,content_id,revision,body_text,text_review,visual_review)
              VALUES (_revision_id,_id,1,_body,'pending','pending');

              INSERT INTO hub.review_events
                (id,content_id,revision_id,admin_account_id,action,metadata)
              VALUES (gen_random_uuid(),_id,_revision_id,NULL,'generated',
                jsonb_build_object('source','human_admin_draft'));

              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,
                device_session_id,action,outcome,organization_id,reason_code
              ) VALUES (
                gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                v_session,'admin.hub.draft.create','succeeded',_owner,'hub_editorial'
              );

              RETURN jsonb_build_object(
                'id',_id,'organization_id',_owner,'kind',_kind,
                'title',_title,'status','ai_draft','version',1,
                'audience_parent',_parent,'audience_player',_player
              );
            END $hub_create$;
            REVOKE ALL ON FUNCTION hub.create_admin_draft(
              text,text,uuid,uuid,text,text,text,boolean,boolean,smallint,smallint
            ) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.create_admin_draft(
              text,text,uuid,uuid,text,text,text,boolean,boolean,smallint,smallint
            );
            """);
}