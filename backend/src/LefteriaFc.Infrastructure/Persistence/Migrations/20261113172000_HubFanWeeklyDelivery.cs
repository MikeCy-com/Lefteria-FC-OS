using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Weekly personal fan delivery. Subscription entitlement is rechecked on
/// every worker cycle; queue events contain identifiers only, never article
/// bodies, player profiles, payment data, or child information.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113172000_HubFanWeeklyDelivery")]
public sealed class HubFanWeeklyDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          CREATE TABLE hub.fan_lesson_schedules (
            identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id),
            organization_id varchar(32) NOT NULL REFERENCES club.organizations(id),
            audience varchar(8) NOT NULL CHECK(audience IN ('parent','player')),
            started_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            next_due_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            cadence_days integer NOT NULL DEFAULT 7 CHECK(cadence_days=7),
            cycle bigint NOT NULL DEFAULT 0 CHECK(cycle BETWEEN 0 AND 5200),
            enabled boolean NOT NULL DEFAULT true,
            PRIMARY KEY(identity_id,organization_id,audience)
          );
          CREATE INDEX ix_fan_lesson_schedules_due
            ON hub.fan_lesson_schedules(next_due_at,identity_id)
            WHERE enabled;
          CREATE TABLE hub.fan_lesson_delivery_inbox (
            id uuid PRIMARY KEY,
            identity_id uuid NOT NULL,
            organization_id varchar(32) NOT NULL,
            audience varchar(8) NOT NULL,
            cycle bigint NOT NULL CHECK(cycle BETWEEN 1 AND 5200),
            content_id uuid NOT NULL REFERENCES hub.content(id),
            revision_id uuid NOT NULL REFERENCES hub.revisions(id),
            queued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            FOREIGN KEY(identity_id,organization_id,audience)
              REFERENCES hub.fan_lesson_schedules(
                identity_id,organization_id,audience),
            UNIQUE(identity_id,organization_id,audience,cycle)
          );
          CREATE INDEX ix_fan_learning_inbox_identity
            ON hub.fan_lesson_delivery_inbox(identity_id,organization_id,queued_at DESC);
          ALTER TABLE hub.fan_lesson_schedules ENABLE ROW LEVEL SECURITY;
          ALTER TABLE hub.fan_lesson_delivery_inbox ENABLE ROW LEVEL SECURITY;
          REVOKE ALL ON hub.fan_lesson_schedules,hub.fan_lesson_delivery_inbox FROM PUBLIC;

          CREATE FUNCTION hub.start_mobile_fan_lesson_schedule(
            _identity uuid,_phone text,_role text,_expires timestamptz,
            _owner text,_audience text
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $fan_schedule_start$
          DECLARE v_check jsonb; v_started timestamptz; v_due timestamptz;
          BEGIN
            -- The first request is possible only with current paid access.
            v_check:=hub.current_mobile_fan_lesson(
              _identity,_phone,_role,_expires,_owner,_audience);
            INSERT INTO hub.fan_lesson_schedules(
              identity_id,organization_id,audience)
              VALUES(_identity,_owner,_audience) ON CONFLICT DO NOTHING;
            SELECT started_at,next_due_at INTO v_started,v_due
              FROM hub.fan_lesson_schedules
              WHERE identity_id=_identity AND organization_id=_owner
                AND audience=_audience AND enabled;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'fan_schedule_disabled' USING ERRCODE='42501';
            END IF;
            RETURN jsonb_build_object('organization_id',_owner,
              'audience',_audience,'started_at',v_started,'next_due_at',v_due,
              'cadence_days',7);
          END $fan_schedule_start$;
          REVOKE ALL ON FUNCTION hub.start_mobile_fan_lesson_schedule(
            uuid,text,text,timestamptz,text,text) FROM PUBLIC;

          CREATE FUNCTION hub.read_mobile_fan_lesson_deliveries(
            _identity uuid,_phone text,_role text,_expires timestamptz,
            _owner text,_audience text
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $fan_schedule_inbox$
          DECLARE v_page jsonb; v_next jsonb; v_items jsonb;
          BEGIN
            v_page:=hub.current_mobile_fan_lesson(
              _identity,_phone,_role,_expires,_owner,_audience);
            v_next:=v_page->'next';
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',d.id,'queued_at',d.queued_at,'type','lesson_ready',
              'audience',d.audience,'content_id',d.content_id)
              ORDER BY d.queued_at DESC),'[]'::jsonb)
            INTO v_items
            FROM (
              SELECT i.* FROM hub.fan_lesson_delivery_inbox i
              WHERE i.identity_id=_identity AND i.organization_id=_owner
                AND i.audience=_audience
                AND v_next IS NOT NULL AND v_next<>'null'::jsonb
                AND i.content_id=(v_next->>'id')::uuid
                AND i.revision_id=(v_next->>'revision_id')::uuid
              ORDER BY i.queued_at DESC LIMIT 10
            ) d;
            RETURN jsonb_build_object('organization_id',_owner,
              'audience',_audience,'items',v_items);
          END $fan_schedule_inbox$;
          REVOKE ALL ON FUNCTION hub.read_mobile_fan_lesson_deliveries(
            uuid,text,text,timestamptz,text,text) FROM PUBLIC;

          CREATE FUNCTION hub.process_due_fan_lesson_schedules(_limit integer)
          RETURNS integer LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $fan_schedule_work$
          DECLARE v_schedule record; v_phone text; v_page jsonb; v_next jsonb;
            v_processed integer:=0; v_now timestamptz:=clock_timestamp();
          BEGIN
            IF _limit NOT BETWEEN 1 AND 50 THEN
              RAISE EXCEPTION 'invalid_fan_delivery_batch' USING ERRCODE='22023';
            END IF;
            FOR v_schedule IN
              SELECT s.* FROM hub.fan_lesson_schedules s
              WHERE s.enabled AND s.next_due_at<=v_now AND s.cycle<5200
              ORDER BY s.next_due_at,s.identity_id,s.audience
              LIMIT _limit FOR UPDATE SKIP LOCKED
            LOOP
              SELECT i.normalized_phone INTO v_phone
                FROM identity.mobile_identities i
                WHERE i.id=v_schedule.identity_id AND i.role='fan' AND i.is_active;
              IF v_phone IS NULL THEN
                UPDATE hub.fan_lesson_schedules SET enabled=false
                  WHERE identity_id=v_schedule.identity_id
                    AND organization_id=v_schedule.organization_id
                    AND audience=v_schedule.audience;
                CONTINUE;
              END IF;
              BEGIN
                v_page:=hub.current_mobile_fan_lesson(
                  v_schedule.identity_id,v_phone,'fan',v_now+interval '1 minute',
                  v_schedule.organization_id,v_schedule.audience);
                v_next:=v_page->'next';
              EXCEPTION WHEN insufficient_privilege THEN
                -- Subscription may expire or be withdrawn. Keep schedule
                -- for possible renewal, but do not send unauthorized reminders.
                UPDATE hub.fan_lesson_schedules
                  SET next_due_at=v_now+interval '1 day'
                  WHERE identity_id=v_schedule.identity_id
                    AND organization_id=v_schedule.organization_id
                    AND audience=v_schedule.audience;
                CONTINUE;
              END;
              IF v_next IS NULL OR v_next='null'::jsonb THEN
                UPDATE hub.fan_lesson_schedules
                  SET next_due_at=v_now+interval '1 day'
                  WHERE identity_id=v_schedule.identity_id
                    AND organization_id=v_schedule.organization_id
                    AND audience=v_schedule.audience;
                CONTINUE;
              END IF;
              INSERT INTO hub.fan_lesson_delivery_inbox(
                id,identity_id,organization_id,audience,cycle,content_id,revision_id)
              VALUES(gen_random_uuid(),v_schedule.identity_id,
                v_schedule.organization_id,v_schedule.audience,v_schedule.cycle+1,
                (v_next->>'id')::uuid,(v_next->>'revision_id')::uuid)
              ON CONFLICT DO NOTHING;
              UPDATE hub.fan_lesson_schedules
                SET cycle=cycle+1,
                  next_due_at=greatest(next_due_at+interval '7 days',
                    v_now+interval '7 days')
                WHERE identity_id=v_schedule.identity_id
                  AND organization_id=v_schedule.organization_id
                  AND audience=v_schedule.audience;
              v_processed:=v_processed+1;
            END LOOP;
            RETURN v_processed;
          END $fan_schedule_work$;
          REVOKE ALL ON FUNCTION hub.process_due_fan_lesson_schedules(integer)
            FROM PUBLIC;
          """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.process_due_fan_lesson_schedules(integer);
          DROP FUNCTION hub.read_mobile_fan_lesson_deliveries(
            uuid,text,text,timestamptz,text,text);
          DROP FUNCTION hub.start_mobile_fan_lesson_schedule(
            uuid,text,text,timestamptz,text,text);
          DROP TABLE hub.fan_lesson_delivery_inbox;
          DROP TABLE hub.fan_lesson_schedules;
          """);
}
