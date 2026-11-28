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
        rotation_interval_minutes integer,
        version integer NOT NULL CHECK(version BETWEEN 1 AND 1000000),
        updated_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(organization_id,host_id),
        FOREIGN KEY(organization_id,team_id) REFERENCES club.teams(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,group_id) REFERENCES club.academy_groups(organization_id,id) ON DELETE RESTRICT,
        CHECK((organization_id='lefteria_fc' AND rotation_interval_minutes IS NULL)
          OR (organization_id='lefteria_fc_academy' AND rotation_interval_minutes IS NOT NULL
              AND rotation_interval_minutes BETWEEN 1 AND duration_minutes))
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
          'duration_minutes',v_config.duration_minutes,'rotation_interval_minutes',v_config.rotation_interval_minutes);
      END $read$;
      REVOKE ALL ON FUNCTION club.read_admin_group_match_timing(text,text,text) FROM PUBLIC;

      CREATE FUNCTION club.write_admin_group_match_timing(_digest text,_owner text,_host text,
        _expected integer,_duration integer,_interval integer)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $write$
      DECLARE v_actor uuid;
      BEGIN
        v_actor:=club.require_admin_match_timing_host(_digest,_owner,_host);
        IF _expected IS NULL OR _expected NOT BETWEEN 0 AND 999999 OR _duration IS NULL OR _duration NOT BETWEEN 1 AND 180
          OR (_owner='lefteria_fc' AND _interval IS NOT NULL)
          OR (_owner='lefteria_fc_academy' AND (_interval IS NULL OR _interval NOT BETWEEN 1 AND _duration))
          THEN RAISE EXCEPTION 'match_timing_invalid' USING ERRCODE='22023'; END IF;
        IF _expected=0 THEN
          INSERT INTO club.group_match_timing(organization_id,host_id,duration_minutes,rotation_interval_minutes,version,updated_by)
            VALUES(_owner,_host,_duration,_interval,1,v_actor) ON CONFLICT DO NOTHING;
        ELSE
          UPDATE club.group_match_timing SET duration_minutes=_duration,rotation_interval_minutes=_interval,
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
        ADD COLUMN rotation_interval_minutes integer,
        ADD CONSTRAINT ck_match_timing_snapshot CHECK(
          (timing_version IS NULL AND duration_minutes IS NULL AND rotation_interval_minutes IS NULL)
          OR (timing_frozen AND timing_version IS NOT NULL AND timing_version BETWEEN 1 AND 1000000
            AND duration_minutes IS NOT NULL AND duration_minutes BETWEEN 1 AND 180 AND
            ((organization_id='lefteria_fc' AND rotation_interval_minutes IS NULL)
             OR (organization_id='lefteria_fc_academy' AND rotation_interval_minutes IS NOT NULL
               AND rotation_interval_minutes BETWEEN 1 AND duration_minutes))));
      -- Existing confirmed fixtures have no known timing. Never infer a historical
      -- value from later settings, and never revise an already frozen snapshot.
      UPDATE club.match_workspaces SET timing_frozen=true WHERE state<>'draft';
      CREATE FUNCTION club.freeze_match_timing()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $freeze$
      DECLARE v_config club.group_match_timing%ROWTYPE;
      BEGIN
        IF TG_OP='UPDATE' THEN
          IF OLD.timing_frozen THEN
            NEW.timing_frozen:=true;NEW.timing_version:=OLD.timing_version;
            NEW.duration_minutes:=OLD.duration_minutes;NEW.rotation_interval_minutes:=OLD.rotation_interval_minutes;
            RETURN NEW;
          END IF;
        END IF;
        NEW.timing_frozen:=false;NEW.timing_version:=NULL;NEW.duration_minutes:=NULL;NEW.rotation_interval_minutes:=NULL;
        IF NEW.state='selected' THEN
          SELECT * INTO v_config FROM club.group_match_timing
            WHERE organization_id=NEW.organization_id AND host_id=NEW.host_id FOR SHARE;
          NEW.timing_frozen:=true;NEW.timing_version:=v_config.version;
          NEW.duration_minutes:=v_config.duration_minutes;NEW.rotation_interval_minutes:=v_config.rotation_interval_minutes;
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
            'duration_minutes',v_plan.duration_minutes,'rotation_interval_minutes',v_plan.rotation_interval_minutes);
        END IF;
        SELECT * INTO v_config FROM club.group_match_timing WHERE organization_id=v_owner AND host_id=v_host;
        RETURN jsonb_build_object('frozen',false,'settings_version',v_config.version,
          'duration_minutes',v_config.duration_minutes,'rotation_interval_minutes',v_config.rotation_interval_minutes);
      END $timing$;
      REVOKE ALL ON FUNCTION club.read_mobile_match_timing(uuid,text,text,timestamptz,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.read_mobile_match_timing(uuid,text,text,timestamptz,text);
      DROP TRIGGER freeze_match_timing ON club.match_workspaces;
      DROP FUNCTION club.freeze_match_timing();
      ALTER TABLE club.match_workspaces DROP CONSTRAINT ck_match_timing_snapshot,
        DROP COLUMN timing_frozen,DROP COLUMN timing_version,DROP COLUMN duration_minutes,DROP COLUMN rotation_interval_minutes;
      DROP FUNCTION club.write_admin_group_match_timing(text,text,text,integer,integer,integer);
      DROP FUNCTION club.read_admin_group_match_timing(text,text,text);
      DROP FUNCTION club.require_admin_match_timing_host(text,text,text);
      DROP TABLE club.group_match_timing;
      """);
}
