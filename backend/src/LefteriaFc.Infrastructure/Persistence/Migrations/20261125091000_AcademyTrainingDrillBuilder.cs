using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125091000_AcademyTrainingDrillBuilder")]
public sealed class AcademyTrainingDrillBuilder : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE club.academy_training_plan_drafts
        ADD COLUMN drill_snapshots jsonb NOT NULL DEFAULT '[]'::jsonb
        CHECK(jsonb_typeof(drill_snapshots)='array' AND jsonb_array_length(drill_snapshots)<=24);

      -- Private content is embedded in the exact plan revision, not linked live
      -- to a mutable catalogue. Existing plan writes preserve the snapshots.
      CREATE FUNCTION club.require_training_drill_group(_actor uuid,_group text)
      RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $scope$
      DECLARE v_role text;v_scope uuid;v_location uuid;
      BEGIN
        SELECT role INTO v_role FROM identity.admin_accounts
          WHERE id=_actor AND is_active AND NOT invite_pending FOR SHARE;
        IF v_role IS NULL OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'training_drill_group_denied' USING ERRCODE='42501';
        END IF;
        SELECT a.scope_id,a.location_id INTO v_scope,v_location
        FROM club.academy_group_location_assignments a
        JOIN club.football_operational_scopes s ON s.id=a.scope_id AND s.organization_id=a.organization_id AND s.academy_city_id=a.city_id
        JOIN club.football_cities c ON c.id=a.city_id AND c.is_active
        JOIN club.football_locations l ON l.id=a.location_id AND l.city_id=a.city_id AND l.is_active
        WHERE a.organization_id='lefteria_fc_academy' AND a.group_id=_group
        FOR SHARE OF a,s,c,l;
        IF v_scope IS NULL OR v_location IS NULL OR
          NOT club.admin_football_allowed(_actor,v_role,'lefteria_fc_academy',v_scope,v_location,true)
        THEN RAISE EXCEPTION 'training_drill_group_denied' USING ERRCODE='42501'; END IF;
      END $scope$;

      CREATE FUNCTION club.guard_training_drill_plan()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $guard$
      DECLARE v_total integer;
      BEGIN
        IF jsonb_array_length(NEW.drill_snapshots)>0 OR
          (TG_OP='UPDATE' AND jsonb_array_length(OLD.drill_snapshots)>0) THEN
          PERFORM club.require_training_drill_group(NEW.updated_by,NEW.group_id);
          SELECT coalesce(sum((item->>'duration_minutes')::integer),0)::integer INTO v_total
            FROM jsonb_array_elements(NEW.drill_snapshots) item;
          IF v_total>NEW.duration_minutes THEN
            RAISE EXCEPTION 'training_drills_exceed_duration' USING ERRCODE='22023';
          END IF;
        END IF;
        RETURN NEW;
      END $guard$;
      CREATE TRIGGER academy_training_drill_plan_guard BEFORE INSERT OR UPDATE
        ON club.academy_training_plan_drafts FOR EACH ROW EXECUTE FUNCTION club.guard_training_drill_plan();

      -- Preserve attachments in the pre-existing immutable per-version event,
      -- including revisions created by ordinary schedule/title changes.
      CREATE FUNCTION club.snapshot_training_drill_event()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $snapshot$
      DECLARE v_drills jsonb;
      BEGIN
        SELECT drill_snapshots INTO STRICT v_drills FROM club.academy_training_plan_drafts
          WHERE id=NEW.plan_id AND version=NEW.version;
        NEW.snapshot:=NEW.snapshot||jsonb_build_object('drill_snapshots',v_drills);
        RETURN NEW;
      END $snapshot$;
      CREATE TRIGGER academy_training_drill_event_snapshot BEFORE INSERT
        ON club.academy_training_plan_events FOR EACH ROW EXECUTE FUNCTION club.snapshot_training_drill_event();

      -- Until the dedicated complete-plan review and member projection ship,
      -- never let the old schedule-only approval publish unreviewed exercises.
      CREATE FUNCTION club.require_training_drill_review_rollout()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $approval$
      BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_drafts
          WHERE id=NEW.plan_id AND version=NEW.plan_version AND jsonb_array_length(drill_snapshots)>0)
        THEN RAISE EXCEPTION 'training_drill_review_not_enabled' USING ERRCODE='55000'; END IF;
        RETURN NEW;
      END $approval$;
      CREATE TRIGGER academy_training_drill_review_gate BEFORE INSERT
        ON club.academy_training_plan_approvals FOR EACH ROW EXECUTE FUNCTION club.require_training_drill_review_rollout();

      CREATE FUNCTION club.training_drill_plan_view(p club.academy_training_plan_drafts)
      RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,pg_temp AS $view$
        SELECT jsonb_build_object('id',p.id,'organization_id',p.organization_id,'group_id',p.group_id,
          'version',p.version,'status',p.status,'duration_minutes',p.duration_minutes,'drills',p.drill_snapshots)
      $view$;

      CREATE FUNCTION club.read_admin_training_drills(_digest text,_group text,_plan uuid,_expected integer)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_actor uuid;v_plan club.academy_training_plan_drafts%ROWTYPE;
      BEGIN
        SELECT actor_id INTO v_actor FROM club.require_academy_training_planner(_digest,'lefteria_fc_academy');
        PERFORM club.require_training_drill_group(v_actor,_group);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id='lefteria_fc_academy' AND group_id=_group AND version=_expected FOR SHARE;
        IF NOT FOUND THEN RAISE EXCEPTION 'training_drill_plan_conflict' USING ERRCODE='23505'; END IF;
        RETURN club.training_drill_plan_view(v_plan);
      END $read$;

      CREATE FUNCTION club.write_admin_training_drills(_digest text,_group text,_plan uuid,_expected integer,_items jsonb)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $write$
      DECLARE v_actor uuid;v_session uuid;v_plan club.academy_training_plan_drafts%ROWTYPE;
        v_item jsonb;v_snapshot jsonb;v_result jsonb:='[]'::jsonb;v_ids uuid[]:=ARRAY[]::uuid[];
        v_id uuid;v_version integer;v_minutes integer;v_total integer:=0;
        v_drill club.academy_coaching_drills%ROWTYPE;
      BEGIN
        SELECT actor_id,session_id INTO v_actor,v_session FROM club.require_academy_training_planner(_digest,'lefteria_fc_academy');
        PERFORM club.require_training_drill_group(v_actor,_group);
        IF _plan IS NULL OR _expected IS NULL OR _expected NOT BETWEEN 1 AND 9999 OR _items IS NULL
          OR jsonb_typeof(_items)<>'array' THEN RAISE EXCEPTION 'training_drill_input_invalid' USING ERRCODE='22023'; END IF;
        IF jsonb_array_length(_items)>24 THEN RAISE EXCEPTION 'training_drill_limit' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id='lefteria_fc_academy' AND group_id=_group FOR UPDATE;
        IF NOT FOUND OR v_plan.version<>_expected OR v_plan.status<>'draft' OR
          EXISTS(SELECT 1 FROM club.academy_training_plan_publications WHERE plan_id=_plan)
        THEN RAISE EXCEPTION 'training_drill_plan_conflict' USING ERRCODE='23505'; END IF;
        FOR v_item IN SELECT value FROM jsonb_array_elements(_items) LOOP
          IF jsonb_typeof(v_item)<>'object' THEN RAISE EXCEPTION 'training_drill_item_invalid' USING ERRCODE='22023'; END IF;
          IF NOT (v_item ?& ARRAY['drill_id','drill_version','duration_minutes'])
            OR (SELECT count(*) FROM jsonb_object_keys(v_item))<>3
            OR jsonb_typeof(v_item->'drill_id')<>'string'
            OR v_item->>'drill_id' !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
            OR jsonb_typeof(v_item->'drill_version')<>'number' OR v_item->>'drill_version' !~ '^[0-9]{1,5}$'
            OR jsonb_typeof(v_item->'duration_minutes')<>'number' OR v_item->>'duration_minutes' !~ '^[0-9]{1,3}$'
          THEN RAISE EXCEPTION 'training_drill_item_invalid' USING ERRCODE='22023'; END IF;
          v_id:=(v_item->>'drill_id')::uuid;v_version:=(v_item->>'drill_version')::integer;
          v_minutes:=(v_item->>'duration_minutes')::integer;
          IF v_id=ANY(v_ids) OR v_version NOT BETWEEN 1 AND 10000 OR v_minutes NOT BETWEEN 1 AND 120
          THEN RAISE EXCEPTION 'training_drill_item_invalid' USING ERRCODE='22023'; END IF;
          v_ids:=array_append(v_ids,v_id);v_total:=v_total+v_minutes;
          -- Retain the existing frozen version when merely reordering/timing.
          SELECT value INTO v_snapshot FROM jsonb_array_elements(v_plan.drill_snapshots)
            WHERE value->>'drill_id'=v_id::text AND value->>'drill_version'=v_version::text;
          IF NOT FOUND THEN
            SELECT * INTO v_drill FROM club.academy_coaching_drills
              WHERE id=v_id AND organization_id='lefteria_fc_academy' AND status='approved' AND version=v_version FOR SHARE;
            IF NOT FOUND THEN RAISE EXCEPTION 'training_drill_catalogue_conflict' USING ERRCODE='23505'; END IF;
            v_snapshot:=jsonb_build_object('drill_id',v_id,'drill_version',v_version,'title',v_drill.title,
              'category',v_drill.category,'age_band',v_drill.age_band,'objective',v_drill.objective,'instructions',v_drill.instructions);
          END IF;
          v_result:=v_result||jsonb_build_array(v_snapshot||jsonb_build_object('duration_minutes',v_minutes));
        END LOOP;
        IF v_total>v_plan.duration_minutes THEN RAISE EXCEPTION 'training_drills_exceed_duration' USING ERRCODE='22023'; END IF;
        UPDATE club.academy_training_plan_drafts SET drill_snapshots=v_result,version=version+1,
          updated_by=v_actor,updated_at=clock_timestamp() WHERE id=_plan AND version=_expected RETURNING * INTO v_plan;
        INSERT INTO club.academy_training_plan_events(id,plan_id,version,action,actor_id,snapshot)
          VALUES(gen_random_uuid(),_plan,v_plan.version,'updated',v_actor,
            to_jsonb(v_plan)-'created_by'-'updated_by'-'created_at'-'updated_at');
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.training.plan.drills.updated','succeeded',
            'lefteria_fc_academy','private_versioned_drill_plan');
        RETURN club.training_drill_plan_view(v_plan);
      END $write$;
      REVOKE ALL ON FUNCTION club.require_training_drill_group(uuid,text),club.guard_training_drill_plan(),
        club.snapshot_training_drill_event(),club.require_training_drill_review_rollout(),
        club.training_drill_plan_view(club.academy_training_plan_drafts),
        club.read_admin_training_drills(text,text,uuid,integer),club.write_admin_training_drills(text,text,uuid,integer,jsonb) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $protect$ BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_events WHERE jsonb_array_length(coalesce(snapshot->'drill_snapshots','[]'::jsonb))>0)
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_drafts WHERE jsonb_array_length(drill_snapshots)>0)
        THEN RAISE EXCEPTION 'training_drill_history_requires_forward_recovery'; END IF;
      END $protect$;
      DROP FUNCTION club.write_admin_training_drills(text,text,uuid,integer,jsonb);
      DROP FUNCTION club.read_admin_training_drills(text,text,uuid,integer);
      DROP FUNCTION club.training_drill_plan_view(club.academy_training_plan_drafts);
      DROP TRIGGER academy_training_drill_review_gate ON club.academy_training_plan_approvals;
      DROP FUNCTION club.require_training_drill_review_rollout();
      DROP TRIGGER academy_training_drill_event_snapshot ON club.academy_training_plan_events;
      DROP FUNCTION club.snapshot_training_drill_event();
      DROP TRIGGER academy_training_drill_plan_guard ON club.academy_training_plan_drafts;
      DROP FUNCTION club.guard_training_drill_plan();
      DROP FUNCTION club.require_training_drill_group(uuid,text);
      ALTER TABLE club.academy_training_plan_drafts DROP COLUMN drill_snapshots;
      """);
}
