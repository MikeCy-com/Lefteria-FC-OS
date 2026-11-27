using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace LefteriaFc.Infrastructure.Persistence.Migrations;
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125100000_AcademyAssignedCoachWorkspace")]
public sealed class AcademyAssignedCoachWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.require_training_assigned_group(_actor uuid,_group text)
      RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $assigned$
      DECLARE v_role text;v_staff text;
      BEGIN
        SELECT role INTO v_role FROM identity.admin_accounts WHERE id=_actor AND is_active
          AND NOT invite_pending AND NOT otp2fa_enabled AND password_hash IS NOT NULL FOR SHARE;
        IF v_role IN ('manager','super_admin') THEN
          PERFORM club.require_training_drill_group(_actor,_group); RETURN;
        END IF;
        IF v_role IS DISTINCT FROM 'coach' THEN
          RAISE EXCEPTION 'training_assigned_group_denied' USING ERRCODE='42501'; END IF;
        SELECT st.id INTO v_staff FROM identity.admin_staff_links link JOIN club.staff st ON st.id=link.staff_id
          WHERE link.admin_account_id=_actor AND st.is_active AND NOT st.is_archived
            AND st.job_role IN ('Head Coach','Assistant Coach','Goalkeeper Coach','Fitness Coach','Youth Coach')
          FOR SHARE OF link,st;
        IF NOT FOUND THEN RAISE EXCEPTION 'training_coach_link_denied' USING ERRCODE='42501'; END IF;
        PERFORM 1 FROM club.academy_group_location_assignments a
          JOIN club.football_operational_scopes s ON s.id=a.scope_id AND s.organization_id=a.organization_id AND s.academy_city_id=a.city_id
          JOIN club.football_cities c ON c.id=a.city_id AND c.is_active
          JOIN club.football_locations l ON l.id=a.location_id AND l.city_id=a.city_id AND l.is_active
          WHERE a.organization_id='lefteria_fc_academy' AND a.group_id=_group FOR SHARE OF a,s,c,l;
        IF NOT FOUND THEN RAISE EXCEPTION 'training_assigned_group_denied' USING ERRCODE='42501'; END IF;
        PERFORM 1 FROM club.staff_academy_groups g WHERE g.staff_id=v_staff
          AND g.organization_id='lefteria_fc_academy' AND g.academy_group_id=_group FOR SHARE;
        IF FOUND THEN RETURN; END IF;
        PERFORM 1 FROM club.academy_group_coaches g WHERE g.staff_id=v_staff
          AND g.organization_id='lefteria_fc_academy' AND g.academy_group_id=_group FOR SHARE;
        IF NOT FOUND THEN RAISE EXCEPTION 'training_coach_assignment_denied' USING ERRCODE='42501'; END IF;
      END $assigned$;
      CREATE FUNCTION club.require_training_draft_actor(_digest text,_owner text,_group text)
      RETURNS TABLE(actor_id uuid,session_id uuid,actor_role text)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $draft_actor$
      BEGIN
        IF _owner IS DISTINCT FROM 'lefteria_fc_academy' THEN
          RAISE EXCEPTION 'training_draft_owner_denied' USING ERRCODE='42501'; END IF;
        SELECT x.actor_id,x.session_id,x.actor_role INTO actor_id,session_id,actor_role
          FROM club.require_central_academy_drills(_digest,false) x;
        PERFORM club.require_training_assigned_group(actor_id,_group);
        RETURN NEXT;
      END $draft_actor$;
      CREATE FUNCTION club.read_admin_coach_training_groups(_digest text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $groups$
      DECLARE v_actor uuid;v_role text;v_staff text;v_group text;v_result jsonb:='[]'::jsonb;
      BEGIN
        SELECT actor_id,actor_role INTO v_actor,v_role FROM club.require_central_academy_drills(_digest,false);
        IF v_role IS DISTINCT FROM 'coach' THEN RAISE EXCEPTION 'training_coach_directory_denied' USING ERRCODE='42501'; END IF;
        SELECT st.id INTO v_staff FROM identity.admin_staff_links link JOIN club.staff st ON st.id=link.staff_id
          WHERE link.admin_account_id=v_actor AND st.is_active AND NOT st.is_archived
            AND st.job_role IN ('Head Coach','Assistant Coach','Goalkeeper Coach','Fitness Coach','Youth Coach')
          FOR SHARE OF link,st;
        IF NOT FOUND THEN RAISE EXCEPTION 'training_coach_link_denied' USING ERRCODE='42501'; END IF;
        FOR v_group IN SELECT group_id FROM (
          SELECT academy_group_id AS group_id FROM club.staff_academy_groups WHERE staff_id=v_staff AND organization_id='lefteria_fc_academy'
          UNION SELECT academy_group_id FROM club.academy_group_coaches WHERE staff_id=v_staff AND organization_id='lefteria_fc_academy'
        ) assigned ORDER BY group_id
        LOOP
          BEGIN
            PERFORM club.require_training_assigned_group(v_actor,v_group);
            SELECT v_result||jsonb_build_array(jsonb_build_object('id',g.id,'name',g.name,'kind','academy_group',
              'active_players',(SELECT count(*) FROM club.players p WHERE p.organization_id=g.organization_id
                AND p.team_type='Academy' AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                AND (p.primary_academy_group_id=g.id OR EXISTS(SELECT 1 FROM club.player_academy_groups m
                  WHERE m.organization_id=g.organization_id AND m.player_id=p.id AND m.academy_group_id=g.id)))))
              INTO v_result FROM club.academy_groups g WHERE g.organization_id='lefteria_fc_academy' AND g.id=v_group;
          EXCEPTION WHEN insufficient_privilege THEN NULL; END;
          IF jsonb_array_length(v_result)>150 THEN RAISE EXCEPTION 'training_coach_group_limit' USING ERRCODE='54000'; END IF;
        END LOOP;
        RETURN jsonb_build_object('organization_id','lefteria_fc_academy','ready',true,'groups',v_result);
      END $groups$;
      CREATE TABLE club.academy_training_plan_submissions(
        plan_id uuid NOT NULL REFERENCES club.academy_training_plan_drafts(id) ON DELETE RESTRICT,
        plan_version integer NOT NULL CHECK(plan_version BETWEEN 1 AND 10000),
        submitted_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        submitted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(plan_id,plan_version)
      );
      ALTER TABLE club.academy_training_plan_submissions ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON club.academy_training_plan_submissions FROM PUBLIC;
      CREATE FUNCTION club.submit_admin_training_plan(_digest text,_group text,_plan uuid,_expected integer,_confirm boolean)
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
          OR v_plan.training_day<(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_approvals WHERE plan_id=_plan AND plan_version=_expected)
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_publications WHERE plan_id=_plan)
          THEN RAISE EXCEPTION 'training_submission_conflict' USING ERRCODE='23505'; END IF;
        INSERT INTO club.academy_training_plan_submissions(plan_id,plan_version,submitted_by) VALUES(_plan,_expected,v_actor);
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.training.plan.submitted','succeeded','lefteria_fc_academy','coach_submission_not_approval');
        RETURN jsonb_build_object('id',_plan,'organization_id','lefteria_fc_academy','group_id',_group,
          'submitted_version',_expected,'submitted',true,'published',false);
      END $submit$;
      REVOKE ALL ON FUNCTION club.require_training_assigned_group(uuid,text),club.require_training_draft_actor(text,text,text),
        club.read_admin_coach_training_groups(text),club.submit_admin_training_plan(text,text,uuid,integer,boolean) FROM PUBLIC;

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
          'status',p.status,'version',p.version,'drill_count',jsonb_array_length(p.drill_snapshots),'submitted_version',CASE WHEN EXISTS(SELECT 1 FROM club.academy_training_plan_submissions sub WHERE sub.plan_id=p.id AND sub.plan_version=p.version) THEN p.version ELSE NULL END)
          ORDER BY p.training_day,p.start_time,p.id),'[]'::jsonb)
        INTO v_rows FROM club.academy_training_plan_drafts p
          WHERE p.organization_id=_owner AND p.group_id=_group
            AND p.training_day BETWEEN _from AND _to;
        RETURN jsonb_build_object('organization_id',_owner,
          'group_id',_group,'from',_from,'to',_to,
          'total',v_total,'plans',v_rows);
      END $plan_read$;

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
          OR _duration NOT BETWEEN 15 AND 240
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
        IF v_role='coach' AND (_archive OR _day<(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date) THEN RAISE EXCEPTION 'training_coach_draft_write_denied' USING ERRCODE='42501'; END IF;
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
          IF v_role='coach' AND (v_current.training_day<(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
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

      CREATE OR REPLACE FUNCTION club.read_admin_academy_training_plan_approvals(
        _digest text,_owner text,_group text,_from date,_to date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_plan_approval_read$
      DECLARE v_result jsonb;v_total integer;v_actor uuid;
      BEGIN
        IF _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
          OR _group ~ '[[:cntrl:]]' OR _from IS NULL OR _to IS NULL
          OR _to<_from OR _to>_from+93 THEN
          RAISE EXCEPTION 'training_plan_approval_filters_invalid'
            USING ERRCODE='22023';
        END IF;
        SELECT actor_id INTO v_actor FROM club.require_training_draft_actor(_digest,_owner,_group);
        IF NOT EXISTS(SELECT 1 FROM club.academy_groups
          WHERE organization_id=_owner AND id=_group) THEN
          RAISE EXCEPTION 'training_plan_approval_group_denied'
            USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_total FROM club.academy_training_plan_drafts p
          WHERE p.organization_id=_owner AND p.group_id=_group
            AND p.training_day BETWEEN _from AND _to;
        IF v_total>500 THEN
          RAISE EXCEPTION 'training_plan_approval_limit' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',p.id,'approved_version',a.plan_version)
          ORDER BY p.training_day,p.id),'[]'::jsonb)
        INTO v_result FROM club.academy_training_plan_drafts p
        JOIN club.academy_training_plan_approvals a
          ON a.plan_id=p.id AND a.plan_version=p.version
        WHERE p.organization_id=_owner AND p.group_id=_group
          AND p.training_day BETWEEN _from AND _to AND p.status='draft';
        RETURN jsonb_build_object('organization_id',_owner,
          'group_id',_group,'from',_from,'to',_to,
          'approvals',v_result,'published',false);
      END $academy_plan_approval_read$;

      CREATE OR REPLACE FUNCTION club.read_admin_academy_training_publications(
        _digest text,_owner text,_group text,_from date,_to date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $native_training_pub_list$
      DECLARE v_count integer;v_rows jsonb;v_actor uuid;
      BEGIN
        IF _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
          OR _group ~ '[[:cntrl:]]' OR _from IS NULL OR _to IS NULL
          OR _to<_from OR _to>_from+93 THEN
          RAISE EXCEPTION 'training_publication_filters_invalid'
            USING ERRCODE='22023'; END IF;
        SELECT actor_id INTO v_actor FROM club.require_training_draft_actor(_digest,_owner,_group);
        IF NOT EXISTS(SELECT 1 FROM club.academy_groups
          WHERE organization_id=_owner AND id=_group)
        THEN RAISE EXCEPTION 'training_publication_group_denied'
          USING ERRCODE='42501'; END IF;
        SELECT count(*) INTO v_count
        FROM club.academy_training_plan_publications pub
        JOIN club.academy_training_plan_drafts plan ON plan.id=pub.plan_id
        WHERE pub.organization_id=_owner AND pub.group_id=_group
          AND plan.training_day BETWEEN _from AND _to;
        IF v_count>500 THEN
          RAISE EXCEPTION 'training_publications_too_many'
            USING ERRCODE='54000'; END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',pub.plan_id,'event_id',pub.event_id,
          'published_version',pub.plan_version,
          'is_cancelled',c.is_cancelled,
          'cancel_reason',c.cancel_reason)
          ORDER BY plan.training_day,pub.plan_id),'[]'::jsonb)
        INTO v_rows FROM club.academy_training_plan_publications pub
        JOIN club.academy_training_plan_drafts plan ON plan.id=pub.plan_id
        JOIN club.calendar_items c ON c.organization_id=pub.organization_id
          AND c.kind='training' AND c.id=pub.event_id
        WHERE pub.organization_id=_owner AND pub.group_id=_group
          AND plan.training_day BETWEEN _from AND _to;
        RETURN jsonb_build_object('organization_id',_owner,
          'group_id',_group,'from',_from,'to',_to,
          'publications',v_rows);
      END $native_training_pub_list$;

      CREATE OR REPLACE FUNCTION club.read_admin_training_drills(_digest text,_group text,_plan uuid,_expected integer)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_actor uuid;v_plan club.academy_training_plan_drafts%ROWTYPE;
      BEGIN
        SELECT actor_id INTO v_actor FROM club.require_training_draft_actor(_digest,'lefteria_fc_academy',_group);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id='lefteria_fc_academy' AND group_id=_group AND version=_expected FOR SHARE;
        IF NOT FOUND THEN RAISE EXCEPTION 'training_drill_plan_conflict' USING ERRCODE='23505'; END IF;
        RETURN club.training_drill_plan_view(v_plan);
      END $read$;

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
        IF v_role='coach' AND (v_plan.training_day<(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_approvals WHERE plan_id=_plan AND plan_version=_expected))
        THEN RAISE EXCEPTION 'training_coach_reviewed_plan_locked' USING ERRCODE='23505'; END IF;
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

      CREATE OR REPLACE FUNCTION club.guard_training_drill_plan()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $guard$
      DECLARE v_total integer;v_has_drills boolean:=false;
      BEGIN
        v_has_drills:=jsonb_array_length(NEW.drill_snapshots)>0;
        IF TG_OP='UPDATE' THEN v_has_drills:=v_has_drills OR jsonb_array_length(OLD.drill_snapshots)>0; END IF;
        IF v_has_drills THEN
          PERFORM club.require_training_assigned_group(NEW.updated_by,NEW.group_id);
          SELECT coalesce(sum((item->>'duration_minutes')::integer),0)::integer INTO v_total
            FROM jsonb_array_elements(NEW.drill_snapshots) item;
          IF v_total>NEW.duration_minutes THEN
            RAISE EXCEPTION 'training_drills_exceed_duration' USING ERRCODE='22023';
          END IF;
        END IF;
        RETURN NEW;
      END $guard$;

      CREATE OR REPLACE FUNCTION club.admin_training_event_scope_allowed(_actor uuid,_owner text,_event text)
      RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $event_scope$
      DECLARE v_group text;v_groups integer:=0;
      BEGIN
        IF _owner IS DISTINCT FROM 'lefteria_fc_academy' THEN RETURN false; END IF;
        PERFORM 1 FROM club.calendar_items c WHERE c.organization_id=_owner AND c.kind='training'
          AND c.id=_event FOR SHARE;
        IF NOT FOUND THEN RETURN false; END IF;
        IF EXISTS(SELECT 1 FROM club.calendar_item_scopes s WHERE s.organization_id=_owner
          AND s.kind='training' AND s.event_id=_event AND s.scope_kind<>'group')
        THEN RETURN false; END IF;
        FOR v_group IN SELECT s.academy_group_id FROM club.calendar_item_scopes s
          WHERE s.organization_id=_owner AND s.kind='training' AND s.event_id=_event
            AND s.scope_kind='group' ORDER BY s.academy_group_id FOR SHARE
        LOOP
          PERFORM club.require_training_assigned_group(_actor,v_group);
          v_groups:=v_groups+1;
        END LOOP;
        RETURN v_groups>0;
      EXCEPTION WHEN insufficient_privilege THEN RETURN false;
      END $event_scope$;

      CREATE OR REPLACE FUNCTION club.read_admin_academy_training_sessions(
        _digest text,_owner text,_group text,_from date,_to date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_training_list$
      DECLARE v_role text;v_actor uuid; v_rows jsonb; v_count integer;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner<>'lefteria_fc_academy'
           OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
           OR _group ~ '[[:cntrl:]]'
           OR _from IS NULL OR _to IS NULL
           OR _from>_to OR _to>_from+93 THEN
          RAISE EXCEPTION 'academy_training_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'attendance_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.role,a.id INTO v_role,v_actor FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin','coach') THEN
          RAISE EXCEPTION 'academy_training_access_denied' USING ERRCODE='42501';
        END IF;
        PERFORM club.require_training_assigned_group(v_actor,_group);
        IF NOT EXISTS(SELECT 1 FROM club.calendar_import_state
          WHERE id='initial' AND is_complete) THEN
          RAISE EXCEPTION 'academy_training_calendar_not_ready' USING ERRCODE='55000';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.academy_groups
          WHERE organization_id=_owner AND id=_group) THEN
          RAISE EXCEPTION 'academy_training_group_not_found' USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_count FROM club.calendar_items c
          WHERE c.organization_id=_owner AND c.kind='training'
          AND c.staff_day BETWEEN _from AND _to
          AND club.admin_training_event_scope_allowed(v_actor,_owner,c.id)
          AND EXISTS (SELECT 1 FROM club.calendar_item_scopes x
            WHERE x.organization_id=c.organization_id AND x.kind=c.kind
              AND x.event_id=c.id AND x.scope_kind='group'
              AND x.academy_group_id=_group);
        IF v_count>500 THEN
          RAISE EXCEPTION 'academy_training_many_events' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',c.id,'day',c.staff_day,'title',
            coalesce(nullif(c.staff_projection_json->>'title',''),c.id),
            'is_cancelled',c.is_cancelled,'is_archived',c.is_archived)
          ORDER BY c.staff_day DESC,c.id COLLATE "C"),'[]'::jsonb)
        INTO v_rows FROM club.calendar_items c
        WHERE c.organization_id=_owner AND c.kind='training'
          AND c.staff_day BETWEEN _from AND _to
          AND club.admin_training_event_scope_allowed(v_actor,_owner,c.id)
          AND EXISTS (SELECT 1 FROM club.calendar_item_scopes x
            WHERE x.organization_id=c.organization_id AND x.kind=c.kind
              AND x.event_id=c.id AND x.scope_kind='group'
              AND x.academy_group_id=_group);
        RETURN jsonb_build_object('organization_id',_owner,
          'group_id',_group,'from',_from,'to',_to,
          'total',v_count,'sessions',v_rows);
      END $academy_training_list$;

      CREATE OR REPLACE FUNCTION club.read_admin_academy_training_attendance(
        _digest text,_owner text,_event text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_attendance_read$
      DECLARE
        v_role text;v_actor uuid; v_date date; v_title text;
        v_cancelled boolean; v_archived boolean;
        v_count integer; v_rows jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner<>'lefteria_fc_academy'
           OR _event IS NULL OR length(_event) NOT BETWEEN 1 AND 128
           OR _event ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'attendance_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'attendance_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.role,a.id INTO v_role,v_actor FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin','coach') THEN
          RAISE EXCEPTION 'attendance_access_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.calendar_import_state
          WHERE id='initial' AND is_complete) THEN
          RAISE EXCEPTION 'attendance_calendar_not_ready' USING ERRCODE='55000';
        END IF;
        IF NOT club.admin_training_event_scope_allowed(v_actor,_owner,_event) THEN
          RAISE EXCEPTION 'attendance_event_scope_denied' USING ERRCODE='42501'; END IF;
        SELECT c.staff_day,
          coalesce(nullif(c.staff_projection_json->>'title',''),c.id),
          c.is_cancelled,c.is_archived
        INTO v_date,v_title,v_cancelled,v_archived
        FROM club.calendar_items c
        WHERE c.organization_id=_owner AND c.kind='training' AND c.id=_event;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'attendance_event_not_found' USING ERRCODE='42501';
        END IF;

        SELECT count(*) INTO v_count FROM club.players p
          WHERE p.organization_id=_owner AND
            club.academy_training_player_eligible(_owner,_event,p.id);
        IF v_count>200 THEN
          RAISE EXCEPTION 'attendance_roster_too_large' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'player_id',p.id,'name',p.name,
          'rsvp',rsvp.status,
          'historical_attendance',hist.status,
          'attendance',coalesce(m.status,hist.status),
          'attendance_source',CASE WHEN m.version IS NOT NULL
            THEN 'os_manager' WHEN hist.status IS NOT NULL
            THEN 'legacy_import' ELSE 'not_recorded' END,
          'version',coalesce(m.version,0))
          ORDER BY p.name COLLATE "C",p.id COLLATE "C"),'[]'::jsonb)
        INTO v_rows FROM club.players p
        LEFT JOIN club.academy_training_attendance m
          ON m.organization_id=_owner AND m.kind='training'
          AND m.event_id=_event AND m.player_id=p.id
        LEFT JOIN club.calendar_responses hist
          ON hist.organization_id=_owner AND hist.kind='training'
          AND hist.event_id=_event AND hist.player_id=p.id
          AND hist.collection='attendance'
        LEFT JOIN LATERAL (
          SELECT r.status FROM club.calendar_responses r
          WHERE r.organization_id=_owner AND r.kind='training'
            AND r.event_id=_event AND r.player_id=p.id
            AND r.collection IN ('availability','event_attendance')
          ORDER BY CASE WHEN r.collection='availability' THEN 0 ELSE 1 END
          LIMIT 1
        ) rsvp ON true
        WHERE p.organization_id=_owner AND
          club.academy_training_player_eligible(_owner,_event,p.id);

        RETURN jsonb_build_object('organization_id',_owner,
          'event_id',_event,'title',v_title,'day',v_date,
          'is_cancelled',v_cancelled,'is_archived',v_archived,
          'writable',NOT v_cancelled AND NOT v_archived AND
            v_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date,
          'total',v_count,'players',v_rows);
      END $academy_attendance_read$;

      CREATE OR REPLACE FUNCTION club.record_admin_academy_training_attendance(
        _digest text,_owner text,_event text,_player text,
        _status text,_expected integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_attendance_write$
      DECLARE
        v_role text; v_actor uuid; v_session uuid;
        v_day date; v_cancelled boolean; v_archived boolean;
        v_player text; v_previous text; v_existing text; v_imported text;
        v_version integer; v_next integer; v_event uuid:=gen_random_uuid();
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner<>'lefteria_fc_academy'
           OR _event IS NULL OR length(_event) NOT BETWEEN 1 AND 128
           OR _event ~ '[[:cntrl:]]'
           OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
           OR _player ~ '[[:cntrl:]]'
           OR _status NOT IN ('present','absent') OR _status IS NULL
           OR _expected IS NULL OR _expected NOT BETWEEN 0 AND 9999
        THEN RAISE EXCEPTION 'attendance_write_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'attendance_authority_revoked' USING ERRCODE='42501';
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
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin','coach') THEN
          RAISE EXCEPTION 'attendance_write_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.calendar_import_state
          WHERE id='initial' AND is_complete) THEN
          RAISE EXCEPTION 'attendance_calendar_not_ready' USING ERRCODE='55000';
        END IF;
        IF NOT club.admin_training_event_scope_allowed(v_actor,_owner,_event) THEN
          RAISE EXCEPTION 'attendance_event_scope_denied' USING ERRCODE='42501'; END IF;
        SELECT staff_day,is_cancelled,is_archived
        INTO v_day,v_cancelled,v_archived FROM club.calendar_items
        WHERE organization_id=_owner AND kind='training' AND id=_event FOR SHARE;
        IF NOT FOUND OR v_cancelled OR v_archived OR
           v_day>(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN
          RAISE EXCEPTION 'attendance_event_not_writable' USING ERRCODE='42501';
        END IF;
        -- Lock the player record to serialize simultaneous manager changes,
        -- and re-evaluate membership instead of trusting a UI-supplied name.
        SELECT p.id INTO v_player FROM club.players p
        WHERE p.organization_id=_owner AND p.id=_player
          AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
        FOR UPDATE;
        IF NOT FOUND OR NOT club.academy_training_player_eligible(
          _owner,_event,_player) THEN
          RAISE EXCEPTION 'attendance_player_not_eligible' USING ERRCODE='42501';
        END IF;
        SELECT hist.status INTO v_imported FROM club.calendar_responses hist
        WHERE hist.organization_id=_owner AND hist.kind='training'
          AND hist.event_id=_event AND hist.player_id=_player
          AND hist.collection='attendance';
        SELECT m.status,m.version INTO v_existing,v_version
        FROM club.academy_training_attendance m
        WHERE m.organization_id=_owner AND m.kind='training'
          AND m.event_id=_event AND m.player_id=_player FOR UPDATE;
        IF FOUND THEN
          IF v_existing=_status THEN
            RETURN jsonb_build_object('event_id',_event,
              'player_id',_player,'status',v_existing,
              'version',v_version,'changed',false,'replay',true);
          END IF;
          IF v_version<>_expected THEN
            RAISE EXCEPTION 'attendance_stale_version' USING ERRCODE='23505';
          END IF;
          v_previous:=v_existing; v_next:=v_version+1;
          UPDATE club.academy_training_attendance
            SET status=_status,version=v_next,
                recorded_by=v_actor,recorded_at=clock_timestamp()
          WHERE organization_id=_owner AND kind='training'
            AND event_id=_event AND player_id=_player;
        ELSE
          IF _expected<>0 THEN
            RAISE EXCEPTION 'attendance_stale_version' USING ERRCODE='23505';
          END IF;
          IF v_imported=_status THEN
            RETURN jsonb_build_object('event_id',_event,
              'player_id',_player,'status',v_imported,
              'version',0,'changed',false,'replay',true);
          END IF;
          v_previous:=v_imported; v_next:=1;
          INSERT INTO club.academy_training_attendance(
            organization_id,kind,event_id,player_id,status,version,recorded_by)
          VALUES(_owner,'training',_event,_player,_status,v_next,v_actor);
        END IF;
        INSERT INTO club.academy_training_attendance_events(
          id,organization_id,kind,event_id,player_id,previous_status,
          status,version,actor_id)
        VALUES(v_event,_owner,'training',_event,_player,v_previous,
          _status,v_next,v_actor);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.academy.attendance.record','succeeded',_owner,_status);
        RETURN jsonb_build_object('event_id',_event,
          'player_id',_player,'status',_status,'version',v_next,
          'changed',true,'replay',false,'audit_event_id',v_event);
      END $academy_attendance_write$;
      """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Assigned coach workflow requires audited forward recovery.");
}
