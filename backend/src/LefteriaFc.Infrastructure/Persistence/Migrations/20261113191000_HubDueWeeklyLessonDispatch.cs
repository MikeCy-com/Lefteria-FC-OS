using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Transactional in-app dispatch. Only a dedicated, separately configured
/// scheduler DB role may be granted this routine. No external push is sent.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113191000_HubDueWeeklyLessonDispatch")]
public sealed class HubDueWeeklyLessonDispatch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.dispatch_due_weekly_learning(_max integer)
            RETURNS integer
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $dispatch$
            DECLARE
              v_enrollment hub.learning_enrollments%ROWTYPE;
              v_phone text;
              v_role text;
              v_content uuid;
              v_revision uuid;
              v_current timestamptz;
              v_queued integer:=0;
            BEGIN
              IF _max IS NULL OR _max NOT BETWEEN 1 AND 100 THEN
                RAISE EXCEPTION 'invalid_dispatch_batch' USING ERRCODE='22023';
              END IF;

              FOR v_enrollment IN
                SELECT e.* FROM hub.learning_enrollments e
                WHERE e.status='active'
                  AND e.next_due_at<=clock_timestamp()
                  AND e.delivery_week<5200
                ORDER BY e.next_due_at ASC,e.id ASC
                LIMIT _max
                FOR UPDATE OF e SKIP LOCKED
              LOOP
                v_current:=clock_timestamp();
                -- No coach curriculum audience exists in the approved HUB
                -- schema yet. Never send player lessons to coaches.
                IF v_enrollment.audience='coach' THEN CONTINUE; END IF;
                SELECT i.normalized_phone,i.role INTO v_phone,v_role
                  FROM identity.mobile_identities i
                  WHERE i.id=v_enrollment.identity_id AND i.is_active
                  FOR SHARE;
                IF NOT FOUND THEN CONTINUE; END IF;
                IF (v_role='parent' AND v_enrollment.audience<>'parent') OR
                   (v_role='player' AND v_enrollment.audience<>'player') OR
                   (v_role='fan' AND v_enrollment.audience NOT IN
                     ('fan_parent','fan_player')) OR
                   v_role NOT IN ('parent','player','fan') THEN
                  CONTINUE;
                END IF;

                -- All recipient relationships are rechecked at actual
                -- delivery time, not just at initial enrollment.
                BEGIN
                  PERFORM hub.authorize_mobile_learning_enrollment(
                    v_enrollment.identity_id,v_phone,v_role,
                    v_current+interval '1 minute',
                    v_enrollment.organization_id,v_enrollment.audience);
                EXCEPTION WHEN insufficient_privilege THEN
                  CONTINUE;
                END;

                v_content:=NULL;v_revision:=NULL;
                SELECT c.id,rr.id INTO v_content,v_revision
                FROM hub.content c
                JOIN LATERAL (
                  SELECT r.id,r.text_review,r.visual_review
                  FROM hub.revisions r WHERE r.content_id=c.id
                  ORDER BY r.revision DESC LIMIT 1
                ) rr ON true
                WHERE c.organization_id=v_enrollment.organization_id
                  AND c.status='published' AND c.approved_by IS NOT NULL
                  AND c.approved_at IS NOT NULL
                  AND c.published_at IS NOT NULL AND c.published_at<=v_current
                  AND (c.kind='article' OR
                    (v_enrollment.audience='player' AND c.kind='course'))
                  AND (
                    (v_enrollment.audience IN ('parent','fan_parent') AND
                      c.audience_parent) OR
                    (v_enrollment.audience IN ('player','fan_player') AND
                      c.audience_player))
                  -- Client-selected age is never proof of canonical age.
                  AND c.age_min IS NULL AND c.age_max IS NULL
                  AND rr.text_review='passed' AND rr.visual_review='passed'
                  AND NOT EXISTS (
                    SELECT 1 FROM hub.media_assets media
                    WHERE media.revision_id=rr.id AND
                      (media.moderation<>'reviewed' OR media.kind<>'image'))
                  AND NOT EXISTS (
                    SELECT 1 FROM hub.learning_delivery_outbox d
                    WHERE d.enrollment_id=v_enrollment.id
                      AND d.content_id=c.id)
                ORDER BY c.published_at ASC,c.id ASC
                LIMIT 1;
                IF v_content IS NULL OR v_revision IS NULL THEN
                  -- Do not skip a week just because editorial publication
                  -- is delayed, all content was withdrawn, or no reviewed
                  -- age-safe lesson is currently available.
                  CONTINUE;
                END IF;

                INSERT INTO hub.learning_delivery_outbox(
                  id,enrollment_id,content_id,revision_id,
                  delivery_week,due_at,channel,state,delivered_at)
                VALUES(gen_random_uuid(),v_enrollment.id,v_content,v_revision,
                  v_enrollment.delivery_week,v_enrollment.next_due_at,
                  'in_app','delivered',v_current)
                ON CONFLICT DO NOTHING;
                IF NOT FOUND THEN CONTINUE; END IF;

                -- Keep one lesson per interval. Late workers do not blast
                -- multiple missed lessons in the same dispatch cycle.
                UPDATE hub.learning_enrollments e
                  SET delivery_week=e.delivery_week+1,
                    next_due_at=greatest(
                      e.next_due_at+interval '7 days',
                      v_current+interval '7 days')
                WHERE e.id=v_enrollment.id AND e.status='active'
                  AND e.delivery_week=v_enrollment.delivery_week;
                IF NOT FOUND THEN
                  RAISE EXCEPTION 'learning_dispatch_race' USING ERRCODE='40001';
                END IF;
                v_queued:=v_queued+1;
              END LOOP;
              RETURN v_queued;
            END $dispatch$;
            REVOKE ALL ON FUNCTION hub.dispatch_due_weekly_learning(integer)
              FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.dispatch_due_weekly_learning(integer);
            """);
}
