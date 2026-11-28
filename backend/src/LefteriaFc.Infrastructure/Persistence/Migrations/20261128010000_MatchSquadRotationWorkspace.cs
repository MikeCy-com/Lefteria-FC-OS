using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128010000_MatchSquadRotationWorkspace")]
public sealed class MatchSquadRotationWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.match_workspaces (
        organization_id varchar(32) NOT NULL,
        kind varchar(16) NOT NULL DEFAULT 'match' CHECK(kind='match'),
        match_id varchar(128) COLLATE "C" NOT NULL,
        host_id varchar(128) COLLATE "C" NOT NULL,
        version integer NOT NULL CHECK(version BETWEEN 1 AND 1000000),
        state text NOT NULL CHECK(state IN ('draft','selected','submitted','approved','rejected')),
        squad jsonb NOT NULL CHECK(jsonb_typeof(squad)='array' AND jsonb_array_length(squad)<=60),
        statistics jsonb NOT NULL DEFAULT '[]'::jsonb CHECK(jsonb_typeof(statistics)='array' AND jsonb_array_length(statistics)<=60),
        rotation_note text NOT NULL DEFAULT '' CHECK(length(rotation_note)<=500),
        review_reason text NOT NULL DEFAULT '' CHECK(length(review_reason)<=500),
        updated_by uuid NOT NULL REFERENCES identity.mobile_identities(id),
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(kind,match_id),
        FOREIGN KEY(organization_id,kind,match_id)
          REFERENCES club.calendar_items(organization_id,kind,id) ON DELETE RESTRICT
      );
      CREATE INDEX ix_match_workspace_host ON club.match_workspaces(organization_id,host_id,state);
      CREATE TABLE club.match_workspace_revisions (
        kind varchar(16) NOT NULL DEFAULT 'match' CHECK(kind='match'),
        match_id varchar(128) COLLATE "C" NOT NULL,
        version integer NOT NULL,
        actor_id uuid NOT NULL REFERENCES identity.mobile_identities(id),
        action text NOT NULL CHECK(action IN ('save_squad','select_squad','submit_statistics','approve','reject')),
        snapshot jsonb NOT NULL CHECK(jsonb_typeof(snapshot)='object'),
        recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(kind,match_id,version),
        FOREIGN KEY(kind,match_id) REFERENCES club.match_workspaces(kind,match_id) ON DELETE RESTRICT
      );
      ALTER TABLE club.match_workspaces ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.match_workspace_revisions ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON club.match_workspaces,club.match_workspace_revisions FROM PUBLIC;

      -- Resolve a single canonical host. A guest-only coach may read their RSVP
      -- elsewhere but may never manage another group's entire match.
      CREATE FUNCTION club.require_mobile_match_host(_actor uuid,_phone text,_role text,
        _expires timestamptz,_match text)
      RETURNS TABLE(owner text,host text,day date,completed boolean)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $host$
      DECLARE v_groups jsonb;v_admin uuid;v_admin_role text;v_scope uuid;
      BEGIN
        IF _role NOT IN ('coach','management') OR _match IS NULL
          OR length(_match) NOT BETWEEN 1 AND 128 THEN
          RAISE EXCEPTION 'match_scope_denied' USING ERRCODE='42501'; END IF;
        v_groups:=club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups';
        SELECT c.organization_id,s.record_id,c.staff_day,
          lower(coalesce(c.status,'')) IN ('completed','finished','ft','final')
        INTO owner,host,day,completed
        FROM club.calendar_items c JOIN club.calendar_item_scopes s
          ON s.kind=c.kind AND s.event_id=c.id AND s.organization_id=c.organization_id
        WHERE c.kind='match' AND c.id=_match AND NOT c.is_archived AND NOT c.is_cancelled
          AND ((c.organization_id='lefteria_fc' AND s.scope_kind='team')
            OR (c.organization_id='lefteria_fc_academy' AND s.scope_kind='group'))
          AND (SELECT count(*) FROM club.calendar_item_scopes x WHERE x.kind=c.kind AND x.event_id=c.id)=1
          AND EXISTS(SELECT 1 FROM jsonb_array_elements(v_groups) g(value)
            WHERE g.value->>'organization_id'=c.organization_id AND g.value->>'id'=s.record_id
              AND g.value->>'kind'=CASE WHEN s.scope_kind='team' THEN 'team' ELSE 'academy_group' END)
          AND EXISTS(SELECT 1 FROM club.calendar_import_state WHERE id='initial' AND is_complete);
        IF NOT FOUND THEN RAISE EXCEPTION 'match_scope_denied' USING ERRCODE='42501'; END IF;
        IF _role='management' THEN
          SELECT a.id,a.role INTO v_admin,v_admin_role FROM identity.mobile_identities i
          JOIN identity.admin_accounts a ON a.id=i.management_admin_id
          JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id AND g.organization_id=owner
          WHERE i.id=_actor AND i.is_active AND a.is_active AND NOT a.invite_pending
            AND a.active_organization_id=owner AND a.role IN ('manager','super_admin') FOR SHARE OF a,g;
          IF NOT FOUND THEN RAISE EXCEPTION 'match_review_denied' USING ERRCODE='42501'; END IF;
          IF owner='lefteria_fc_academy' THEN
            PERFORM club.require_training_drill_group(v_admin,host);
          ELSE
            SELECT id INTO v_scope FROM club.football_operational_scopes
            WHERE organization_id=owner AND team_id=host;
            IF NOT coalesce(club.admin_football_allowed(v_admin,v_admin_role,owner,v_scope,NULL,true),false)
              THEN RAISE EXCEPTION 'match_review_denied' USING ERRCODE='42501'; END IF;
          END IF;
        END IF;
        RETURN NEXT;
      END $host$;
      REVOKE ALL ON FUNCTION club.require_mobile_match_host(uuid,text,text,timestamptz,text) FROM PUBLIC;

      CREATE FUNCTION club.match_workspace_roster(_owner text,_host text,_match text)
      RETURNS TABLE(player_id text,player_name text,shirt_number integer,position text)
      LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $roster$
        SELECT p.id::text,p.name::text,p.number,p.position::text FROM club.players p
        WHERE p.organization_id=_owner AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          AND ((_owner='lefteria_fc' AND p.team_type='First Team' AND p.team_id=_host)
            OR (_owner='lefteria_fc_academy' AND p.team_type='Academy' AND
              (p.primary_academy_group_id=_host OR EXISTS(SELECT 1 FROM club.player_academy_groups m
                WHERE m.organization_id=_owner AND m.player_id=p.id AND m.academy_group_id=_host)))
            OR EXISTS(SELECT 1 FROM club.calendar_item_guests g WHERE g.kind='match' AND g.event_id=_match
                AND g.organization_id=_owner AND g.player_id=p.id))
      $roster$;
      REVOKE ALL ON FUNCTION club.match_workspace_roster(text,text,text) FROM PUBLIC;

      CREATE FUNCTION club.read_mobile_match_workspace(_actor uuid,_phone text,_role text,
        _expires timestamptz,_match text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_owner text;v_host text;v_day date;v_completed boolean;
        v_plan club.match_workspaces%ROWTYPE;v_roster jsonb;v_stale boolean;
      BEGIN
        SELECT owner,host,day,completed INTO STRICT v_owner,v_host,v_day,v_completed
          FROM club.require_mobile_match_host(_actor,_phone,_role,_expires,_match);
        SELECT * INTO v_plan FROM club.match_workspaces WHERE kind='match' AND match_id=_match
          AND organization_id=v_owner AND host_id=v_host;
        -- A changed host is not silently allowed to inherit another roster.
        IF NOT FOUND AND EXISTS(SELECT 1 FROM club.match_workspaces WHERE kind='match' AND match_id=_match)
          THEN RAISE EXCEPTION 'match_host_changed' USING ERRCODE='42501'; END IF;
        WITH eligible AS MATERIALIZED (
          SELECT * FROM club.match_workspace_roster(v_owner,v_host,_match) LIMIT 201
        ), history AS (
          SELECT c.staff_day,w.match_id,s.value->>'player_id' AS player_id,s.value->>'assignment' AS assignment
          FROM club.match_workspaces w JOIN club.calendar_items c ON c.kind=w.kind AND c.id=w.match_id
          CROSS JOIN LATERAL jsonb_array_elements(w.squad) s(value)
          WHERE v_owner='lefteria_fc_academy' AND w.organization_id=v_owner AND w.host_id=v_host
            AND w.state IN ('selected','submitted','approved','rejected')
            AND c.staff_day<v_day AND c.staff_day>=v_day-365 AND NOT c.is_cancelled AND NOT c.is_archived
        )
        SELECT coalesce(jsonb_agg(jsonb_build_object('player_id',r.player_id,'name',r.player_name,
          'number',r.shirt_number,'position',CASE WHEN v_owner='lefteria_fc' THEN r.position ELSE NULL END,
          'last_assignment',(SELECT h.assignment FROM history h WHERE h.player_id=r.player_id
            ORDER BY h.staff_day DESC,h.match_id DESC LIMIT 1),
          'starts',(SELECT count(*) FROM history h WHERE h.player_id=r.player_id AND h.assignment='starter'),
          'reserves',(SELECT count(*) FROM history h WHERE h.player_id=r.player_id AND h.assignment='reserve'))
          ORDER BY r.player_name COLLATE "C",r.player_id COLLATE "C"),'[]'::jsonb)
        INTO v_roster FROM eligible r;
        IF jsonb_array_length(v_roster)>200 THEN RAISE EXCEPTION 'match_roster_limit' USING ERRCODE='54000'; END IF;
        SELECT EXISTS(SELECT 1 FROM jsonb_array_elements(coalesce(v_plan.squad,'[]'::jsonb)) s(value)
          WHERE NOT EXISTS(SELECT 1 FROM jsonb_array_elements(v_roster) r(value)
            WHERE r.value->>'player_id'=s.value->>'player_id')) INTO v_stale;
        RETURN jsonb_build_object('match_id',_match,'organization_id',v_owner,'host_id',v_host,
          'mode',CASE WHEN v_owner='lefteria_fc' THEN 'first_team' ELSE 'grassroots' END,
          'version',coalesce(v_plan.version,0),'state',coalesce(v_plan.state,'draft'),
          'completed',v_completed,'roster_stale',v_stale,'roster',v_roster,
          'squad',CASE WHEN v_stale THEN '[]'::jsonb ELSE coalesce(v_plan.squad,'[]'::jsonb) END,
          'statistics',CASE WHEN v_stale THEN '[]'::jsonb ELSE coalesce(v_plan.statistics,'[]'::jsonb) END,
          'rotation_note',CASE WHEN v_stale THEN '' ELSE coalesce(v_plan.rotation_note,'') END,
          'review_reason',CASE WHEN v_stale THEN '' ELSE coalesce(v_plan.review_reason,'') END);
      END $read$;
      REVOKE ALL ON FUNCTION club.read_mobile_match_workspace(uuid,text,text,timestamptz,text) FROM PUBLIC;

      CREATE FUNCTION club.write_mobile_match_workspace(_actor uuid,_phone text,_role text,
        _expires timestamptz,_match text,_action text,_expected integer,_squad jsonb,_statistics jsonb,
        _rotation_note text,_review_reason text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $write$
      DECLARE v_owner text;v_host text;v_day date;v_completed boolean;
        v_plan club.match_workspaces%ROWTYPE;v_existing boolean;v_state text;v_version integer;
        v_squad jsonb;v_stats jsonb;v_rotation text;v_reason text;v_read jsonb;
      BEGIN
        IF _action IS NULL OR _action NOT IN ('save_squad','select_squad','submit_statistics','approve','reject')
          OR _expected IS NULL OR _expected NOT BETWEEN 0 AND 999999
          OR coalesce(length(_rotation_note),0)>500 OR coalesce(length(_review_reason),0)>500
          OR coalesce(_rotation_note,'') ~ '[[:cntrl:]]' OR coalesce(_review_reason,'') ~ '[[:cntrl:]]'
          THEN RAISE EXCEPTION 'match_input_invalid' USING ERRCODE='22023'; END IF;
        IF (_action IN ('save_squad','select_squad','submit_statistics') AND _role<>'coach')
          OR (_action IN ('approve','reject') AND _role<>'management')
          THEN RAISE EXCEPTION 'match_action_denied' USING ERRCODE='42501'; END IF;
        -- Serialize writers for the same match including the first insert.
        PERFORM 1 FROM club.calendar_items WHERE kind='match' AND id=_match FOR UPDATE;
        SELECT owner,host,day,completed INTO STRICT v_owner,v_host,v_day,v_completed
          FROM club.require_mobile_match_host(_actor,_phone,_role,_expires,_match);
        SELECT * INTO v_plan FROM club.match_workspaces WHERE kind='match' AND match_id=_match FOR UPDATE;
        v_existing:=FOUND;
        IF (v_existing AND (v_plan.organization_id<>v_owner OR v_plan.host_id<>v_host))
          THEN RAISE EXCEPTION 'match_host_changed' USING ERRCODE='42501'; END IF;
        IF coalesce(v_plan.version,0)<>_expected THEN RAISE EXCEPTION 'match_version_conflict' USING ERRCODE='40001'; END IF;
        v_state:=coalesce(v_plan.state,'draft');v_squad:=coalesce(v_plan.squad,'[]'::jsonb);
        v_stats:=coalesce(v_plan.statistics,'[]'::jsonb);v_rotation:=coalesce(v_plan.rotation_note,'');
        v_reason:=coalesce(v_plan.review_reason,'');
        IF _action IN ('save_squad','select_squad') THEN
          IF v_state NOT IN ('draft','selected') OR v_completed
            THEN RAISE EXCEPTION 'match_squad_locked' USING ERRCODE='40001'; END IF;
          IF _squad IS NULL OR jsonb_typeof(_squad)<>'array' OR jsonb_array_length(_squad)>60
            OR _statistics IS NOT NULL OR _review_reason IS NOT NULL
            THEN RAISE EXCEPTION 'match_squad_invalid' USING ERRCODE='22023'; END IF;
          IF EXISTS(SELECT 1 FROM jsonb_array_elements(_squad) s(value)
            WHERE jsonb_typeof(s.value)<>'object'
              OR NOT (s.value ?& ARRAY['player_id','assignment','position'])
              OR (s.value-ARRAY['player_id','assignment','position'])<>'{}'::jsonb
              OR jsonb_typeof(s.value->'player_id')<>'string'
              OR coalesce(s.value->>'assignment','') NOT IN ('starter','reserve')
              OR (s.value->'position'<>'null'::jsonb AND
                (jsonb_typeof(s.value->'position')<>'string' OR length(s.value->>'position') NOT BETWEEN 1 AND 40
                  OR s.value->>'position' ~ '[[:cntrl:]]'))
              OR (v_owner='lefteria_fc_academy' AND s.value->'position'<>'null'::jsonb)
              OR NOT EXISTS(SELECT 1 FROM club.match_workspace_roster(v_owner,v_host,_match) p
                WHERE p.player_id=s.value->>'player_id'))
            OR (SELECT count(DISTINCT value->>'player_id') FROM jsonb_array_elements(_squad))<>jsonb_array_length(_squad)
            OR (SELECT count(*) FROM jsonb_array_elements(_squad) WHERE value->>'assignment'='starter')>11
            THEN RAISE EXCEPTION 'match_squad_invalid' USING ERRCODE='22023'; END IF;
          IF _action='select_squad' AND (
            NOT EXISTS(SELECT 1 FROM jsonb_array_elements(_squad) WHERE value->>'assignment'='starter')
            OR (v_owner='lefteria_fc' AND (SELECT count(*) FROM jsonb_array_elements(_squad)
              WHERE value->>'assignment'='starter')<>11)
            OR (v_owner='lefteria_fc' AND EXISTS(SELECT 1 FROM jsonb_array_elements(_squad)
              WHERE value->>'assignment'='starter' AND value->>'position' IS NULL)))
            THEN RAISE EXCEPTION 'match_starting_group_invalid' USING ERRCODE='22023'; END IF;
          -- Rotation is based on the previous confirmed game for this exact
          -- host. Do not penalize unavailable children or force a fixed position.
          IF _action='select_squad' AND v_owner='lefteria_fc_academy' AND btrim(coalesce(_rotation_note,''))='' AND EXISTS(
            WITH previous AS (
              SELECT w.squad FROM club.match_workspaces w JOIN club.calendar_items c ON c.kind=w.kind AND c.id=w.match_id
              WHERE w.organization_id=v_owner AND w.host_id=v_host AND w.state<>'draft'
                AND c.staff_day<v_day AND NOT c.is_cancelled AND NOT c.is_archived
              ORDER BY c.staff_day DESC,c.id DESC LIMIT 1
            )
            SELECT 1 FROM previous p CROSS JOIN LATERAL jsonb_array_elements(p.squad) old
            JOIN LATERAL jsonb_array_elements(_squad) current ON current->>'player_id'=old->>'player_id'
            WHERE old->>'assignment'='reserve' AND current->>'assignment'='reserve'
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(p.squad) last
                JOIN LATERAL jsonb_array_elements(_squad) now ON now->>'player_id'=last->>'player_id'
                WHERE last->>'assignment'='starter' AND now->>'assignment'='starter'))
            THEN RAISE EXCEPTION 'match_rotation_note_required' USING ERRCODE='22023'; END IF;
          v_squad:=_squad;v_rotation:=CASE WHEN v_owner='lefteria_fc_academy' THEN btrim(coalesce(_rotation_note,'')) ELSE '' END;
          v_state:=CASE WHEN _action='select_squad' THEN 'selected' ELSE 'draft' END;
        ELSIF _action='submit_statistics' THEN
          IF NOT v_existing OR v_state NOT IN ('selected','rejected') OR NOT v_completed
            THEN RAISE EXCEPTION 'match_statistics_locked' USING ERRCODE='40001'; END IF;
          IF _squad IS NOT NULL OR _rotation_note IS NOT NULL OR _review_reason IS NOT NULL
            OR _statistics IS NULL OR jsonb_typeof(_statistics)<>'array' OR jsonb_array_length(_statistics)<>jsonb_array_length(v_squad)
            THEN RAISE EXCEPTION 'match_statistics_invalid' USING ERRCODE='22023'; END IF;
          IF EXISTS(SELECT 1 FROM jsonb_array_elements(_statistics) s(value)
            WHERE jsonb_typeof(s.value)<>'object'
              OR NOT (s.value ?& ARRAY['player_id','minutes_played','goals','assists'])
              OR (s.value-ARRAY['player_id','minutes_played','goals','assists'])<>'{}'::jsonb
              OR jsonb_typeof(s.value->'player_id')<>'string'
              OR NOT EXISTS(SELECT 1 FROM jsonb_array_elements(v_squad) p WHERE p->>'player_id'=s.value->>'player_id')
              OR jsonb_typeof(s.value->'minutes_played')<>'number' OR coalesce(s.value->>'minutes_played','') !~ '^[0-9]{1,3}$'
              OR jsonb_typeof(s.value->'goals')<>'number' OR coalesce(s.value->>'goals','') !~ '^[0-9]{1,2}$'
              OR jsonb_typeof(s.value->'assists')<>'number' OR coalesce(s.value->>'assists','') !~ '^[0-9]{1,2}$')
            OR (SELECT count(DISTINCT value->>'player_id') FROM jsonb_array_elements(_statistics))<>jsonb_array_length(_statistics)
            THEN RAISE EXCEPTION 'match_statistics_invalid' USING ERRCODE='22023'; END IF;
          IF EXISTS(SELECT 1 FROM jsonb_array_elements(_statistics) s(value)
            WHERE (s.value->>'minutes_played')::integer>180 OR (s.value->>'goals')::integer>50
              OR (s.value->>'assists')::integer>50 OR ((s.value->>'minutes_played')::integer=0 AND
                ((s.value->>'goals')::integer>0 OR (s.value->>'assists')::integer>0)))
            THEN RAISE EXCEPTION 'match_statistics_invalid' USING ERRCODE='22023'; END IF;
          v_stats:=_statistics;v_state:='submitted';v_reason:='';
        ELSE
          IF NOT v_existing OR v_state<>'submitted' OR NOT v_completed
            THEN RAISE EXCEPTION 'match_review_conflict' USING ERRCODE='40001'; END IF;
          IF _squad IS NOT NULL OR _statistics IS NOT NULL OR _rotation_note IS NOT NULL
            OR (_action='reject' AND btrim(coalesce(_review_reason,''))='')
            THEN RAISE EXCEPTION 'match_review_invalid' USING ERRCODE='22023'; END IF;
          v_state:=CASE WHEN _action='approve' THEN 'approved' ELSE 'rejected' END;
          v_reason:=btrim(coalesce(_review_reason,''));
        END IF;
        -- Never approve or submit stale roster identities. Read-only callers
        -- see no stale personal fields; a draft/selection can be repaired.
        IF EXISTS(SELECT 1 FROM jsonb_array_elements(v_squad) s(value)
          WHERE NOT EXISTS(SELECT 1 FROM club.match_workspace_roster(v_owner,v_host,_match) p
            WHERE p.player_id=s.value->>'player_id'))
          THEN RAISE EXCEPTION 'match_roster_changed' USING ERRCODE='42501'; END IF;
        PERFORM * FROM club.require_mobile_match_host(_actor,_phone,_role,_expires,_match);
        v_version:=_expected+1;
        INSERT INTO club.match_workspaces(organization_id,match_id,host_id,version,state,squad,statistics,rotation_note,review_reason,updated_by)
          VALUES(v_owner,_match,v_host,v_version,v_state,v_squad,v_stats,v_rotation,v_reason,_actor)
        ON CONFLICT(kind,match_id) DO UPDATE SET version=EXCLUDED.version,state=EXCLUDED.state,
          squad=EXCLUDED.squad,statistics=EXCLUDED.statistics,rotation_note=EXCLUDED.rotation_note,
          review_reason=EXCLUDED.review_reason,updated_by=EXCLUDED.updated_by,updated_at=clock_timestamp();
        INSERT INTO club.match_workspace_revisions(match_id,version,actor_id,action,snapshot)
          VALUES(_match,v_version,_actor,_action,jsonb_build_object('state',v_state,'squad',v_squad,
            'statistics',v_stats,'rotation_note',v_rotation,'review_reason',v_reason));
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_actor,'mobile.match.'||_action,'succeeded',v_owner,'scoped_match_workspace');
        v_read:=club.read_mobile_match_workspace(_actor,_phone,_role,_expires,_match);
        RETURN v_read;
      END $write$;
      REVOKE ALL ON FUNCTION club.write_mobile_match_workspace(uuid,text,text,timestamptz,text,text,integer,jsonb,jsonb,text,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.write_mobile_match_workspace(uuid,text,text,timestamptz,text,text,integer,jsonb,jsonb,text,text);
      DROP FUNCTION club.read_mobile_match_workspace(uuid,text,text,timestamptz,text);
      DROP FUNCTION club.match_workspace_roster(text,text,text);
      DROP FUNCTION club.require_mobile_match_host(uuid,text,text,timestamptz,text);
      DROP TABLE club.match_workspace_revisions;
      DROP TABLE club.match_workspaces;
      """);
}
