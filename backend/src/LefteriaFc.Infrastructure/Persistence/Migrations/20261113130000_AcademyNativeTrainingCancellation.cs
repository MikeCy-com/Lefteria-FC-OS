using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Audited cancellation of OS-native published training only. Retains the
/// published event, existing RSVP/attendance histories and child-safe
/// projection; makes new availability writes ineligible by is_cancelled.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113130000_AcademyNativeTrainingCancellation")]
public sealed class AcademyNativeTrainingCancellation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.academy_training_publication_cancellations(
        plan_id uuid PRIMARY KEY REFERENCES club.academy_training_plan_publications(plan_id)
          ON DELETE RESTRICT,
        plan_version integer NOT NULL CHECK(plan_version BETWEEN 1 AND 10000),
        event_id varchar(128) COLLATE "C" NOT NULL,
        organization_id varchar(32) NOT NULL CHECK(organization_id='lefteria_fc_academy'),
        reason varchar(500) NOT NULL
          CHECK(length(reason) BETWEEN 8 AND 500 AND reason=btrim(reason)
            AND reason !~ '[[:cntrl:]]'),
        cancelled_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        cancelled_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );
      REVOKE ALL ON club.academy_training_publication_cancellations FROM PUBLIC;
      ALTER TABLE club.academy_training_publication_cancellations
        ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION club.cancel_admin_academy_training_publication(
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
      REVOKE ALL ON FUNCTION club.cancel_admin_academy_training_publication(
        text,text,uuid,integer,text,boolean) FROM PUBLIC;

      -- Staff/guardian/member calendars must still SHOW cancelled events
      -- to primary-group members. RSVP mutations already reject cancelled
      -- events using NOT c.is_cancelled in their current single SQL statement.
      CREATE OR REPLACE FUNCTION club.os_native_training_primary_member(
        _owner text,_event text,_player text
      ) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $os_native_primary$
        SELECT EXISTS(SELECT 1 FROM club.calendar_items c
          JOIN club.academy_training_plan_publications pub
            ON pub.organization_id=c.organization_id
            AND pub.event_id=c.id AND pub.event_kind=c.kind
          JOIN club.calendar_item_scopes sc
            ON sc.organization_id=c.organization_id
            AND sc.kind=c.kind AND sc.event_id=c.id AND sc.scope_kind='group'
          JOIN club.players player
            ON player.id=_player AND player.organization_id=c.organization_id
          WHERE _owner='lefteria_fc_academy'
            AND c.organization_id=_owner AND c.kind='training'
            AND c.source_collection='os_training' AND c.id=_event
            AND NOT c.is_archived AND c.member_projection_json IS NOT NULL
            AND player.is_active AND NOT player.is_archived
            AND NOT player.is_transferred AND player.team_type='Academy'
            AND player.primary_academy_group_id=sc.record_id)
      $os_native_primary$;

      CREATE OR REPLACE FUNCTION club.read_admin_academy_training_publications(
        _digest text,_owner text,_group text,_from date,_to date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $native_training_pub_list$
      DECLARE v_count integer;v_rows jsonb;
      BEGIN
        IF _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
          OR _group ~ '[[:cntrl:]]' OR _from IS NULL OR _to IS NULL
          OR _to<_from OR _to>_from+93 THEN
          RAISE EXCEPTION 'training_publication_filters_invalid'
            USING ERRCODE='22023'; END IF;
        PERFORM club.require_academy_training_planner(_digest,_owner);
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
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
      migrationBuilder.Sql("""
        DO $cancel_rollback_guard$ BEGIN
          IF EXISTS(SELECT 1 FROM club.academy_training_publication_cancellations)
          THEN RAISE EXCEPTION 'published_training_cancellation_history_exists'; END IF;
        END $cancel_rollback_guard$;
        DROP FUNCTION club.cancel_admin_academy_training_publication(
          text,text,uuid,integer,text,boolean);
        DROP TABLE club.academy_training_publication_cancellations;
        CREATE OR REPLACE FUNCTION club.os_native_training_primary_member(
          _owner text,_event text,_player text
        ) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $os_native_primary$
          SELECT EXISTS(SELECT 1 FROM club.calendar_items c
            JOIN club.academy_training_plan_publications pub
              ON pub.organization_id=c.organization_id
              AND pub.event_id=c.id AND pub.event_kind=c.kind
            JOIN club.calendar_item_scopes sc
              ON sc.organization_id=c.organization_id
              AND sc.kind=c.kind AND sc.event_id=c.id AND sc.scope_kind='group'
            JOIN club.players player
              ON player.id=_player AND player.organization_id=c.organization_id
            WHERE _owner='lefteria_fc_academy'
              AND c.organization_id=_owner AND c.kind='training'
              AND c.source_collection='os_training' AND c.id=_event
              AND NOT c.is_archived AND NOT c.is_cancelled
              AND c.member_projection_json IS NOT NULL
              AND player.is_active AND NOT player.is_archived
              AND NOT player.is_transferred AND player.team_type='Academy'
              AND player.primary_academy_group_id=sc.record_id)
        $os_native_primary$;
        CREATE OR REPLACE FUNCTION club.read_admin_academy_training_publications(
          _digest text,_owner text,_group text,_from date,_to date
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $native_training_pub_list$
        DECLARE v_count integer;v_rows jsonb;
        BEGIN
          IF _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _group ~ '[[:cntrl:]]' OR _from IS NULL OR _to IS NULL
            OR _to<_from OR _to>_from+93 THEN
            RAISE EXCEPTION 'training_publication_filters_invalid'
              USING ERRCODE='22023'; END IF;
          PERFORM club.require_academy_training_planner(_digest,_owner);
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
            'published_version',pub.plan_version)
            ORDER BY plan.training_day,pub.plan_id),'[]'::jsonb)
          INTO v_rows FROM club.academy_training_plan_publications pub
          JOIN club.academy_training_plan_drafts plan ON plan.id=pub.plan_id
          WHERE pub.organization_id=_owner AND pub.group_id=_group
            AND plan.training_day BETWEEN _from AND _to;
          RETURN jsonb_build_object('organization_id',_owner,
            'group_id',_group,'from',_from,'to',_to,
            'publications',v_rows);
        END $native_training_pub_list$;
        """);
}
