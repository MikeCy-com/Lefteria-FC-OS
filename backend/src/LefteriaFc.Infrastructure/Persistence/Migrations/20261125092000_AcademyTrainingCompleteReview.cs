using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125092000_AcademyTrainingCompleteReview")]
public sealed class AcademyTrainingCompleteReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION club.require_training_drill_review_rollout()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $approval$
      DECLARE v_drills jsonb;v_plan club.academy_training_plan_drafts%ROWTYPE;
      BEGIN
        SELECT * INTO STRICT v_plan FROM club.academy_training_plan_drafts
          WHERE id=NEW.plan_id AND version=NEW.plan_version FOR SHARE;
        v_drills:=v_plan.drill_snapshots;
        IF jsonb_array_length(v_drills)>0 AND (
          NEW.plan_snapshot->>'review_kind' IS DISTINCT FROM 'complete_drill_plan'
          OR NEW.plan_snapshot->'drill_snapshots' IS DISTINCT FROM v_drills
          OR jsonb_typeof(NEW.plan_snapshot->'member_summary') IS DISTINCT FROM 'string'
          OR coalesce(length(NEW.plan_snapshot->>'member_summary'),0) NOT BETWEEN 1 AND 1200
        ) THEN RAISE EXCEPTION 'training_drill_review_not_enabled' USING ERRCODE='55000'; END IF;
        RETURN NEW;
      END $approval$;
      CREATE FUNCTION club.approve_admin_training_drill_plan(_digest text,_group text,_plan uuid,_expected integer,_member_summary text,_confirm boolean)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $complete_review$
      DECLARE v_actor uuid;v_session uuid;v_plan club.academy_training_plan_drafts%ROWTYPE;v_snapshot jsonb;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL OR _expected NOT BETWEEN 1 AND 10000
          OR _confirm IS DISTINCT FROM true OR _member_summary IS NULL
          OR length(_member_summary) NOT BETWEEN 1 AND 1200
          OR _member_summary<>btrim(_member_summary,E' \\n')
          OR replace(_member_summary,chr(10),'') ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'training_complete_review_invalid' USING ERRCODE='22023'; END IF;
        SELECT actor_id,session_id INTO v_actor,v_session
          FROM club.require_academy_training_planner(_digest,'lefteria_fc_academy');
        PERFORM club.require_training_drill_group(v_actor,_group);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id='lefteria_fc_academy' AND group_id=_group FOR UPDATE;
        IF NOT FOUND OR v_plan.status<>'draft' OR v_plan.version<>_expected
          OR jsonb_array_length(v_plan.drill_snapshots)=0
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_publications WHERE plan_id=_plan)
        THEN RAISE EXCEPTION 'training_complete_review_conflict' USING ERRCODE='23505'; END IF;
        IF v_plan.training_day<(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date
        THEN RAISE EXCEPTION 'training_complete_review_in_past' USING ERRCODE='22023'; END IF;
        -- Human explicitly reviews the frozen staff sequence and separately
        -- writes a member-safe summary. Never copy notes or drill instructions.
        v_snapshot:=(to_jsonb(v_plan)-'created_by'-'updated_by'-'created_at'-'updated_at')
          ||jsonb_build_object('review_kind','complete_drill_plan','member_summary',_member_summary);
        INSERT INTO club.academy_training_plan_approvals(id,plan_id,plan_version,approved_by,plan_snapshot)
          VALUES(gen_random_uuid(),_plan,_expected,v_actor,v_snapshot);
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.training.plan.drills.approved','succeeded',
            'lefteria_fc_academy','complete_human_review_not_published');
        RETURN jsonb_build_object('id',_plan,'organization_id','lefteria_fc_academy','group_id',_group,
          'approved_version',_expected,'approval_status','approved','published',false,'member_summary',_member_summary);
      END $complete_review$;
      REVOKE ALL ON FUNCTION club.approve_admin_training_drill_plan(text,text,uuid,integer,text,boolean) FROM PUBLIC;
      CREATE OR REPLACE FUNCTION club.preview_admin_academy_training_distribution(
        _digest text,_owner text,_plan uuid,_expected integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $training_distribution_preview$
      DECLARE
        v_plan club.academy_training_plan_drafts%ROWTYPE;
        v_eligible integer;v_actor uuid;v_member_summary text;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL
          OR _expected NOT BETWEEN 1 AND 10000 THEN
          RAISE EXCEPTION 'training_distribution_scope_invalid'
            USING ERRCODE='22023';
        END IF;
        SELECT actor_id INTO v_actor FROM club.require_academy_training_planner(_digest,_owner);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner FOR SHARE;
        IF NOT FOUND OR v_plan.version<>_expected OR
          v_plan.status<>'draft' OR NOT EXISTS(
            SELECT 1 FROM club.academy_training_plan_approvals a
            WHERE a.plan_id=v_plan.id AND a.plan_version=v_plan.version
          ) THEN
          RAISE EXCEPTION 'training_distribution_unapproved'
            USING ERRCODE='23505';
        END IF;
        IF jsonb_array_length(v_plan.drill_snapshots)>0 THEN
          PERFORM club.require_training_drill_group(v_actor,v_plan.group_id);
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
        v_actor uuid;v_session uuid;v_plan club.academy_training_plan_drafts%ROWTYPE;
        v_digest text;v_source integer;v_event text;v_projection jsonb;
        v_active integer;v_member_summary text;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL
          OR _expected NOT BETWEEN 1 AND 10000 OR _confirm IS DISTINCT FROM true
        THEN RAISE EXCEPTION 'training_publish_confirmation_required'
          USING ERRCODE='22023'; END IF;
        SELECT actor_id,session_id INTO v_actor,v_session
          FROM club.require_academy_training_planner(_digest,_owner);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner FOR UPDATE;
        IF NOT FOUND OR v_plan.status<>'draft' OR v_plan.version<>_expected
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_publications
            WHERE plan_id=_plan)
          OR NOT EXISTS(SELECT 1 FROM club.academy_training_plan_approvals
            WHERE plan_id=_plan AND plan_version=_expected)
        THEN RAISE EXCEPTION 'training_publish_approval_conflict'
          USING ERRCODE='23505'; END IF;
        IF jsonb_array_length(v_plan.drill_snapshots)>0 THEN
          PERFORM club.require_training_drill_group(v_actor,v_plan.group_id);
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
      CREATE OR REPLACE FUNCTION club.cancel_admin_academy_training_publication(
        _digest text,_owner text,_plan uuid,_expected integer,
        _reason text,_confirm boolean
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $cancel_native_training$
      DECLARE
        v_actor uuid;v_session uuid;
        v_plan club.academy_training_plan_drafts%ROWTYPE;
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
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner FOR UPDATE;
        IF NOT FOUND OR v_plan.version<>_expected OR
          v_plan.training_day <
          (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN
          RAISE EXCEPTION 'training_cancel_stale'
            USING ERRCODE='23505'; END IF;
        IF jsonb_array_length(v_plan.drill_snapshots)>0 THEN
          PERFORM club.require_training_drill_group(v_actor,v_plan.group_id);
        END IF;
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

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $protect$ BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_approvals WHERE plan_snapshot->>'review_kind'='complete_drill_plan')
          THEN RAISE EXCEPTION 'complete_training_review_requires_forward_recovery'; END IF;
      END $protect$;
      CREATE OR REPLACE FUNCTION club.cancel_admin_academy_training_publication(
        _digest text,_owner text,_plan uuid,_expected integer,
        _reason text,_confirm boolean
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $cancel_native_training$
      DECLARE
        v_actor uuid;v_session uuid;
        v_plan club.academy_training_plan_drafts%ROWTYPE;
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
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner FOR UPDATE;
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
      CREATE OR REPLACE FUNCTION club.publish_admin_academy_training_plan(
        _digest text,_owner text,_plan uuid,_expected integer,
        _confirm boolean
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $os_publish_training$
      DECLARE
        v_actor uuid;v_session uuid;v_plan club.academy_training_plan_drafts%ROWTYPE;
        v_digest text;v_source integer;v_event text;v_projection jsonb;
        v_active integer;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL
          OR _expected NOT BETWEEN 1 AND 10000 OR _confirm IS DISTINCT FROM true
        THEN RAISE EXCEPTION 'training_publish_confirmation_required'
          USING ERRCODE='22023'; END IF;
        SELECT actor_id,session_id INTO v_actor,v_session
          FROM club.require_academy_training_planner(_digest,_owner);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner FOR UPDATE;
        IF NOT FOUND OR v_plan.status<>'draft' OR v_plan.version<>_expected
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_publications
            WHERE plan_id=_plan)
          OR NOT EXISTS(SELECT 1 FROM club.academy_training_plan_approvals
            WHERE plan_id=_plan AND plan_version=_expected)
        THEN RAISE EXCEPTION 'training_publish_approval_conflict'
          USING ERRCODE='23505'; END IF;
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
          'summary',NULL,'journey',NULL,'preparation',NULL,
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
      CREATE OR REPLACE FUNCTION club.preview_admin_academy_training_distribution(
        _digest text,_owner text,_plan uuid,_expected integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $training_distribution_preview$
      DECLARE
        v_plan club.academy_training_plan_drafts%ROWTYPE;
        v_eligible integer;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL
          OR _expected NOT BETWEEN 1 AND 10000 THEN
          RAISE EXCEPTION 'training_distribution_scope_invalid'
            USING ERRCODE='22023';
        END IF;
        PERFORM club.require_academy_training_planner(_digest,_owner);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner FOR SHARE;
        IF NOT FOUND OR v_plan.version<>_expected OR
          v_plan.status<>'draft' OR NOT EXISTS(
            SELECT 1 FROM club.academy_training_plan_approvals a
            WHERE a.plan_id=v_plan.id AND a.plan_version=v_plan.version
          ) THEN
          RAISE EXCEPTION 'training_distribution_unapproved'
            USING ERRCODE='23505';
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
        );
      END $training_distribution_preview$;
      DROP FUNCTION club.approve_admin_training_drill_plan(text,text,uuid,integer,text,boolean);
      CREATE OR REPLACE FUNCTION club.require_training_drill_review_rollout()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $approval$
      BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_drafts
          WHERE id=NEW.plan_id AND version=NEW.plan_version AND jsonb_array_length(drill_snapshots)>0)
        THEN RAISE EXCEPTION 'training_drill_review_not_enabled' USING ERRCODE='55000'; END IF;
        RETURN NEW;
      END $approval$;
      """);
}
