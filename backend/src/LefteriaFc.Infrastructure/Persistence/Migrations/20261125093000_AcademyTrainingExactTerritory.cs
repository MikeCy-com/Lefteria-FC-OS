using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Close organization-only authority on every native private training
/// plan lifecycle route. Unassigned/inactive locations require reconciliation,
/// including for Super Admin. Do not change imported calendar data.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125093000_AcademyTrainingExactTerritory")]
public sealed class AcademyTrainingExactTerritory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
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
        SELECT actor_id INTO v_actor FROM club.require_academy_training_planner(_digest,_owner);
        PERFORM club.require_training_drill_group(v_actor,_group);
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
          'status',p.status,'version',p.version,'drill_count',jsonb_array_length(p.drill_snapshots))
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
        v_actor uuid;v_session uuid;v_version integer;v_status text;
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
        SELECT actor_id,session_id INTO v_actor,v_session FROM
          club.require_academy_training_planner(_digest,_owner);
        PERFORM club.require_training_drill_group(v_actor,_group);
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
            WHERE id=_id AND organization_id=_owner FOR UPDATE;
          IF NOT FOUND OR v_current.group_id<>_group OR
            v_current.version<>_expected OR v_current.status<>'draft' THEN
            RAISE EXCEPTION 'training_plan_version_conflict' USING ERRCODE='23505';
          END IF;
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
        SELECT actor_id INTO v_actor FROM club.require_academy_training_planner(_digest,_owner);
        PERFORM club.require_training_drill_group(v_actor,_group);
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
        SELECT to_jsonb(p) - 'created_by' - 'updated_by' - 'created_at' - 'updated_at'
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
        IF jsonb_array_length(v_plan.drill_snapshots)>0 THEN
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
        IF jsonb_array_length(v_plan.drill_snapshots)>0 THEN
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
        SELECT actor_id INTO v_actor FROM club.require_academy_training_planner(_digest,_owner);
        PERFORM club.require_training_drill_group(v_actor,_group);
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

      CREATE OR REPLACE FUNCTION club.cancel_admin_academy_training_publication(
        _digest text,_owner text,_plan uuid,_expected integer,
        _reason text,_confirm boolean
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $cancel_native_training$
      DECLARE
        v_actor uuid;v_session uuid;
        v_plan club.academy_training_plan_drafts%ROWTYPE;v_group text;
        v_pub club.academy_training_plan_publications%ROWTYPE;
        v_count integer;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL
          OR _expected NOT BETWEEN 1 AND 10000
          OR _reason IS NULL OR length(_reason) NOT BETWEEN 8 AND 500
          OR _reason<>btrim(_reason) OR _reason ~ '[[:cntrl:]]'
          OR _confirm IS DISTINCT FROM true THEN
          RAISE EXCEPTION 'training_cancel_requires_reason_and_confirmation'
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
        IF NOT FOUND OR v_plan.version<>_expected OR
          v_plan.training_day <
          (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN
          RAISE EXCEPTION 'training_cancel_stale'
            USING ERRCODE='23505'; END IF;
        SELECT * INTO v_pub FROM club.academy_training_plan_publications
          WHERE plan_id=_plan AND organization_id=_owner
            AND plan_version=_expected FOR UPDATE;
        IF NOT FOUND OR EXISTS(SELECT 1
          FROM club.academy_training_publication_cancellations
          WHERE plan_id=_plan) THEN
          RAISE EXCEPTION 'training_cancel_publication_invalid'
            USING ERRCODE='23505'; END IF;
        UPDATE club.calendar_items SET
          is_cancelled=true,status='cancelled',content_status='cancelled',
          cancel_reason=_reason
        WHERE organization_id=_owner AND kind='training'
          AND id=v_pub.event_id AND source_collection='os_training'
          AND NOT is_archived AND NOT is_cancelled;
        GET DIAGNOSTICS v_count=ROW_COUNT;
        IF v_count<>1 THEN
          RAISE EXCEPTION 'training_cancel_calendar_conflict'
            USING ERRCODE='23505'; END IF;
        INSERT INTO club.academy_training_publication_cancellations(
          plan_id,plan_version,event_id,organization_id,reason,cancelled_by)
        VALUES(_plan,_expected,v_pub.event_id,_owner,_reason,v_actor);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          v_session,'admin.training.plan.cancelled','succeeded',_owner,
          'human_cancelled_native_training');
        RETURN jsonb_build_object('id',_plan,'organization_id',_owner,
          'event_id',v_pub.event_id,'published_version',_expected,
          'cancelled',true,'cancel_reason',_reason,
          'notifications_sent',false);
      END $cancel_native_training$;
      """);

    // A rollback must not reopen cross-location access. An audited forward
    // migration is required to change this security boundary.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Training territory security requires forward recovery.");
}
