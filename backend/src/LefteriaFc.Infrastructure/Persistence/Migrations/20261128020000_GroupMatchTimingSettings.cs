using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128020000_GroupMatchTimingSettings")]
public sealed class GroupMatchTimingSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.group_match_timing (
        organization_id varchar(32) NOT NULL CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        host_id varchar(128) COLLATE "C" NOT NULL,
        team_id varchar(128) COLLATE "C" GENERATED ALWAYS AS
          (CASE WHEN organization_id='lefteria_fc' THEN host_id ELSE NULL END) STORED,
        group_id varchar(128) COLLATE "C" GENERATED ALWAYS AS
          (CASE WHEN organization_id='lefteria_fc_academy' THEN host_id ELSE NULL END) STORED,
        duration_minutes integer NOT NULL CHECK(duration_minutes BETWEEN 1 AND 180),
        players_on_field integer NOT NULL CHECK(players_on_field IN (5,7,9,11)),
        version integer NOT NULL CHECK(version BETWEEN 1 AND 1000000),
        updated_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(organization_id,host_id),
        FOREIGN KEY(organization_id,team_id) REFERENCES club.teams(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,group_id) REFERENCES club.academy_groups(organization_id,id) ON DELETE RESTRICT,
        CHECK(organization_id='lefteria_fc_academy' OR players_on_field=11)
      );
      ALTER TABLE club.group_match_timing ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON club.group_match_timing FROM PUBLIC;

      CREATE FUNCTION club.require_admin_match_timing_host(_digest text,_owner text,_host text)
      RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $guard$
      DECLARE v_actor uuid;v_role text;v_scope uuid;
      BEGIN
        IF _host IS NULL OR length(_host) NOT BETWEEN 1 AND 128 OR _host<>btrim(_host)
          OR _host ~ '[[:cntrl:]]' THEN RAISE EXCEPTION 'match_timing_invalid' USING ERRCODE='22023'; END IF;
        SELECT actor_id,account_role INTO STRICT v_actor,v_role FROM club.authorize_admin_football(_digest,_owner);
        IF _owner='lefteria_fc_academy' THEN
          PERFORM club.require_training_drill_group(v_actor,_host);
        ELSIF _owner='lefteria_fc' THEN
          SELECT id INTO v_scope FROM club.football_operational_scopes WHERE organization_id=_owner AND team_id=_host FOR SHARE;
          IF NOT coalesce(club.admin_football_allowed(v_actor,v_role,_owner,v_scope,NULL,true),false)
            THEN RAISE EXCEPTION 'match_timing_denied' USING ERRCODE='42501'; END IF;
        ELSE RAISE EXCEPTION 'match_timing_denied' USING ERRCODE='42501'; END IF;
        RETURN v_actor;
      END $guard$;
      REVOKE ALL ON FUNCTION club.require_admin_match_timing_host(text,text,text) FROM PUBLIC;

      CREATE FUNCTION club.read_admin_group_match_timing(_digest text,_owner text,_host text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_config club.group_match_timing%ROWTYPE;
      BEGIN
        PERFORM club.require_admin_match_timing_host(_digest,_owner,_host);
        SELECT * INTO v_config FROM club.group_match_timing WHERE organization_id=_owner AND host_id=_host;
        RETURN jsonb_build_object('organization_id',_owner,'host_id',_host,'version',coalesce(v_config.version,0),
          'duration_minutes',v_config.duration_minutes,'players_on_field',v_config.players_on_field,'attendance_ids',NULL);
      END $read$;
      REVOKE ALL ON FUNCTION club.read_admin_group_match_timing(text,text,text) FROM PUBLIC;

      CREATE FUNCTION club.write_admin_group_match_timing(_digest text,_owner text,_host text,
        _expected integer,_duration integer,_format integer)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $write$
      DECLARE v_actor uuid;
      BEGIN
        v_actor:=club.require_admin_match_timing_host(_digest,_owner,_host);
        IF _expected IS NULL OR _expected NOT BETWEEN 0 AND 999999 OR _duration IS NULL OR _duration NOT BETWEEN 1 AND 180
          OR _format IS NULL OR _format NOT IN (5,7,9,11) OR (_owner='lefteria_fc' AND _format<>11)
          THEN RAISE EXCEPTION 'match_timing_invalid' USING ERRCODE='22023'; END IF;
        IF _expected=0 THEN
          INSERT INTO club.group_match_timing(organization_id,host_id,duration_minutes,players_on_field,version,updated_by)
            VALUES(_owner,_host,_duration,_format,1,v_actor) ON CONFLICT DO NOTHING;
        ELSE
          UPDATE club.group_match_timing SET duration_minutes=_duration,players_on_field=_format,
            version=version+1,updated_by=v_actor,updated_at=clock_timestamp()
            WHERE organization_id=_owner AND host_id=_host AND version=_expected;
        END IF;
        IF NOT FOUND THEN RAISE EXCEPTION 'match_timing_conflict' USING ERRCODE='23505'; END IF;
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,'admin.match.timing.updated','succeeded',_owner,'group_match_timing');
        RETURN club.read_admin_group_match_timing(_digest,_owner,_host);
      END $write$;
      REVOKE ALL ON FUNCTION club.write_admin_group_match_timing(text,text,text,integer,integer,integer) FROM PUBLIC;

      ALTER TABLE club.match_workspaces ADD COLUMN timing_frozen boolean NOT NULL DEFAULT false,
        ADD COLUMN timing_version integer,
        ADD COLUMN duration_minutes integer,
        ADD COLUMN players_on_field integer,
        ADD COLUMN attendance_ids jsonb CHECK(attendance_ids IS NULL OR (jsonb_typeof(attendance_ids)='array' AND jsonb_array_length(attendance_ids)<=60)),
        ADD CONSTRAINT ck_match_timing_snapshot CHECK(
          (timing_version IS NULL AND duration_minutes IS NULL AND players_on_field IS NULL)
          OR (timing_frozen AND timing_version IS NOT NULL AND timing_version BETWEEN 1 AND 1000000
            AND duration_minutes IS NOT NULL AND duration_minutes BETWEEN 1 AND 180 AND
            players_on_field IS NOT NULL AND players_on_field IN (5,7,9,11)
            AND (organization_id='lefteria_fc_academy' OR players_on_field=11)));
      -- Existing confirmed fixtures have no known timing. Never infer a historical
      -- value from later settings, and never revise an already frozen snapshot.
      UPDATE club.match_workspaces w SET timing_frozen=true FROM club.calendar_items c
        WHERE c.kind=w.kind AND c.id=w.match_id AND (c.status='completed' OR w.state IN ('submitted','approved','rejected'));
      CREATE FUNCTION club.freeze_match_timing()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $freeze$
      DECLARE v_config club.group_match_timing%ROWTYPE;
      BEGIN
        IF TG_OP='UPDATE' THEN
          IF OLD.attendance_ids IS NOT NULL AND (NEW.squad IS DISTINCT FROM OLD.squad OR NEW.state='draft') THEN
            RAISE EXCEPTION 'match_attendance_locked' USING ERRCODE='40001';
          END IF;
          IF OLD.timing_frozen THEN
            NEW.timing_frozen:=true;NEW.timing_version:=OLD.timing_version;
            NEW.duration_minutes:=OLD.duration_minutes;NEW.players_on_field:=OLD.players_on_field;
            RETURN NEW;
          END IF;
        END IF;
        NEW.timing_frozen:=false;NEW.timing_version:=NULL;NEW.duration_minutes:=NULL;NEW.players_on_field:=NULL;
        IF NEW.state='selected' AND (NEW.organization_id='lefteria_fc' OR NEW.attendance_ids IS NOT NULL) THEN
          SELECT * INTO v_config FROM club.group_match_timing
            WHERE organization_id=NEW.organization_id AND host_id=NEW.host_id FOR SHARE;
          IF NEW.attendance_ids IS NOT NULL AND v_config.version IS NULL THEN
            RAISE EXCEPTION 'match_timing_not_configured' USING ERRCODE='22023'; END IF;
          NEW.timing_frozen:=true;NEW.timing_version:=v_config.version;
          NEW.duration_minutes:=v_config.duration_minutes;NEW.players_on_field:=v_config.players_on_field;
        END IF;
        RETURN NEW;
      END $freeze$;
      REVOKE ALL ON FUNCTION club.freeze_match_timing() FROM PUBLIC;
      CREATE TRIGGER freeze_match_timing BEFORE INSERT OR UPDATE ON club.match_workspaces
        FOR EACH ROW EXECUTE FUNCTION club.freeze_match_timing();

      CREATE FUNCTION club.read_mobile_match_timing(_actor uuid,_phone text,_role text,
        _expires timestamptz,_match text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $timing$
      DECLARE v_owner text;v_host text;v_completed boolean;v_plan club.match_workspaces%ROWTYPE;v_config club.group_match_timing%ROWTYPE;
      BEGIN
        SELECT owner,host,completed INTO STRICT v_owner,v_host,v_completed
          FROM club.require_mobile_match_host(_actor,_phone,_role,_expires,_match);
        SELECT * INTO v_plan FROM club.match_workspaces WHERE kind='match' AND match_id=_match;
        IF FOUND AND (v_plan.organization_id<>v_owner OR v_plan.host_id<>v_host)
          THEN RAISE EXCEPTION 'match_host_changed' USING ERRCODE='42501'; END IF;
        IF coalesce(v_plan.timing_frozen,false) OR v_completed THEN
          RETURN jsonb_build_object('frozen',true,'settings_version',v_plan.timing_version,
            'duration_minutes',v_plan.duration_minutes,'players_on_field',v_plan.players_on_field,'attendance_ids',v_plan.attendance_ids);
        END IF;
        SELECT * INTO v_config FROM club.group_match_timing WHERE organization_id=v_owner AND host_id=v_host;
        RETURN jsonb_build_object('frozen',false,'settings_version',v_config.version,
          'duration_minutes',v_config.duration_minutes,'players_on_field',v_config.players_on_field);
      END $timing$;
      REVOKE ALL ON FUNCTION club.read_mobile_match_timing(uuid,text,text,timestamptz,text) FROM PUBLIC;

      ALTER TABLE club.match_workspace_revisions DROP CONSTRAINT match_workspace_revisions_action_check;
      ALTER TABLE club.match_workspace_revisions ADD CONSTRAINT match_workspace_revisions_action_check
        CHECK(action IN ('save_squad','select_squad','submit_statistics','approve','reject','confirm_attendance'));
      CREATE FUNCTION club.confirm_mobile_match_attendance(_actor uuid,_phone text,_role text,
        _expires timestamptz,_match text,_expected integer,_attendance jsonb)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $present$
      DECLARE v_owner text;v_host text;v_day date;v_completed boolean;
        v_plan club.match_workspaces%ROWTYPE;v_config club.group_match_timing%ROWTYPE;
      BEGIN
        IF _role IS DISTINCT FROM 'coach' THEN RAISE EXCEPTION 'match_attendance_denied' USING ERRCODE='42501'; END IF;
        IF _expected IS NULL OR _expected NOT BETWEEN 1 AND 999999 OR _attendance IS NULL
          OR jsonb_typeof(_attendance)<>'array' OR jsonb_array_length(_attendance) NOT BETWEEN 1 AND 60
          THEN RAISE EXCEPTION 'match_attendance_invalid' USING ERRCODE='22023'; END IF;
        PERFORM * FROM club.require_mobile_match_host(_actor,_phone,_role,_expires,_match);
        PERFORM 1 FROM club.calendar_items WHERE kind='match' AND id=_match FOR UPDATE;
        SELECT owner,host,day,completed INTO STRICT v_owner,v_host,v_day,v_completed
          FROM club.require_mobile_match_host(_actor,_phone,_role,_expires,_match);
        IF v_owner<>'lefteria_fc_academy' OR v_completed OR v_day<>(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
          THEN RAISE EXCEPTION 'match_attendance_day_denied' USING ERRCODE='42501'; END IF;
        SELECT * INTO v_plan FROM club.match_workspaces WHERE kind='match' AND match_id=_match FOR UPDATE;
        IF NOT FOUND OR v_plan.organization_id<>v_owner OR v_plan.host_id<>v_host
          OR v_plan.state<>'selected' OR v_plan.version<>_expected
          THEN RAISE EXCEPTION 'match_attendance_conflict' USING ERRCODE='40001'; END IF;
        IF EXISTS(SELECT 1 FROM jsonb_array_elements(_attendance) a(value)
            WHERE jsonb_typeof(a.value)<>'string' OR NOT EXISTS(
              SELECT 1 FROM jsonb_array_elements(v_plan.squad) s(value) WHERE s.value->>'player_id'=a.value#>>'{}')
              OR NOT EXISTS(SELECT 1 FROM club.match_workspace_roster(v_owner,v_host,_match) r WHERE r.player_id=a.value#>>'{}'))
          OR (SELECT count(DISTINCT value) FROM jsonb_array_elements(_attendance))<>jsonb_array_length(_attendance)
          THEN RAISE EXCEPTION 'match_attendance_invalid' USING ERRCODE='22023'; END IF;
        IF NOT v_plan.timing_frozen THEN
          SELECT * INTO v_config FROM club.group_match_timing WHERE organization_id=v_owner AND host_id=v_host FOR SHARE;
          IF NOT FOUND THEN RAISE EXCEPTION 'match_timing_not_configured' USING ERRCODE='22023'; END IF;
        ELSIF v_plan.timing_version IS NULL THEN
          RAISE EXCEPTION 'match_timing_not_configured' USING ERRCODE='22023';
        END IF;
        UPDATE club.match_workspaces SET attendance_ids=_attendance,version=version+1,updated_by=_actor,updated_at=clock_timestamp()
          WHERE kind='match' AND match_id=_match;
        INSERT INTO club.match_workspace_revisions(match_id,version,actor_id,action,snapshot)
          VALUES(_match,_expected+1,_actor,'confirm_attendance',jsonb_build_object('state',v_plan.state,
            'attendance_ids',_attendance,'squad',v_plan.squad));
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_actor,'mobile.match.confirm_attendance','succeeded',v_owner,'match_day_attendance');
        RETURN club.read_mobile_match_workspace(_actor,_phone,_role,_expires,_match);
      END $present$;
      REVOKE ALL ON FUNCTION club.confirm_mobile_match_attendance(uuid,text,text,timestamptz,text,integer,jsonb) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.confirm_mobile_match_attendance(uuid,text,text,timestamptz,text,integer,jsonb);
      ALTER TABLE club.match_workspace_revisions DROP CONSTRAINT match_workspace_revisions_action_check;
      ALTER TABLE club.match_workspace_revisions ADD CONSTRAINT match_workspace_revisions_action_check
        CHECK(action IN ('save_squad','select_squad','submit_statistics','approve','reject'));
      DROP FUNCTION club.read_mobile_match_timing(uuid,text,text,timestamptz,text);
      DROP TRIGGER freeze_match_timing ON club.match_workspaces;
      DROP FUNCTION club.freeze_match_timing();
      ALTER TABLE club.match_workspaces DROP CONSTRAINT ck_match_timing_snapshot,
        DROP COLUMN timing_frozen,DROP COLUMN timing_version,DROP COLUMN duration_minutes,DROP COLUMN players_on_field,DROP COLUMN attendance_ids;
      DROP FUNCTION club.write_admin_group_match_timing(text,text,text,integer,integer,integer);
      DROP FUNCTION club.read_admin_group_match_timing(text,text,text);
      DROP FUNCTION club.require_admin_match_timing_host(text,text,text);
      DROP TABLE club.group_match_timing;
      """);
}
