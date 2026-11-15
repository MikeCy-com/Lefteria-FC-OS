using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Permanent, private player development goals with append-only progress events.
/// Delegated roles cannot access cross-city player history until geographic scoping.
/// A player changing groups or seasons retains this history.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115900000_PlayerDevelopmentGoals")]
public sealed class PlayerDevelopmentGoals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.player_development_goals(
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        player_id varchar(128) COLLATE "C" NOT NULL,
        title varchar(200) NOT NULL CHECK(length(btrim(title)) BETWEEN 1 AND 200),
        description varchar(2000) NOT NULL DEFAULT '',
        category varchar(16) NOT NULL CHECK(category IN
          ('general','physical','technical','tactical','mental','social')),
        target_date date,
        source_evaluation_id uuid,
        progress smallint NOT NULL DEFAULT 0 CHECK(progress BETWEEN 0 AND 100 AND progress%10=0),
        status varchar(16) NOT NULL DEFAULT 'active' CHECK(status IN
          ('active','completed','archived')),
        version integer NOT NULL DEFAULT 1 CHECK(version BETWEEN 1 AND 10000),
        created_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        updated_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(organization_id,id),
        FOREIGN KEY(organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,source_evaluation_id)
          REFERENCES academy_private.evaluations(organization_id,id) ON DELETE RESTRICT,
        CHECK(status <> 'completed' OR progress=100),
        CHECK(status <> 'active' OR progress<100)
      );
      CREATE INDEX ix_player_development_goals_player
        ON club.player_development_goals(organization_id,player_id,created_at DESC,id);
      REVOKE ALL ON club.player_development_goals FROM PUBLIC;
      ALTER TABLE club.player_development_goals ENABLE ROW LEVEL SECURITY;

      CREATE TABLE club.player_development_goal_events(
        organization_id varchar(32) NOT NULL,
        goal_id uuid NOT NULL,
        player_id varchar(128) COLLATE "C" NOT NULL,
        version integer NOT NULL CHECK(version BETWEEN 1 AND 10000),
        request_id uuid NOT NULL,
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        event_kind varchar(16) NOT NULL CHECK(event_kind IN
          ('created','progress','completed','reopened','archived')),
        progress smallint NOT NULL CHECK(progress BETWEEN 0 AND 100 AND progress%10=0),
        status varchar(16) NOT NULL CHECK(status IN ('active','completed','archived')),
        note varchar(500) NOT NULL DEFAULT '',
        payload_fingerprint char(32) COLLATE "C" NOT NULL
          CHECK(payload_fingerprint ~ '^[0-9a-f]{32}$'),
        recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(goal_id,version),
        UNIQUE(organization_id,request_id),
        FOREIGN KEY(organization_id,goal_id)
          REFERENCES club.player_development_goals(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT
      );
      CREATE INDEX ix_player_development_goal_events_player
        ON club.player_development_goal_events(organization_id,player_id,recorded_at DESC);
      REVOKE ALL ON club.player_development_goal_events FROM PUBLIC;
      ALTER TABLE club.player_development_goal_events ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION club.reject_goal_event_mutation()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp
      AS $goal_immutable$
      BEGIN
        RAISE EXCEPTION 'development_goal_history_is_immutable' USING ERRCODE='42501';
      END $goal_immutable$;
      CREATE TRIGGER tr_goal_events_immutable
        BEFORE UPDATE OR DELETE ON club.player_development_goal_events
        FOR EACH ROW EXECUTE FUNCTION club.reject_goal_event_mutation();
      REVOKE ALL ON FUNCTION club.reject_goal_event_mutation() FROM PUBLIC;

      CREATE FUNCTION club.require_development_goal_manager(
        _digest text,_owner text,_player text
      ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $goal_authority$
      DECLARE v_actor uuid;v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'development_goal_invalid_scope' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'development_goal_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF v_actor IS NULL OR v_role<>'super_admin'
          OR NOT EXISTS(
            SELECT 1 FROM club.players p
            WHERE p.organization_id=_owner AND p.id=_player
              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
              AND (
                (_owner='lefteria_fc' AND p.team_type='First Team'
                  AND EXISTS(SELECT 1 FROM club.teams t
                    WHERE t.organization_id=_owner AND t.id=p.team_id))
                OR (_owner='lefteria_fc_academy' AND p.team_type='Academy'
                  AND (EXISTS(SELECT 1 FROM club.academy_groups ag
                        WHERE ag.organization_id=_owner
                          AND ag.id=p.primary_academy_group_id)
                    OR EXISTS(SELECT 1 FROM club.player_academy_groups m
                        JOIN club.academy_groups ag
                          ON ag.organization_id=m.organization_id
                          AND ag.id=m.academy_group_id
                        WHERE m.organization_id=_owner AND m.player_id=p.id)))
              )
          ) THEN
          RAISE EXCEPTION 'development_goal_access_denied' USING ERRCODE='42501';
        END IF;
        RETURN v_actor;
      END $goal_authority$;
      REVOKE ALL ON FUNCTION club.require_development_goal_manager(text,text,text) FROM PUBLIC;

      CREATE FUNCTION club.read_admin_player_development_goals(
        _digest text,_owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $goal_read$
      DECLARE v_total integer;v_rows jsonb;
      BEGIN
        PERFORM club.require_development_goal_manager(_digest,_owner,_player);
        SELECT count(*) INTO v_total FROM club.player_development_goals g
          WHERE g.organization_id=_owner AND g.player_id=_player;
        IF v_total>100 THEN
          RAISE EXCEPTION 'development_goal_list_too_large' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',g.id,'title',g.title,'description',g.description,
          'category',g.category,'target_date',g.target_date,
          'source_evaluation_id',g.source_evaluation_id,
          'progress',g.progress,'status',g.status,'version',g.version,
          'created_at',g.created_at,'updated_at',g.updated_at,
          'history',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'version',e.version,'event',e.event_kind,
              'progress',e.progress,'status',e.status,'note',e.note,
              'recorded_at',e.recorded_at
            ) ORDER BY e.version DESC),'[]'::jsonb)
            FROM (
              SELECT version,event_kind,progress,status,note,recorded_at
              FROM club.player_development_goal_events
              WHERE organization_id=_owner AND goal_id=g.id
              ORDER BY version DESC LIMIT 50
            ) e
          ),
          'history_count',(
            SELECT count(*) FROM club.player_development_goal_events e
            WHERE e.organization_id=_owner AND e.goal_id=g.id)
          ) ORDER BY g.created_at DESC,g.id),'[]'::jsonb)
        INTO v_rows FROM club.player_development_goals g
        WHERE g.organization_id=_owner AND g.player_id=_player;
        RETURN jsonb_build_object('organization_id',_owner,'player_id',_player,
          'total',v_total,'goals',v_rows);
      END $goal_read$;
      REVOKE ALL ON FUNCTION club.read_admin_player_development_goals(
        text,text,text) FROM PUBLIC;

      CREATE FUNCTION club.create_admin_player_development_goal(
        _digest text,_owner text,_player text,_request uuid,
        _title text,_description text,_category text,_target date,
        _evaluation uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $goal_create$
      DECLARE v_actor uuid;v_goal uuid:=gen_random_uuid();v_old record;
        v_fingerprint char(32);
      BEGIN
        IF _request IS NULL OR _title IS NULL OR _title<>btrim(_title)
          OR length(_title) NOT BETWEEN 1 AND 200
          OR _description IS NULL OR length(_description)>2000
          OR _category NOT IN ('general','physical','technical','tactical','mental','social')
        THEN RAISE EXCEPTION 'development_goal_payload_invalid' USING ERRCODE='22023';
        END IF;
        v_actor:=club.require_development_goal_manager(_digest,_owner,_player);
        v_fingerprint:=md5(jsonb_build_array(_player,_title,_description,
          _category,_target,_evaluation)::text);
        PERFORM pg_advisory_xact_lock(hashtextextended(_owner||':'||_request::text,0));
        SELECT e.goal_id,e.actor_id,e.player_id,e.payload_fingerprint
          INTO v_old FROM club.player_development_goal_events e
          WHERE e.organization_id=_owner AND e.request_id=_request;
        IF FOUND THEN
          IF v_old.actor_id<>v_actor OR v_old.player_id<>_player
            OR v_old.payload_fingerprint<>v_fingerprint THEN
            RAISE EXCEPTION 'development_goal_request_conflict' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('id',v_old.goal_id,'organization_id',_owner,
            'player_id',_player,'replay',true);
        END IF;
        IF _evaluation IS NOT NULL AND NOT EXISTS(
          SELECT 1 FROM academy_private.evaluations e
          WHERE e.id=_evaluation AND e.organization_id=_owner
            AND e.player_id=_player AND e.withdrawn_at IS NULL)
        THEN RAISE EXCEPTION 'development_goal_evaluation_invalid' USING ERRCODE='42501';
        END IF;
        IF (SELECT count(*) FROM club.player_development_goals
            WHERE organization_id=_owner AND player_id=_player)>=100
        THEN RAISE EXCEPTION 'development_goal_limit_reached' USING ERRCODE='54000'; END IF;
        INSERT INTO club.player_development_goals(id,organization_id,player_id,
          title,description,category,target_date,source_evaluation_id,
          created_by,updated_by)
        VALUES(v_goal,_owner,_player,_title,_description,_category,_target,
          _evaluation,v_actor,v_actor);
        INSERT INTO club.player_development_goal_events(
          organization_id,goal_id,player_id,version,request_id,actor_id,
          event_kind,progress,status,payload_fingerprint)
        VALUES(_owner,v_goal,_player,1,_request,v_actor,'created',0,'active',v_fingerprint);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          (SELECT s.id FROM identity.admin_sessions s
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() LIMIT 1),
          'admin.player.development_goal.create','succeeded',_owner,'created');
        RETURN jsonb_build_object('id',v_goal,'organization_id',_owner,
          'player_id',_player,'version',1,'replay',false);
      END $goal_create$;
      REVOKE ALL ON FUNCTION club.create_admin_player_development_goal(
        text,text,text,uuid,text,text,text,date,uuid) FROM PUBLIC;

      CREATE FUNCTION club.update_admin_player_development_goal(
        _digest text,_owner text,_player text,_goal uuid,_request uuid,
        _expected integer,_progress integer,_archive boolean,_note text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $goal_update$
      DECLARE v_actor uuid;v_old record;v_previous club.player_development_goals%ROWTYPE;
        v_fingerprint char(32);v_status text;v_event text;v_version integer;
      BEGIN
        IF _goal IS NULL OR _request IS NULL OR _expected NOT BETWEEN 1 AND 9999
          OR _progress NOT BETWEEN 0 AND 100 OR _progress%10<>0
          OR _archive IS NULL OR _note IS NULL OR length(_note)>500
        THEN RAISE EXCEPTION 'development_goal_update_invalid' USING ERRCODE='22023';
        END IF;
        v_actor:=club.require_development_goal_manager(_digest,_owner,_player);
        v_fingerprint:=md5(jsonb_build_array(_player,_goal,_expected,
          _progress,_archive,_note)::text);
        PERFORM pg_advisory_xact_lock(hashtextextended(_owner||':'||_request::text,0));
        SELECT e.goal_id,e.actor_id,e.player_id,e.payload_fingerprint,e.version
          INTO v_old FROM club.player_development_goal_events e
          WHERE e.organization_id=_owner AND e.request_id=_request;
        IF FOUND THEN
          IF v_old.actor_id<>v_actor OR v_old.goal_id<>_goal
            OR v_old.player_id<>_player
            OR v_old.payload_fingerprint<>v_fingerprint THEN
            RAISE EXCEPTION 'development_goal_request_conflict' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('id',_goal,'organization_id',_owner,
            'player_id',_player,'version',v_old.version,'replay',true);
        END IF;
        SELECT * INTO v_previous FROM club.player_development_goals
          WHERE organization_id=_owner AND player_id=_player AND id=_goal FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'development_goal_not_found' USING ERRCODE='42501'; END IF;
        IF v_previous.version<>_expected THEN
          RAISE EXCEPTION 'development_goal_stale_version' USING ERRCODE='23505';
        END IF;
        IF v_previous.status='archived' THEN
          RAISE EXCEPTION 'development_goal_is_archived' USING ERRCODE='22023';
        END IF;
        v_status:=CASE WHEN _archive THEN 'archived'
          WHEN _progress=100 THEN 'completed' ELSE 'active' END;
        v_event:=CASE WHEN _archive THEN 'archived'
          WHEN _progress=100 THEN 'completed'
          WHEN v_previous.status='completed' THEN 'reopened'
          ELSE 'progress' END;
        v_version:=_expected+1;
        UPDATE club.player_development_goals SET
          progress=_progress,status=v_status,version=v_version,
          updated_by=v_actor,updated_at=clock_timestamp()
        WHERE id=_goal;
        INSERT INTO club.player_development_goal_events(
          organization_id,goal_id,player_id,version,request_id,actor_id,
          event_kind,progress,status,note,payload_fingerprint)
        VALUES(_owner,_goal,_player,v_version,_request,v_actor,
          v_event,_progress,v_status,_note,v_fingerprint);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          (SELECT s.id FROM identity.admin_sessions s
            WHERE s.token_digest=_digest AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() LIMIT 1),
          'admin.player.development_goal.update','succeeded',_owner,v_event);
        RETURN jsonb_build_object('id',_goal,'organization_id',_owner,
          'player_id',_player,'version',v_version,'replay',false);
      END $goal_update$;
      REVOKE ALL ON FUNCTION club.update_admin_player_development_goal(
        text,text,text,uuid,uuid,integer,integer,boolean,text) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.update_admin_player_development_goal(
        text,text,text,uuid,uuid,integer,integer,boolean,text);
      DROP FUNCTION club.create_admin_player_development_goal(
        text,text,text,uuid,text,text,text,date,uuid);
      DROP FUNCTION club.read_admin_player_development_goals(text,text,text);
      DROP FUNCTION club.require_development_goal_manager(text,text,text);
      DROP TRIGGER tr_goal_events_immutable ON club.player_development_goal_events;
      DROP FUNCTION club.reject_goal_event_mutation();
      DROP TABLE club.player_development_goal_events;
      DROP TABLE club.player_development_goals;
    """);
}