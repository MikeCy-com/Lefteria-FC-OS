using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Human-approved, group-scoped OS-native training publication. Historical
/// imported source rows are never edited; every native event retains a
/// separate provenance document. No notifications are sent by this migration.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113120000_AcademyTrainingCanonicalPublication")]
public sealed class AcademyTrainingCanonicalPublication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE migration.calendar_source_documents
        DROP CONSTRAINT ck_calendar_source_collection;
      ALTER TABLE migration.calendar_source_documents
        ADD CONSTRAINT ck_calendar_source_collection
        CHECK (collection IN ('fixtures','events','training_sessions',
          'availability','event_attendance','attendance','os_training')
          AND ordinal>=0);
      ALTER TABLE club.calendar_items DROP CONSTRAINT ck_calendar_item_origin;
      ALTER TABLE club.calendar_items ADD CONSTRAINT ck_calendar_item_origin
        CHECK ((kind='match' AND source_collection='fixtures')
          OR (kind='event' AND source_collection='events')
          OR (kind='training' AND source_collection IN
            ('training_sessions','os_training')));
      CREATE SEQUENCE club.os_training_source_ordinal AS integer START WITH 1;
      REVOKE ALL ON SEQUENCE club.os_training_source_ordinal FROM PUBLIC;
      CREATE TABLE club.academy_training_plan_publications(
        plan_id uuid PRIMARY KEY REFERENCES club.academy_training_plan_drafts(id)
          ON DELETE RESTRICT,
        plan_version integer NOT NULL CHECK(plan_version BETWEEN 1 AND 10000),
        event_id varchar(128) COLLATE "C" NOT NULL UNIQUE,
        event_kind varchar(16) NOT NULL DEFAULT 'training'
          CHECK(event_kind='training'),
        source_ordinal integer NOT NULL UNIQUE CHECK(source_ordinal>0),
        organization_id varchar(32) NOT NULL
          CHECK(organization_id='lefteria_fc_academy'),
        group_id varchar(128) COLLATE "C" NOT NULL,
        published_by uuid NOT NULL REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        published_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY(organization_id,group_id)
          REFERENCES club.academy_groups(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,event_kind,event_id)
          REFERENCES club.calendar_items(organization_id,kind,id)
          ON DELETE RESTRICT
      );
      REVOKE ALL ON club.academy_training_plan_publications FROM PUBLIC;
      ALTER TABLE club.academy_training_plan_publications ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION club.prevent_published_academy_plan_edits()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $publication_immutable$
      BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_publications
          WHERE plan_id=OLD.id) THEN
          RAISE EXCEPTION 'published_training_plan_immutable'
            USING ERRCODE='23505';
        END IF;
        RETURN NEW;
      END $publication_immutable$;
      REVOKE ALL ON FUNCTION club.prevent_published_academy_plan_edits()
        FROM PUBLIC;
      CREATE TRIGGER academy_published_training_plan_immutable
        BEFORE UPDATE OR DELETE ON club.academy_training_plan_drafts
        FOR EACH ROW EXECUTE FUNCTION
          club.prevent_published_academy_plan_edits();

      CREATE FUNCTION club.publish_admin_academy_training_plan(
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
      REVOKE ALL ON FUNCTION club.publish_admin_academy_training_plan(
        text,text,uuid,integer,boolean) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
      migrationBuilder.Sql("""
        DO $rollback_guard$ BEGIN
          IF EXISTS(SELECT 1 FROM club.academy_training_plan_publications)
          THEN RAISE EXCEPTION 'os_training_published_data_exists'; END IF;
        END $rollback_guard$;
        DROP FUNCTION club.publish_admin_academy_training_plan(
          text,text,uuid,integer,boolean);
        DROP TRIGGER academy_published_training_plan_immutable
          ON club.academy_training_plan_drafts;
        DROP FUNCTION club.prevent_published_academy_plan_edits();
        DROP TABLE club.academy_training_plan_publications;
        DROP SEQUENCE club.os_training_source_ordinal;
        ALTER TABLE club.calendar_items DROP CONSTRAINT ck_calendar_item_origin;
        ALTER TABLE club.calendar_items ADD CONSTRAINT ck_calendar_item_origin
          CHECK ((kind='match' AND source_collection='fixtures')
            OR (kind='event' AND source_collection='events')
            OR (kind='training' AND source_collection='training_sessions'));
        ALTER TABLE migration.calendar_source_documents
          DROP CONSTRAINT ck_calendar_source_collection;
        ALTER TABLE migration.calendar_source_documents
          ADD CONSTRAINT ck_calendar_source_collection CHECK(
            collection IN ('fixtures','events','training_sessions',
              'availability','event_attendance','attendance') AND ordinal>=0);
        """);
}
