using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Idempotent, auditable new evaluation snapshot creation by authenticated
/// management for a currently assigned player. No app/mobile table grants.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110140000_PrivateEvaluationCreation")]
public sealed class PrivateEvaluationCreation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE academy_private.evaluation_create_requests (
        organization_id varchar(32) NOT NULL REFERENCES club.organizations(id),
        request_id uuid NOT NULL,
        player_id varchar(128) COLLATE "C" NOT NULL,
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id),
        evaluation_id uuid NOT NULL UNIQUE,
        payload_fingerprint char(32) COLLATE "C" NOT NULL,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY (organization_id,request_id),
        FOREIGN KEY (organization_id,player_id)
          REFERENCES club.players(organization_id,id)
      );
      REVOKE ALL ON academy_private.evaluation_create_requests FROM PUBLIC;
      ALTER TABLE academy_private.evaluation_create_requests ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION academy_private.create_admin_player_evaluation(
        _digest text,_owner text,_player text,_request uuid,
        _summary text,_notes text,_technical jsonb,_tactical jsonb,
        _physical jsonb,_character jsonb,_overall jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $private_evaluation_create$
      DECLARE
        v_actor uuid; v_session uuid; v_role text;
        v_existing academy_private.evaluation_create_requests%ROWTYPE;
        v_id uuid:=gen_random_uuid();
        v_created timestamptz:=clock_timestamp();
        v_fingerprint char(32);
        v_affected integer;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player ~ '[[:cntrl:]]'
          OR _request IS NULL
          OR _summary IS NULL OR length(btrim(_summary)) NOT BETWEEN 1 AND 500
          OR _summary<>btrim(_summary)
          OR _notes IS NULL OR length(_notes)>4000
          OR _overall IS NULL OR jsonb_typeof(_overall)<>'object'
          OR pg_column_size(_overall)>4096
        THEN RAISE EXCEPTION 'evaluation_payload_invalid' USING ERRCODE='22023';
        END IF;
        PERFORM academy_private.validate_academy_score_group(_technical,'technical');
        PERFORM academy_private.validate_academy_score_group(_tactical,'tactical');
        PERFORM academy_private.validate_academy_score_group(_physical,'physical');
        PERFORM academy_private.validate_academy_score_group(_character,'character');
        IF EXISTS(SELECT 1 FROM jsonb_each(_overall) kv
          WHERE kv.key NOT IN ('strengths','areas_to_improve','current_focus',
            'goal_3_month','goal_6_month','goal_12_month')
            OR jsonb_typeof(kv.value)<>'string'
            OR length(kv.value #>> '{}')>1000)
        THEN RAISE EXCEPTION 'evaluation_overall_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'evaluation_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'evaluation_writer_denied' USING ERRCODE='42501';
        END IF;
        PERFORM 1 FROM club.players p WHERE p.organization_id=_owner
          AND p.id=_player AND p.is_active
          AND NOT p.is_archived AND NOT p.is_transferred
          AND (
            (_owner='lefteria_fc' AND p.team_type='First Team'
              AND EXISTS(SELECT 1 FROM club.teams t
                WHERE t.organization_id=_owner AND t.id=p.team_id))
            OR (_owner='lefteria_fc_academy' AND p.team_type='Academy'
              AND (EXISTS(SELECT 1 FROM club.academy_groups g
                     WHERE g.organization_id=_owner AND g.id=p.primary_academy_group_id)
                OR EXISTS(SELECT 1 FROM club.player_academy_groups m
                     JOIN club.academy_groups g
                       ON g.organization_id=m.organization_id AND g.id=m.academy_group_id
                     WHERE m.organization_id=_owner AND m.player_id=p.id)))
          ) FOR SHARE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'evaluation_player_not_assigned' USING ERRCODE='42501';
        END IF;
        v_fingerprint:=md5(jsonb_build_array(_player,_summary,_notes,
          _technical,_tactical,_physical,_character,_overall)::text);
        INSERT INTO academy_private.evaluation_create_requests(
          organization_id,request_id,player_id,actor_id,evaluation_id,payload_fingerprint)
        VALUES(_owner,_request,_player,v_actor,v_id,v_fingerprint)
        ON CONFLICT DO NOTHING;
        GET DIAGNOSTICS v_affected=ROW_COUNT;
        IF v_affected=0 THEN
          SELECT * INTO v_existing FROM academy_private.evaluation_create_requests
            WHERE organization_id=_owner AND request_id=_request;
          IF NOT FOUND OR v_existing.actor_id<>v_actor
            OR v_existing.player_id<>_player
            OR v_existing.payload_fingerprint<>v_fingerprint THEN
            RAISE EXCEPTION 'evaluation_request_conflict' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('id',v_existing.evaluation_id,
            'player_id',_player,'organization_id',_owner,'replay',true);
        END IF;
        INSERT INTO academy_private.evaluations(
          id,organization_id,player_id,author_id,created_at,edit_until)
        VALUES(v_id,_owner,_player,v_actor,v_created,v_created+interval '24 hours');
        INSERT INTO academy_private.evaluation_revisions(
          evaluation_id,revision,editor_id,summary,notes,technical,tactical,
          physical,character,overall)
        VALUES(v_id,1,v_actor,_summary,_notes,_technical,_tactical,
          _physical,_character,_overall);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.player.evaluation.create','succeeded',_owner,'snapshot');
        RETURN jsonb_build_object('id',v_id,'player_id',_player,
          'organization_id',_owner,'revision',1,'replay',false);
      END $private_evaluation_create$;
      REVOKE ALL ON FUNCTION academy_private.create_admin_player_evaluation(
        text,text,text,uuid,text,text,jsonb,jsonb,jsonb,jsonb,jsonb) FROM PUBLIC;
    """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION academy_private.create_admin_player_evaluation(
        text,text,text,uuid,text,text,jsonb,jsonb,jsonb,jsonb,jsonb);
      DROP TABLE academy_private.evaluation_create_requests;
    """);
}
