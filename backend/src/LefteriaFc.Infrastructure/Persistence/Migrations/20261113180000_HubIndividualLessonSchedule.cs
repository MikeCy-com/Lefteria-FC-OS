using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Durable, weekly, per-person lesson release reminders. Only approved
/// education can create a notification and no queue item contains article text.
/// Worker uses a separate grant; disabled by default at the API host.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113180000_HubIndividualLessonSchedule")]
public sealed class HubIndividualLessonSchedule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          CREATE TABLE hub.learning_schedules (
            identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id),
            organization_id varchar(32) NOT NULL REFERENCES club.organizations(id),
            viewer_role varchar(8) NOT NULL CHECK(viewer_role IN ('parent','player')),
            audience varchar(8) NOT NULL CHECK(audience IN ('parent','player')),
            started_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            next_due_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            cadence_days integer NOT NULL DEFAULT 7 CHECK(cadence_days=7),
            cycle bigint NOT NULL DEFAULT 0 CHECK(cycle BETWEEN 0 AND 5200),
            enabled boolean NOT NULL DEFAULT true,
            PRIMARY KEY(identity_id,organization_id,viewer_role,audience),
            CHECK(viewer_role=audience)
          );
          CREATE INDEX ix_learning_schedules_due
            ON hub.learning_schedules(next_due_at,identity_id)
            WHERE enabled;
          CREATE TABLE hub.learning_delivery_inbox (
            id uuid PRIMARY KEY,
            identity_id uuid NOT NULL,
            organization_id varchar(32) NOT NULL,
            viewer_role varchar(8) NOT NULL,
            audience varchar(8) NOT NULL,
            cycle bigint NOT NULL CHECK(cycle BETWEEN 1 AND 5200),
            content_id uuid NOT NULL REFERENCES hub.content(id),
            revision_id uuid NOT NULL REFERENCES hub.revisions(id),
            queued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            opened_at timestamptz,
            FOREIGN KEY(identity_id,organization_id,viewer_role,audience)
              REFERENCES hub.learning_schedules(
                identity_id,organization_id,viewer_role,audience),
            UNIQUE(identity_id,organization_id,viewer_role,audience,cycle)
          );
          CREATE INDEX ix_learning_inbox_recipient
            ON hub.learning_delivery_inbox(identity_id,organization_id,queued_at DESC);
          ALTER TABLE hub.learning_schedules ENABLE ROW LEVEL SECURITY;
          ALTER TABLE hub.learning_delivery_inbox ENABLE ROW LEVEL SECURITY;
          REVOKE ALL ON hub.learning_schedules,hub.learning_delivery_inbox FROM PUBLIC;

          CREATE FUNCTION hub.start_mobile_lesson_schedule(
            _identity uuid,_phone text,_role text,_expires timestamptz,_owner text)
          RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $schedule_start$
          DECLARE v_lesson jsonb; v_started timestamptz; v_due timestamptz;
          BEGIN
            -- Performs current identity, roster/guardian and organization
            -- authorization before creating a schedule. No role from client.
            v_lesson:=hub.current_mobile_education_lesson(
              _identity,_phone,_role,_expires,_owner);
            IF _role NOT IN ('parent','player') THEN
              RAISE EXCEPTION 'invalid_learning_role' USING ERRCODE='42501';
            END IF;
            INSERT INTO hub.learning_schedules(
              identity_id,organization_id,viewer_role,audience)
              VALUES(_identity,_owner,_role,_role)
              ON CONFLICT DO NOTHING;
            SELECT started_at,next_due_at INTO v_started,v_due
              FROM hub.learning_schedules WHERE identity_id=_identity
                AND organization_id=_owner AND viewer_role=_role
                AND audience=_role AND enabled;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'schedule_disabled' USING ERRCODE='42501';
            END IF;
            RETURN jsonb_build_object('organization_id',_owner,
              'audience',_role,'started_at',v_started,'next_due_at',v_due,
              'cadence_days',7);
          END $schedule_start$;
          REVOKE ALL ON FUNCTION hub.start_mobile_lesson_schedule(
            uuid,text,text,timestamptz,text) FROM PUBLIC;

          CREATE FUNCTION hub.read_mobile_lesson_deliveries(
            _identity uuid,_phone text,_role text,_expires timestamptz,_owner text)
          RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $schedule_read$
          DECLARE v_page jsonb; v_item jsonb; v_items jsonb;
          BEGIN
            v_page:=hub.current_mobile_education_lesson(
              _identity,_phone,_role,_expires,_owner);
            v_item:=v_page->'next';
            -- Never expose material that is no longer the recipient's
            -- current approved step after withdrawal, role change or completion.
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',d.id,'queued_at',d.queued_at,'type','lesson_ready',
              'audience',d.audience,'content_id',d.content_id)
              ORDER BY d.queued_at DESC),'[]'::jsonb)
            INTO v_items FROM (
              SELECT d.* FROM hub.learning_delivery_inbox d
              WHERE d.identity_id=_identity AND d.organization_id=_owner
                AND d.viewer_role=_role AND d.audience=_role
                AND v_item IS NOT NULL AND v_item<>'null'::jsonb
                AND d.content_id=(v_item->>'id')::uuid
                AND d.revision_id=(v_item->>'revision_id')::uuid
              ORDER BY queued_at DESC LIMIT 10
            ) d;
            RETURN jsonb_build_object('organization_id',_owner,
              'audience',_role,'items',v_items);
          END $schedule_read$;
          REVOKE ALL ON FUNCTION hub.read_mobile_lesson_deliveries(
            uuid,text,text,timestamptz,text) FROM PUBLIC;

          CREATE FUNCTION hub.process_due_mobile_lesson_schedules(_limit integer)
          RETURNS integer LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $schedule_work$
          DECLARE item record; v_phone text; v_page jsonb; v_next jsonb;
            processed integer:=0; v_now timestamptz:=clock_timestamp();
          BEGIN
            IF _limit NOT BETWEEN 1 AND 50 THEN
              RAISE EXCEPTION 'invalid_schedule_batch' USING ERRCODE='22023';
            END IF;
            FOR item IN
              SELECT s.* FROM hub.learning_schedules s
              WHERE s.enabled AND s.next_due_at<=v_now AND s.cycle<5200
              ORDER BY s.next_due_at,s.identity_id
              LIMIT _limit FOR UPDATE SKIP LOCKED
            LOOP
              SELECT i.normalized_phone INTO v_phone
                FROM identity.mobile_identities i
                WHERE i.id=item.identity_id AND i.role=item.viewer_role
                  AND i.is_active;
              IF v_phone IS NULL THEN
                UPDATE hub.learning_schedules SET enabled=false
                  WHERE identity_id=item.identity_id
                    AND organization_id=item.organization_id
                    AND viewer_role=item.viewer_role
                    AND audience=item.audience;
                CONTINUE;
              END IF;
              BEGIN
                v_page:=hub.current_mobile_education_lesson(
                  item.identity_id,v_phone,item.viewer_role,
                  v_now+interval '1 minute',item.organization_id);
                v_next:=v_page->'next';
              EXCEPTION WHEN insufficient_privilege THEN
                -- Roster, guardian delegation, or role was revoked.
                UPDATE hub.learning_schedules SET enabled=false
                  WHERE identity_id=item.identity_id
                    AND organization_id=item.organization_id
                    AND viewer_role=item.viewer_role
                    AND audience=item.audience;
                CONTINUE;
              END;
              IF v_next IS NOT NULL AND v_next<>'null'::jsonb THEN
                INSERT INTO hub.learning_delivery_inbox(
                  id,identity_id,organization_id,viewer_role,audience,
                  cycle,content_id,revision_id)
                VALUES(gen_random_uuid(),item.identity_id,item.organization_id,
                  item.viewer_role,item.audience,item.cycle+1,
                  (v_next->>'id')::uuid,(v_next->>'revision_id')::uuid)
                ON CONFLICT DO NOTHING;
                UPDATE hub.learning_schedules
                  SET cycle=cycle+1,
                    next_due_at=greatest(next_due_at+interval '7 days',
                      v_now+interval '7 days')
                  WHERE identity_id=item.identity_id
                    AND organization_id=item.organization_id
                    AND viewer_role=item.viewer_role
                    AND audience=item.audience;
                processed:=processed+1;
              ELSE
                -- No newly approved eligible lesson; check again tomorrow,
                -- not once per process restart and never skip lessons.
                UPDATE hub.learning_schedules
                  SET next_due_at=v_now+interval '1 day'
                  WHERE identity_id=item.identity_id
                    AND organization_id=item.organization_id
                    AND viewer_role=item.viewer_role
                    AND audience=item.audience;
              END IF;
            END LOOP;
            RETURN processed;
          END $schedule_work$;
          REVOKE ALL ON FUNCTION hub.process_due_mobile_lesson_schedules(integer)
            FROM PUBLIC;
          """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.process_due_mobile_lesson_schedules(integer);
          DROP FUNCTION hub.read_mobile_lesson_deliveries(
            uuid,text,text,timestamptz,text);
          DROP FUNCTION hub.start_mobile_lesson_schedule(
            uuid,text,text,timestamptz,text);
          DROP TABLE hub.learning_delivery_inbox;
          DROP TABLE hub.learning_schedules;
          """);
}
