using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125120000_AcademyWeeklyCompleteSession")]
public sealed class AcademyWeeklyCompleteSession : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Rolling seven calendar days in the Academy timezone, including today.
      CREATE FUNCTION club.coach_training_week_allows(_day date)
      RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog,pg_temp AS $week$
        SELECT coalesce(_day BETWEEN (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
          AND (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+6,false)
      $week$;
      CREATE FUNCTION club.training_delivery_game_minutes(_items jsonb)
      RETURNS integer LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,pg_temp AS $game$
        SELECT coalesce(sum((x->>'duration_minutes')::integer) FILTER(WHERE x->>'category'='game'),0)::integer
        FROM jsonb_array_elements(_items) x
      $game$;
      CREATE FUNCTION club.training_delivery_complete(_items jsonb,_duration integer)
      RETURNS boolean LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,pg_temp AS $complete$
        SELECT coalesce(_duration>=20 AND jsonb_array_length(_items)>0
          AND (SELECT coalesce(sum((x->>'duration_minutes')::integer),0) FROM jsonb_array_elements(_items) x)=_duration
          AND club.training_delivery_game_minutes(_items)>=20,false)
      $complete$;
      CREATE FUNCTION club.require_complete_training_delivery(_items jsonb,_duration integer)
      RETURNS void LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog,pg_temp AS $require_complete$
      BEGIN
        IF NOT club.training_delivery_complete(_items,_duration)
          THEN RAISE EXCEPTION 'training_delivery_requires_full_duration_and_20_game_minutes' USING ERRCODE='22023'; END IF;
      END $require_complete$;
      REVOKE ALL ON FUNCTION club.coach_training_week_allows(date),club.training_delivery_game_minutes(jsonb),
        club.training_delivery_complete(jsonb,integer),club.require_complete_training_delivery(jsonb,integer) FROM PUBLIC;

      CREATE OR REPLACE FUNCTION club.write_admin_academy_training_plan(
        _digest text,_owner text,_id uuid,_group text,_day date,_start time,
        _duration integer,_title text,_location text,_notes text,
        _expected integer,_archive boolean
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $plan_write$
      DECLARE
        v_actor uuid;v_session uuid;v_role text;v_version integer;v_status text;
        v_current club.academy_training_plan_drafts%ROWTYPE;
        v_action text;v_snapshot jsonb;
      BEGIN
        IF _id IS NULL OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
          OR _group ~ '[[:cntrl:]]' OR _day IS NULL
          OR _start IS NULL OR _duration IS NULL
          OR _duration NOT BETWEEN 20 AND 240
          OR _title IS NULL OR length(_title) NOT BETWEEN 1 AND 180
          OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
          OR _location IS NULL OR length(_location)>200
          OR _location ~ '[[:cntrl:]]'
          OR _notes IS NULL OR length(_notes)>2000
          OR _notes ~ '[[:cntrl:]]'
          OR _expected IS NULL OR _expected NOT BETWEEN 0 AND 9999
          OR _archive IS NULL OR (_expected=0 AND _archive) THEN
          RAISE EXCEPTION 'training_plan_invalid' USING ERRCODE='22023';
        END IF;
        SELECT actor_id,session_id,actor_role INTO v_actor,v_session,v_role FROM
          club.require_training_draft_actor(_digest,_owner,_group);
        IF v_role='coach' AND (_archive OR NOT club.coach_training_week_allows(_day)) THEN RAISE EXCEPTION 'training_coach_draft_write_denied' USING ERRCODE='42501'; END IF;
        IF NOT EXISTS(SELECT 1 FROM club.academy_groups
          WHERE organization_id=_owner AND id=_group) THEN
          RAISE EXCEPTION 'training_plan_group_denied' USING ERRCODE='42501';
        END IF;
        IF _expected=0 THEN
          IF _archive THEN RAISE EXCEPTION 'cannot_archive_uncreated_plan'
            USING ERRCODE='22023'; END IF;
          INSERT INTO club.academy_training_plan_drafts(
            id,organization_id,group_id,training_day,start_time,
            duration_minutes,title,location,notes,created_by,updated_by)
          VALUES(_id,_owner,_group,_day,_start,_duration,_title,
            _location,_notes,v_actor,v_actor);
          v_version:=1;v_status:='draft';v_action:='created';
        ELSE
          SELECT * INTO v_current FROM club.academy_training_plan_drafts
            WHERE id=_id AND organization_id=_owner AND group_id=_group FOR UPDATE;
          IF NOT FOUND OR v_current.group_id<>_group OR
            v_current.version<>_expected OR v_current.status<>'draft' THEN
            RAISE EXCEPTION 'training_plan_version_conflict' USING ERRCODE='23505';
          END IF;
          IF v_role='coach' AND (NOT club.coach_training_week_allows(v_current.training_day)
            OR EXISTS(SELECT 1 FROM club.academy_training_plan_approvals WHERE plan_id=_id AND plan_version=_expected)
            OR EXISTS(SELECT 1 FROM club.academy_training_plan_publications WHERE plan_id=_id))
          THEN RAISE EXCEPTION 'training_coach_reviewed_plan_locked' USING ERRCODE='23505'; END IF;
          v_status:=CASE WHEN _archive THEN 'archived' ELSE 'draft' END;
          v_action:=CASE WHEN _archive THEN 'archived' ELSE 'updated' END;
          UPDATE club.academy_training_plan_drafts SET
            training_day=_day,start_time=_start,duration_minutes=_duration,
            title=_title,location=_location,notes=_notes,status=v_status,
            version=version+1,updated_by=v_actor,updated_at=clock_timestamp()
          WHERE id=_id AND version=_expected RETURNING version INTO v_version;
        END IF;
        SELECT jsonb_build_object('id',p.id,'organization_id',p.organization_id,
          'group_id',p.group_id,'day',p.training_day,
          'start_time',to_char(p.start_time,'HH24:MI'),
          'duration_minutes',p.duration_minutes,'title',p.title,
          'location',p.location,'notes',p.notes,'status',p.status,
          'version',p.version) INTO v_snapshot
        FROM club.academy_training_plan_drafts p WHERE p.id=_id;
        INSERT INTO club.academy_training_plan_events(
          id,plan_id,version,action,actor_id,snapshot)
        VALUES(gen_random_uuid(),_id,v_version,v_action,v_actor,v_snapshot);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,
          device_session_id,action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          v_session,'admin.training.plan.'||v_action,'succeeded',
          _owner,'private_unpublished_plan');
        RETURN v_snapshot;
      END $plan_write$;
      CREATE OR REPLACE FUNCTION club.write_admin_training_delivery(_digest text,_group text,_plan uuid,_schedule integer,_expected integer,_items jsonb)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $delivery_write$
      DECLARE v_actor uuid;v_session uuid;v_role text;v_plan club.academy_training_plan_drafts%ROWTYPE;v_current jsonb;
        v_item jsonb;v_snapshot jsonb;v_result jsonb:='[]'::jsonb;v_ids uuid[]:=ARRAY[]::uuid[];
        v_id uuid;v_version integer;v_minutes integer;v_total integer:=0;
        v_drill club.academy_coaching_drills%ROWTYPE;
      BEGIN
        SELECT actor_id,session_id,actor_role INTO v_actor,v_session,v_role FROM club.require_training_draft_actor(_digest,'lefteria_fc_academy',_group);
        IF _plan IS NULL OR _schedule IS NULL OR _schedule NOT BETWEEN 1 AND 10000 OR _expected IS NULL OR _expected NOT BETWEEN 0 AND 9999
          OR jsonb_typeof(_items) IS DISTINCT FROM 'array' THEN RAISE EXCEPTION 'training_delivery_input_invalid' USING ERRCODE='22023'; END IF;
        IF jsonb_array_length(_items)>24 THEN RAISE EXCEPTION 'training_drill_limit' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts WHERE id=_plan AND group_id=_group
          AND organization_id='lefteria_fc_academy' FOR UPDATE;
        IF NOT FOUND OR v_plan.version<>_schedule OR v_plan.status<>'draft'
          OR v_plan.training_day<(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
          OR (v_role='coach' AND NOT club.coach_training_week_allows(v_plan.training_day))
          OR EXISTS(SELECT 1 FROM club.academy_training_publication_cancellations WHERE plan_id=_plan)
        THEN RAISE EXCEPTION 'training_delivery_schedule_conflict' USING ERRCODE='23505'; END IF;
        v_current:=club.training_delivery_view(v_plan);
        IF (v_current->>'sequence_version')::integer<>_expected THEN RAISE EXCEPTION 'training_delivery_version_conflict' USING ERRCODE='23505'; END IF;
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
          SELECT value INTO v_snapshot FROM jsonb_array_elements(v_current->'drills')
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

        PERFORM club.require_complete_training_delivery(v_result,v_plan.duration_minutes);
        INSERT INTO club.academy_training_delivery_revisions(plan_id,sequence_version,schedule_version,actor_id,drills)
          VALUES(_plan,_expected+1,_schedule,v_actor,v_result);
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.training.delivery.updated','succeeded','lefteria_fc_academy','private_coaching_sequence_no_publication');
        RETURN club.training_drill_plan_view(v_plan)||jsonb_build_object('sequence_version',_expected+1,'drills',v_result);
      END $delivery_write$;
      CREATE OR REPLACE FUNCTION club.write_admin_training_drills(_digest text,_group text,_plan uuid,_expected integer,_items jsonb)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $write$
      DECLARE v_actor uuid;v_session uuid;v_role text;v_plan club.academy_training_plan_drafts%ROWTYPE;
        v_item jsonb;v_snapshot jsonb;v_result jsonb:='[]'::jsonb;v_ids uuid[]:=ARRAY[]::uuid[];
        v_id uuid;v_version integer;v_minutes integer;v_total integer:=0;
        v_drill club.academy_coaching_drills%ROWTYPE;
      BEGIN
        SELECT actor_id,session_id,actor_role INTO v_actor,v_session,v_role FROM club.require_training_draft_actor(_digest,'lefteria_fc_academy',_group);
        IF _plan IS NULL OR _expected IS NULL OR _expected NOT BETWEEN 1 AND 9999 OR _items IS NULL
          OR jsonb_typeof(_items)<>'array' THEN RAISE EXCEPTION 'training_drill_input_invalid' USING ERRCODE='22023'; END IF;
        IF jsonb_array_length(_items)>24 THEN RAISE EXCEPTION 'training_drill_limit' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id='lefteria_fc_academy' AND group_id=_group FOR UPDATE;
        IF NOT FOUND OR v_plan.version<>_expected OR v_plan.status<>'draft' OR
          EXISTS(SELECT 1 FROM club.academy_training_plan_publications WHERE plan_id=_plan)
        THEN RAISE EXCEPTION 'training_drill_plan_conflict' USING ERRCODE='23505'; END IF;
        IF v_role='coach' AND (NOT club.coach_training_week_allows(v_plan.training_day)
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_approvals WHERE plan_id=_plan AND plan_version=_expected))
        THEN RAISE EXCEPTION 'training_coach_reviewed_plan_locked' USING ERRCODE='23505'; END IF;
        IF v_role='coach' THEN RAISE EXCEPTION 'coach_use_weekly_delivery' USING ERRCODE='42501'; END IF;
        IF EXISTS(SELECT 1 FROM club.academy_training_delivery_revisions WHERE plan_id=_plan)
        THEN RAISE EXCEPTION 'training_delivery_use_independent_sequence' USING ERRCODE='23505'; END IF;
        FOR v_item IN SELECT value FROM jsonb_array_elements(_items) LOOP
          IF jsonb_typeof(v_item)<>'object' THEN RAISE EXCEPTION 'training_drill_item_invalid' USING ERRCODE='22023'; END IF;
          IF NOT (v_item ?& ARRAY['drill_id','drill_version','duration_minutes'])
            OR (SELECT count(*) FROM jsonb_object_keys(v_item))<>3
            OR jsonb_typeof(v_item->'drill_id')<>'string'
            OR v_item->>'drill_id' !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Preserve coaching revisions and proposals using forward recovery.");
}

            OR jsonb_typeof(v_item->'drill_version')<>'number' OR v_item->>'drill_version' !~ '^[0-9]{1,5}(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Preserve coaching revisions and proposals using forward recovery.");
}

            OR jsonb_typeof(v_item->'duration_minutes')<>'number' OR v_item->>'duration_minutes' !~ '^[0-9]{1,3}(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Preserve coaching revisions and proposals using forward recovery.");
}

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
      CREATE OR REPLACE FUNCTION club.submit_admin_training_plan(_digest text,_group text,_plan uuid,_expected integer,_confirm boolean)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $submit$
      DECLARE v_actor uuid;v_session uuid;v_role text;v_plan club.academy_training_plan_drafts%ROWTYPE;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL OR _expected NOT BETWEEN 1 AND 10000 OR _confirm IS DISTINCT FROM true
          THEN RAISE EXCEPTION 'training_submission_invalid' USING ERRCODE='22023'; END IF;
        SELECT actor_id,session_id,actor_role INTO v_actor,v_session,v_role
          FROM club.require_training_draft_actor(_digest,'lefteria_fc_academy',_group);
        IF v_role IS DISTINCT FROM 'coach' THEN RAISE EXCEPTION 'training_submission_coach_only' USING ERRCODE='42501'; END IF;
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts WHERE id=_plan
          AND organization_id='lefteria_fc_academy' AND group_id=_group FOR UPDATE;
        IF NOT FOUND OR v_plan.version<>_expected OR v_plan.status<>'draft'
          OR NOT club.coach_training_week_allows(v_plan.training_day)
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_approvals WHERE plan_id=_plan AND plan_version=_expected)
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_publications WHERE plan_id=_plan)
          THEN RAISE EXCEPTION 'training_submission_conflict' USING ERRCODE='23505'; END IF;
        PERFORM club.require_complete_training_delivery(club.training_delivery_view(v_plan)->'drills',v_plan.duration_minutes);
        INSERT INTO club.academy_training_plan_submissions(plan_id,plan_version,submitted_by) VALUES(_plan,_expected,v_actor);
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.training.plan.submitted','succeeded','lefteria_fc_academy','coach_submission_not_approval');
        RETURN jsonb_build_object('id',_plan,'organization_id','lefteria_fc_academy','group_id',_group,
          'submitted_version',_expected,'submitted',true,'published',false);
      END $submit$;
      CREATE OR REPLACE FUNCTION club.read_admin_academy_training_plans(
        _digest text,_owner text,_group text,_from date,_to date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $plan_read$
      DECLARE v_rows jsonb;v_total integer;v_actor uuid;
      BEGIN
        IF _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
          OR _group ~ '[[:cntrl:]]' OR _from IS NULL OR _to IS NULL
          OR _to<_from OR _to>_from+93 THEN
          RAISE EXCEPTION 'training_plan_filters_invalid' USING ERRCODE='22023';
        END IF;
        SELECT actor_id INTO v_actor FROM club.require_training_draft_actor(_digest,_owner,_group);
        IF NOT EXISTS(SELECT 1 FROM club.academy_groups
          WHERE organization_id=_owner AND id=_group) THEN
          RAISE EXCEPTION 'training_plan_group_denied' USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_total FROM club.academy_training_plan_drafts p
          WHERE p.organization_id=_owner AND p.group_id=_group
            AND p.training_day BETWEEN _from AND _to;
        IF v_total>500 THEN
          RAISE EXCEPTION 'training_plan_list_too_large' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',p.id,'organization_id',p.organization_id,
          'group_id',p.group_id,'day',p.training_day,
          'start_time',to_char(p.start_time,'HH24:MI'),
          'duration_minutes',p.duration_minutes,'title',p.title,
          'location',p.location,'notes',p.notes,
          'status',p.status,'version',p.version,'drill_count',jsonb_array_length(p.drill_snapshots),'delivery_drill_count',jsonb_array_length(club.training_delivery_view(p)->'drills'),'delivery_game_minutes',club.training_delivery_game_minutes(club.training_delivery_view(p)->'drills'),'delivery_complete',club.training_delivery_complete(club.training_delivery_view(p)->'drills',p.duration_minutes),'submitted_version',CASE WHEN EXISTS(SELECT 1 FROM club.academy_training_plan_submissions sub WHERE sub.plan_id=p.id AND sub.plan_version=p.version) THEN p.version ELSE NULL END)
          ORDER BY p.training_day,p.start_time,p.id),'[]'::jsonb)
        INTO v_rows FROM club.academy_training_plan_drafts p
          WHERE p.organization_id=_owner AND p.group_id=_group
            AND p.training_day BETWEEN _from AND _to;
        RETURN jsonb_build_object('organization_id',_owner,
          'group_id',_group,'from',_from,'to',_to,
          'total',v_total,'plans',v_rows);
      END $plan_read$;
      CREATE OR REPLACE FUNCTION club.guard_training_delivery_duration()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $delivery_guard$
      DECLARE v_total integer;
      BEGIN
        SELECT coalesce(sum((x->>'duration_minutes')::integer),0)::integer INTO v_total
        FROM jsonb_array_elements(coalesce((SELECT drills FROM club.academy_training_delivery_revisions WHERE plan_id=NEW.id
          ORDER BY sequence_version DESC LIMIT 1),'[]'::jsonb)) x;
        IF EXISTS(SELECT 1 FROM club.academy_training_delivery_revisions WHERE plan_id=NEW.id) AND v_total<>NEW.duration_minutes THEN
          RAISE EXCEPTION 'training_delivery_duration_change_requires_replanning' USING ERRCODE='22023'; END IF;
        IF v_total>NEW.duration_minutes THEN RAISE EXCEPTION 'training_delivery_exceeds_schedule' USING ERRCODE='22023'; END IF;
        RETURN NEW;
      END $delivery_guard$;
      """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Preserve weekly training policy using forward recovery.");
}
