using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125110000_AcademyCoachDrillAutonomy")]
public sealed class AcademyCoachDrillAutonomy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $trim_fix$
      DECLARE v_name text;
      BEGIN
        FOR v_name IN SELECT conname FROM pg_constraint WHERE conrelid='club.academy_coaching_drills'::regclass
          AND contype='c' AND pg_get_constraintdef(oid) LIKE '%btrim(instructions%'
        LOOP EXECUTE format('ALTER TABLE club.academy_coaching_drills DROP CONSTRAINT %I',v_name); END LOOP;
        ALTER TABLE club.academy_coaching_drills ADD CONSTRAINT academy_drill_instruction_text
          CHECK(length(instructions) BETWEEN 1 AND 4000 AND instructions=btrim(instructions,' '||chr(10)) AND replace(instructions,chr(10),'') !~ '[[:cntrl:]]');
      END $trim_fix$;

      CREATE OR REPLACE FUNCTION club.write_central_academy_drill(_digest text,_id uuid,_expected integer,_action text,
        _title text,_category text,_age text,_duration integer,_objective text,_instructions text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $write$
      DECLARE v_actor uuid;v_session uuid;v_row club.academy_coaching_drills%ROWTYPE;v_snapshot jsonb;
      BEGIN
        SELECT actor_id,session_id INTO v_actor,v_session FROM club.require_central_academy_drills(_digest,true);
        IF _id IS NULL OR _expected IS NULL OR _expected NOT BETWEEN 0 AND 9999
          OR _action IS NULL OR _action NOT IN ('create','edit','approve','archive','restore')
          OR (_action='create') IS DISTINCT FROM (_expected=0) THEN
          RAISE EXCEPTION 'drill_command_invalid' USING ERRCODE='22023';
        END IF;
        IF _action IN ('create','edit') THEN
          IF _title IS NULL OR length(_title) NOT BETWEEN 1 AND 180 OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
            OR _category IS NULL OR _category NOT IN ('technical','tactical','physical','goalkeeping','warmup','game','recovery','other')
            OR _age IS NULL OR _age NOT IN ('all','u6-u8','u9-u11','u12-u14','u15-u18','adult')
            OR _duration IS NULL OR _duration NOT BETWEEN 1 AND 120
            OR _objective IS NULL OR length(_objective) NOT BETWEEN 1 AND 1000 OR _objective<>btrim(_objective) OR _objective ~ '[[:cntrl:]]'
            OR _instructions IS NULL OR length(_instructions) NOT BETWEEN 1 AND 4000 OR _instructions<>btrim(_instructions,' '||chr(10)) OR replace(_instructions,chr(10),'') ~ '[[:cntrl:]]'
          THEN RAISE EXCEPTION 'drill_content_invalid' USING ERRCODE='22023'; END IF;
        ELSE
          IF _title IS NOT NULL OR _category IS NOT NULL OR _age IS NOT NULL OR _duration IS NOT NULL
            OR _objective IS NOT NULL OR _instructions IS NOT NULL
          THEN RAISE EXCEPTION 'drill_lifecycle_content_invalid' USING ERRCODE='22023'; END IF;
        END IF;
        IF _action='create' THEN
          INSERT INTO club.academy_coaching_drills(id,organization_id,title,category,age_band,duration_minutes,objective,instructions,status,version)
          VALUES(_id,'lefteria_fc_academy',_title,_category,_age,_duration,_objective,_instructions,'draft',1)
          RETURNING * INTO v_row;
        ELSE
          SELECT * INTO v_row FROM club.academy_coaching_drills
            WHERE id=_id AND organization_id='lefteria_fc_academy' FOR UPDATE;
          IF NOT FOUND OR v_row.version<>_expected OR
            (_action IN ('edit','archive') AND v_row.status NOT IN ('draft','approved')) OR
            (_action='approve' AND v_row.status<>'draft') OR
            (_action='restore' AND v_row.status<>'archived')
          THEN RAISE EXCEPTION 'drill_version_conflict' USING ERRCODE='23505'; END IF;
          UPDATE club.academy_coaching_drills SET
            title=CASE WHEN _action='edit' THEN _title ELSE title END,
            category=CASE WHEN _action='edit' THEN _category ELSE category END,
            age_band=CASE WHEN _action='edit' THEN _age ELSE age_band END,
            duration_minutes=CASE WHEN _action='edit' THEN _duration ELSE duration_minutes END,
            objective=CASE WHEN _action='edit' THEN _objective ELSE objective END,
            instructions=CASE WHEN _action='edit' THEN _instructions ELSE instructions END,
            status=CASE _action WHEN 'approve' THEN 'approved' WHEN 'archive' THEN 'archived' ELSE 'draft' END,
            version=version+1 WHERE id=_id AND version=_expected RETURNING * INTO v_row;
        END IF;
        v_snapshot:=club.central_academy_drill_view(v_row);
        INSERT INTO club.academy_coaching_drill_versions(drill_id,version,action,actor_id,snapshot)
          VALUES(_id,v_row.version,_action,v_actor,v_snapshot);
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.drill.'||_action,'succeeded','lefteria_fc_academy','central_coaching_library');
        RETURN v_snapshot;
      END $write$;
      -- Proposals enter the existing private central draft lifecycle, never
      -- the approved catalogue. Only the originating coach sees their list.
      CREATE TABLE club.academy_coach_drill_proposals(
        drill_id uuid PRIMARY KEY REFERENCES club.academy_coaching_drills(id) ON DELETE RESTRICT,
        coach_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        submitted_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );
      ALTER TABLE club.academy_coach_drill_proposals ENABLE ROW LEVEL SECURITY;
      CREATE INDEX coach_drill_proposals_owner ON club.academy_coach_drill_proposals(coach_id,submitted_at DESC,drill_id);
      CREATE FUNCTION club.require_drill_proposal_coach(_digest text)
      RETURNS TABLE(actor_id uuid,session_id uuid)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $proposer$
      DECLARE v_role text;
      BEGIN
        SELECT x.actor_id,x.session_id,x.actor_role INTO actor_id,session_id,v_role
          FROM club.require_central_academy_drills(_digest,false) x;
        IF v_role IS DISTINCT FROM 'coach' THEN RAISE EXCEPTION 'drill_proposal_coach_only' USING ERRCODE='42501'; END IF;
        PERFORM 1 FROM identity.admin_staff_links link JOIN club.staff st ON st.id=link.staff_id
          WHERE link.admin_account_id=actor_id AND st.is_active AND NOT st.is_archived
            AND st.job_role IN ('Head Coach','Assistant Coach','Goalkeeper Coach','Fitness Coach','Youth Coach') FOR SHARE OF link,st;
        IF NOT FOUND THEN RAISE EXCEPTION 'drill_proposal_staff_denied' USING ERRCODE='42501'; END IF;
        RETURN NEXT;
      END $proposer$;
      CREATE FUNCTION club.submit_coach_drill_proposal(_digest text,_id uuid,_title text,_category text,_age text,
        _duration integer,_objective text,_instructions text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $proposal$
      DECLARE v_actor uuid;v_session uuid;v_row club.academy_coaching_drills%ROWTYPE;v_snapshot jsonb;
      BEGIN
        SELECT actor_id,session_id INTO v_actor,v_session FROM club.require_drill_proposal_coach(_digest);
        IF _id IS NULL THEN RAISE EXCEPTION 'drill_proposal_invalid' USING ERRCODE='22023'; END IF;
          IF _title IS NULL OR length(_title) NOT BETWEEN 1 AND 180 OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
            OR _category IS NULL OR _category NOT IN ('technical','tactical','physical','goalkeeping','warmup','game','recovery','other')
            OR _age IS NULL OR _age NOT IN ('all','u6-u8','u9-u11','u12-u14','u15-u18','adult')
            OR _duration IS NULL OR _duration NOT BETWEEN 1 AND 120
            OR _objective IS NULL OR length(_objective) NOT BETWEEN 1 AND 1000 OR _objective<>btrim(_objective) OR _objective ~ '[[:cntrl:]]'
            OR _instructions IS NULL OR length(_instructions) NOT BETWEEN 1 AND 4000 OR _instructions<>btrim(_instructions,' '||chr(10)) OR replace(_instructions,chr(10),'') ~ '[[:cntrl:]]'
          THEN RAISE EXCEPTION 'drill_content_invalid' USING ERRCODE='22023'; END IF;
        -- Serialize the pending quota per coach without exposing any table.
        PERFORM pg_advisory_xact_lock(hashtextextended('coach_drill_proposal:'||v_actor::text,0));
        IF (SELECT count(*) FROM club.academy_coach_drill_proposals p JOIN club.academy_coaching_drills d ON d.id=p.drill_id
          WHERE p.coach_id=v_actor AND d.status='draft')>=50
        THEN RAISE EXCEPTION 'drill_proposal_pending_limit' USING ERRCODE='54000'; END IF;
        INSERT INTO club.academy_coaching_drills(id,organization_id,title,category,age_band,duration_minutes,objective,instructions,status,version)
          VALUES(_id,'lefteria_fc_academy',_title,_category,_age,_duration,_objective,_instructions,'draft',1) RETURNING * INTO v_row;
        INSERT INTO club.academy_coach_drill_proposals(drill_id,coach_id) VALUES(_id,v_actor);
        v_snapshot:=club.central_academy_drill_view(v_row);
        INSERT INTO club.academy_coaching_drill_versions(drill_id,version,action,actor_id,snapshot)
          VALUES(_id,1,'create',v_actor,v_snapshot);
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.drill.proposed','succeeded','lefteria_fc_academy','coach_proposal_not_approved');
        RETURN v_snapshot;
      END $proposal$;
      CREATE FUNCTION club.read_coach_drill_proposals(_digest text,_offset integer)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $proposals$
      DECLARE v_actor uuid;v_role text;v_count integer;v_rows jsonb;
      BEGIN
        SELECT actor_id,actor_role INTO v_actor,v_role FROM club.require_central_academy_drills(_digest,false);
        IF v_role='coach' THEN PERFORM club.require_drill_proposal_coach(_digest);
        ELSIF v_role IS DISTINCT FROM 'super_admin' THEN RAISE EXCEPTION 'drill_proposal_list_denied' USING ERRCODE='42501'; END IF;
        IF _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN RAISE EXCEPTION 'drill_proposal_offset_invalid' USING ERRCODE='22023'; END IF;
        WITH page AS (
          SELECT d AS drill,p.submitted_at,p.drill_id FROM club.academy_coach_drill_proposals p
          JOIN club.academy_coaching_drills d ON d.id=p.drill_id
          WHERE (v_role='coach' AND p.coach_id=v_actor) OR (v_role='super_admin' AND d.status='draft')
          ORDER BY p.submitted_at DESC,p.drill_id LIMIT 31 OFFSET _offset
        ), numbered AS (SELECT drill,row_number() OVER(ORDER BY submitted_at DESC,drill_id) rn FROM page)
        SELECT coalesce(jsonb_agg(club.central_academy_drill_view(drill) ORDER BY rn) FILTER(WHERE rn<=30),'[]'::jsonb),
          count(*)::integer INTO v_rows,v_count FROM numbered;
        RETURN jsonb_build_object('organization_id','lefteria_fc_academy','drills',v_rows,
          'next_offset',CASE WHEN v_count=31 AND _offset<=99970 THEN _offset+30 ELSE NULL END);
      END $proposals$;
      REVOKE ALL ON club.academy_coach_drill_proposals FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.require_drill_proposal_coach(text),club.submit_coach_drill_proposal(text,uuid,text,text,text,integer,text,text),club.read_coach_drill_proposals(text,integer) FROM PUBLIC;

      CREATE TABLE club.academy_training_delivery_revisions(
        plan_id uuid NOT NULL REFERENCES club.academy_training_plan_drafts(id) ON DELETE RESTRICT,
        sequence_version integer NOT NULL CHECK(sequence_version BETWEEN 1 AND 10000),
        schedule_version integer NOT NULL CHECK(schedule_version BETWEEN 1 AND 10000),
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        drills jsonb NOT NULL CHECK(jsonb_typeof(drills)='array' AND jsonb_array_length(drills)<=24),
        PRIMARY KEY(plan_id,sequence_version)
      );
      ALTER TABLE club.academy_training_delivery_revisions ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON club.academy_training_delivery_revisions FROM PUBLIC;
      CREATE FUNCTION club.training_delivery_view(p club.academy_training_plan_drafts)
      RETURNS jsonb LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $delivery_view$
        SELECT club.training_drill_plan_view(p)||jsonb_build_object('sequence_version',coalesce(r.sequence_version,0),
          'drills',coalesce(r.drills,p.drill_snapshots))
        FROM (SELECT 1) seed LEFT JOIN LATERAL
          (SELECT sequence_version,drills FROM club.academy_training_delivery_revisions WHERE plan_id=p.id
           ORDER BY sequence_version DESC LIMIT 1) r ON true
      $delivery_view$;
      CREATE FUNCTION club.read_admin_training_delivery(_digest text,_group text,_plan uuid,_schedule integer)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $delivery_read$
      DECLARE v_plan club.academy_training_plan_drafts%ROWTYPE;
      BEGIN
        PERFORM club.require_training_draft_actor(_digest,'lefteria_fc_academy',_group);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts WHERE id=_plan AND group_id=_group
          AND organization_id='lefteria_fc_academy' AND version=_schedule FOR SHARE;
        IF NOT FOUND THEN RAISE EXCEPTION 'training_delivery_schedule_conflict' USING ERRCODE='23505'; END IF;
        RETURN club.training_delivery_view(v_plan);
      END $delivery_read$;

      CREATE FUNCTION club.write_admin_training_delivery(_digest text,_group text,_plan uuid,_schedule integer,_expected integer,_items jsonb)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $delivery_write$
      DECLARE v_actor uuid;v_session uuid;v_plan club.academy_training_plan_drafts%ROWTYPE;v_current jsonb;
        v_item jsonb;v_snapshot jsonb;v_result jsonb:='[]'::jsonb;v_ids uuid[]:=ARRAY[]::uuid[];
        v_id uuid;v_version integer;v_minutes integer;v_total integer:=0;
        v_drill club.academy_coaching_drills%ROWTYPE;
      BEGIN
        SELECT actor_id,session_id INTO v_actor,v_session FROM club.require_training_draft_actor(_digest,'lefteria_fc_academy',_group);
        IF _plan IS NULL OR _schedule IS NULL OR _schedule NOT BETWEEN 1 AND 10000 OR _expected IS NULL OR _expected NOT BETWEEN 0 AND 9999
          OR jsonb_typeof(_items) IS DISTINCT FROM 'array' THEN RAISE EXCEPTION 'training_delivery_input_invalid' USING ERRCODE='22023'; END IF;
        IF jsonb_array_length(_items)>24 THEN RAISE EXCEPTION 'training_drill_limit' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts WHERE id=_plan AND group_id=_group
          AND organization_id='lefteria_fc_academy' FOR UPDATE;
        IF NOT FOUND OR v_plan.version<>_schedule OR v_plan.status<>'draft'
          OR v_plan.training_day<(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
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

        INSERT INTO club.academy_training_delivery_revisions(plan_id,sequence_version,schedule_version,actor_id,drills)
          VALUES(_plan,_expected+1,_schedule,v_actor,v_result);
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.training.delivery.updated','succeeded','lefteria_fc_academy','private_coaching_sequence_no_publication');
        RETURN club.training_drill_plan_view(v_plan)||jsonb_build_object('sequence_version',_expected+1,'drills',v_result);
      END $delivery_write$;
      CREATE FUNCTION club.guard_training_delivery_duration()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $delivery_guard$
      DECLARE v_total integer;
      BEGIN
        SELECT coalesce(sum((x->>'duration_minutes')::integer),0)::integer INTO v_total
        FROM jsonb_array_elements(coalesce((SELECT drills FROM club.academy_training_delivery_revisions WHERE plan_id=NEW.id
          ORDER BY sequence_version DESC LIMIT 1),'[]'::jsonb)) x;
        IF v_total>NEW.duration_minutes THEN RAISE EXCEPTION 'training_delivery_exceeds_schedule' USING ERRCODE='22023'; END IF;
        RETURN NEW;
      END $delivery_guard$;
      CREATE TRIGGER academy_training_delivery_duration BEFORE UPDATE OF duration_minutes ON club.academy_training_plan_drafts
        FOR EACH ROW EXECUTE FUNCTION club.guard_training_delivery_duration();
      REVOKE ALL ON FUNCTION club.training_delivery_view(club.academy_training_plan_drafts),club.read_admin_training_delivery(text,text,uuid,integer),
        club.write_admin_training_delivery(text,text,uuid,integer,integer,jsonb),club.guard_training_delivery_duration() FROM PUBLIC;

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
          'status',p.status,'version',p.version,'drill_count',jsonb_array_length(p.drill_snapshots),'delivery_drill_count',jsonb_array_length(club.training_delivery_view(p)->'drills'),'submitted_version',CASE WHEN EXISTS(SELECT 1 FROM club.academy_training_plan_submissions sub WHERE sub.plan_id=p.id AND sub.plan_version=p.version) THEN p.version ELSE NULL END)
          ORDER BY p.training_day,p.start_time,p.id),'[]'::jsonb)
        INTO v_rows FROM club.academy_training_plan_drafts p
          WHERE p.organization_id=_owner AND p.group_id=_group
            AND p.training_day BETWEEN _from AND _to;
        RETURN jsonb_build_object('organization_id',_owner,
          'group_id',_group,'from',_from,'to',_to,
          'total',v_total,'plans',v_rows);
      END $plan_read$;
      CREATE OR REPLACE FUNCTION club.require_training_drill_review_rollout()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $approval$
      DECLARE v_drills jsonb;v_plan club.academy_training_plan_drafts%ROWTYPE;
      BEGIN
        SELECT * INTO STRICT v_plan FROM club.academy_training_plan_drafts
          WHERE id=NEW.plan_id AND version=NEW.plan_version FOR SHARE;
        v_drills:=v_plan.drill_snapshots;
        IF NEW.plan_snapshot->>'review_kind'='schedule_only' AND NOT (NEW.plan_snapshot ?| ARRAY['drill_snapshots','notes','member_summary']) THEN RETURN NEW; END IF;
        IF jsonb_array_length(v_drills)>0 AND (
          NEW.plan_snapshot->>'review_kind' IS DISTINCT FROM 'complete_drill_plan'
          OR NEW.plan_snapshot->'drill_snapshots' IS DISTINCT FROM v_drills
          OR jsonb_typeof(NEW.plan_snapshot->'member_summary') IS DISTINCT FROM 'string'
          OR coalesce(length(NEW.plan_snapshot->>'member_summary'),0) NOT BETWEEN 1 AND 1200
        ) THEN RAISE EXCEPTION 'training_drill_review_not_enabled' USING ERRCODE='55000'; END IF;
        RETURN NEW;
      END $approval$;
      CREATE OR REPLACE FUNCTION club.approve_admin_academy_training_plan(
        _digest text,_owner text,_plan uuid,_expected integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_plan_approve$
      DECLARE
        v_actor uuid; v_session uuid;
        v_plan club.academy_training_plan_drafts%ROWTYPE;v_group text;
        v_snapshot jsonb;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL OR
          _expected NOT BETWEEN 1 AND 10000 THEN
          RAISE EXCEPTION 'invalid_training_plan_approval' USING ERRCODE='22023';
        END IF;
        SELECT actor_id,session_id INTO v_actor,v_session
          FROM club.require_academy_training_planner(_digest,_owner);
        -- Resolve the exact group, lock its live territory authority, then
        -- lock the plan. All native plan paths share this lock order.
        SELECT group_id INTO v_group FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner;
        IF NOT FOUND THEN RAISE EXCEPTION 'training_plan_scope_denied' USING ERRCODE='42501'; END IF;
        PERFORM club.require_training_drill_group(v_actor,v_group);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner AND group_id=v_group FOR UPDATE;
        IF NOT FOUND OR v_plan.status<>'draft'
          OR v_plan.version<>_expected THEN
          RAISE EXCEPTION 'training_plan_approval_stale'
            USING ERRCODE='23505';
        END IF;
        IF v_plan.training_day <
          (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN
          RAISE EXCEPTION 'training_plan_approval_in_past'
            USING ERRCODE='22023';
        END IF;
        SELECT (to_jsonb(p) - 'created_by' - 'updated_by' - 'created_at' - 'updated_at' - 'drill_snapshots' - 'notes')||jsonb_build_object('review_kind','schedule_only')
          INTO v_snapshot FROM club.academy_training_plan_drafts p
          WHERE p.id=_plan;
        INSERT INTO club.academy_training_plan_approvals(
          id,plan_id,plan_version,approved_by,plan_snapshot)
        VALUES(gen_random_uuid(),_plan,_expected,v_actor,v_snapshot);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,
          device_session_id,action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.training.plan.approved','succeeded',_owner,
          'human_approval_not_published');
        RETURN jsonb_build_object('id',_plan,'organization_id',_owner,
          'approved_version',_expected,'approval_status','approved',
          'published',false);
      END $academy_plan_approve$;
      CREATE OR REPLACE FUNCTION club.preview_admin_academy_training_distribution(
        _digest text,_owner text,_plan uuid,_expected integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $training_distribution_preview$
      DECLARE
        v_plan club.academy_training_plan_drafts%ROWTYPE;v_group text;
        v_eligible integer;v_actor uuid;v_member_summary text;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL
          OR _expected NOT BETWEEN 1 AND 10000 THEN
          RAISE EXCEPTION 'training_distribution_scope_invalid'
            USING ERRCODE='22023';
        END IF;
        SELECT actor_id INTO v_actor FROM club.require_academy_training_planner(_digest,_owner);
        -- Resolve the exact group, lock its live territory authority, then
        -- lock the plan. All native plan paths share this lock order.
        SELECT group_id INTO v_group FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner;
        IF NOT FOUND THEN RAISE EXCEPTION 'training_plan_scope_denied' USING ERRCODE='42501'; END IF;
        PERFORM club.require_training_drill_group(v_actor,v_group);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner AND group_id=v_group FOR SHARE;
        IF NOT FOUND OR v_plan.version<>_expected OR
          v_plan.status<>'draft' OR NOT EXISTS(
            SELECT 1 FROM club.academy_training_plan_approvals a
            WHERE a.plan_id=v_plan.id AND a.plan_version=v_plan.version
          ) THEN
          RAISE EXCEPTION 'training_distribution_unapproved'
            USING ERRCODE='23505';
        END IF;
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_approvals a WHERE a.plan_id=v_plan.id AND a.plan_version=v_plan.version AND a.plan_snapshot->>'review_kind'='complete_drill_plan') THEN
          SELECT a.plan_snapshot->>'member_summary' INTO v_member_summary
          FROM club.academy_training_plan_approvals a
          WHERE a.plan_id=v_plan.id AND a.plan_version=v_plan.version
            AND a.plan_snapshot->>'review_kind'='complete_drill_plan'
            AND a.plan_snapshot->'drill_snapshots'=v_plan.drill_snapshots
            AND jsonb_typeof(a.plan_snapshot->'member_summary')='string';
          IF v_member_summary IS NULL OR length(v_member_summary) NOT BETWEEN 1 AND 1200
          THEN RAISE EXCEPTION 'training_complete_review_required' USING ERRCODE='23505'; END IF;
        END IF;
        IF v_plan.training_day <
          (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN
          RAISE EXCEPTION 'training_distribution_date_in_past'
            USING ERRCODE='22023';
        END IF;
        -- Current ACTIVE roster only, with exact organization and group
        -- scope. Counts are aggregates; no player IDs or contacts leave SQL.
        SELECT count(*) INTO v_eligible FROM club.players p
          WHERE p.organization_id=_owner AND p.is_active
            AND NOT p.is_archived AND NOT p.is_transferred
            AND p.team_type='Academy'
            AND (
              p.primary_academy_group_id=v_plan.group_id
              OR EXISTS(SELECT 1 FROM club.player_academy_groups m
                WHERE m.organization_id=_owner
                  AND m.player_id=p.id
                  AND m.academy_group_id=v_plan.group_id)
            );
        RETURN jsonb_build_object(
          'id',v_plan.id,'organization_id',v_plan.organization_id,
          'group_id',v_plan.group_id,'version',v_plan.version,
          'day',v_plan.training_day,
          'start_time',to_char(v_plan.start_time,'HH24:MI'),
          'duration_minutes',v_plan.duration_minutes,
          'title',v_plan.title,'location',v_plan.location,
          'eligible_player_count',v_eligible,
          'human_approved',true,'published',false
        )||CASE WHEN v_member_summary IS NULL THEN '{}'::jsonb
          ELSE jsonb_build_object('member_summary',v_member_summary) END;
      END $training_distribution_preview$;
      CREATE OR REPLACE FUNCTION club.publish_admin_academy_training_plan(
        _digest text,_owner text,_plan uuid,_expected integer,
        _confirm boolean
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $os_publish_training$
      DECLARE
        v_actor uuid;v_session uuid;v_plan club.academy_training_plan_drafts%ROWTYPE;v_group text;
        v_digest text;v_source integer;v_event text;v_projection jsonb;
        v_active integer;v_member_summary text;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL
          OR _expected NOT BETWEEN 1 AND 10000 OR _confirm IS DISTINCT FROM true
        THEN RAISE EXCEPTION 'training_publish_confirmation_required'
          USING ERRCODE='22023'; END IF;
        SELECT actor_id,session_id INTO v_actor,v_session
          FROM club.require_academy_training_planner(_digest,_owner);
        -- Resolve the exact group, lock its live territory authority, then
        -- lock the plan. All native plan paths share this lock order.
        SELECT group_id INTO v_group FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner;
        IF NOT FOUND THEN RAISE EXCEPTION 'training_plan_scope_denied' USING ERRCODE='42501'; END IF;
        PERFORM club.require_training_drill_group(v_actor,v_group);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner AND group_id=v_group FOR UPDATE;
        IF NOT FOUND OR v_plan.status<>'draft' OR v_plan.version<>_expected
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_publications
            WHERE plan_id=_plan)
          OR NOT EXISTS(SELECT 1 FROM club.academy_training_plan_approvals
            WHERE plan_id=_plan AND plan_version=_expected)
        THEN RAISE EXCEPTION 'training_publish_approval_conflict'
          USING ERRCODE='23505'; END IF;
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_approvals a WHERE a.plan_id=v_plan.id AND a.plan_version=v_plan.version AND a.plan_snapshot->>'review_kind'='complete_drill_plan') THEN
          SELECT a.plan_snapshot->>'member_summary' INTO v_member_summary
          FROM club.academy_training_plan_approvals a
          WHERE a.plan_id=v_plan.id AND a.plan_version=v_plan.version
            AND a.plan_snapshot->>'review_kind'='complete_drill_plan'
            AND a.plan_snapshot->'drill_snapshots'=v_plan.drill_snapshots
            AND jsonb_typeof(a.plan_snapshot->'member_summary')='string';
          IF v_member_summary IS NULL OR length(v_member_summary) NOT BETWEEN 1 AND 1200
          THEN RAISE EXCEPTION 'training_complete_review_required' USING ERRCODE='23505'; END IF;
        END IF;
        IF v_plan.training_day <
          (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
          OR NOT EXISTS(SELECT 1 FROM club.calendar_import_state
            WHERE id='initial' AND is_complete)
        THEN RAISE EXCEPTION 'training_publish_not_ready'
          USING ERRCODE='22023'; END IF;
        SELECT count(*) INTO v_active FROM club.players p
          WHERE p.organization_id=_owner AND p.is_active AND NOT p.is_archived
            AND NOT p.is_transferred AND p.team_type='Academy'
            AND (p.primary_academy_group_id=v_plan.group_id OR
              EXISTS(SELECT 1 FROM club.player_academy_groups m
                WHERE m.organization_id=_owner AND m.player_id=p.id
                  AND m.academy_group_id=v_plan.group_id));
        IF v_active=0 THEN RAISE EXCEPTION 'training_publish_no_recipients'
          USING ERRCODE='22023'; END IF;
        SELECT digest INTO v_digest FROM club.calendar_import_state
          WHERE id='initial' AND is_complete FOR SHARE;
        v_event:='os-training-'||_plan::text;
        v_source:=nextval('club.os_training_source_ordinal');
        -- Canonical projection is intentionally public-safe: private notes
        -- and youth roster data never enter a mobile-visible projection.
        v_projection:=jsonb_build_object(
          'day',v_plan.training_day,'end_day',v_plan.training_day,
          'source_date',v_plan.training_day::text,
          'source_end_date',v_plan.training_day::text,
          'local_time',to_char(v_plan.start_time,'HH24:MI'),
          'starts_at',NULL,'ends_at',NULL,
          'title',v_plan.title,'location',v_plan.location,
          'location_url',NULL,'arrival_time',NULL,
          'duration_minutes',v_plan.duration_minutes,
          'summary',v_member_summary,'journey',NULL,'preparation',NULL,
          'description',NULL);
        INSERT INTO migration.calendar_source_documents(
          collection,ordinal,import_digest,disposition,target_kind,
          target_id,source_json)
        VALUES('os_training',v_source,v_digest,'item','training',v_event,
          json_build_object('origin','os_native','plan_id',_plan,
            'version',_expected,'approved',true)::text);
        INSERT INTO club.calendar_items(kind,id,organization_id,
          source_collection,source_ordinal,event_type,status,
          is_cancelled,is_archived,cancel_reason,content_status,
          staff_day,staff_end_day,member_day,member_end_day,
          staff_projection_json,member_projection_json)
        VALUES('training',v_event,_owner,'os_training',v_source,
          'academy_training','published',false,false,NULL,'published',
          v_plan.training_day,v_plan.training_day,
          v_plan.training_day,v_plan.training_day,v_projection,v_projection);
        INSERT INTO club.calendar_item_scopes(kind,event_id,scope_kind,
          record_id,organization_id,team_id,academy_group_id)
        VALUES('training',v_event,'group',v_plan.group_id,_owner,NULL,
          v_plan.group_id);
        INSERT INTO club.academy_training_plan_publications(
          plan_id,plan_version,event_id,source_ordinal,organization_id,
          group_id,published_by)
        VALUES(_plan,_expected,v_event,v_source,_owner,v_plan.group_id,v_actor);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          v_session,'admin.training.plan.published','succeeded',_owner,
          'exact_human_approved_version');
        RETURN jsonb_build_object('id',_plan,'organization_id',_owner,
          'event_id',v_event,'event_type','training','published_version',
          _expected,'published',true,'notifications_sent',false);
      END $os_publish_training$;
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
      """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Preserve coaching revisions and proposals using forward recovery.");
}
