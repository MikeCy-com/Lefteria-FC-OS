using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Atomic app-only publish events and member-scoped in-app notification
/// inbox. This does not send OS/browser push. Rollback is disposable-CI only.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108310000_AppAnnouncementNotificationInbox")]
public sealed class AppAnnouncementNotificationInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- One immutable event per publication. There is no stored recipient
        -- list, eliminating stale fan-out when roster permissions change.
        CREATE TABLE club.app_announcement_notification_events (
          announcement_id varchar(128) COLLATE "C" PRIMARY KEY
            REFERENCES club.app_announcements(id) ON DELETE RESTRICT,
          organization_id varchar(32) NOT NULL,
          group_id varchar(128) COLLATE "C" NOT NULL,
          queued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          event_kind text NOT NULL DEFAULT 'in_app' CHECK (event_kind='in_app')
        );
        CREATE INDEX ix_app_announcement_inbox_recent
          ON club.app_announcement_notification_events(
            queued_at DESC,announcement_id DESC);
        REVOKE ALL ON TABLE club.app_announcement_notification_events FROM PUBLIC;

        -- An AFTER INSERT trigger participates in the same transaction as
        -- the manager's publish and mandatory security audit; if either fails,
        -- the event cannot survive. Existing notices are backfilled once.
        CREATE FUNCTION club.enqueue_app_announcement_notification()
        RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $queue_app_notice$
        BEGIN
          INSERT INTO club.app_announcement_notification_events(
            announcement_id,organization_id,group_id,queued_at)
          VALUES(NEW.id,NEW.organization_id,NEW.group_id,NEW.published_at)
          ON CONFLICT (announcement_id) DO NOTHING;
          RETURN NEW;
        END $queue_app_notice$;
        REVOKE ALL ON FUNCTION club.enqueue_app_announcement_notification()
          FROM PUBLIC;
        CREATE TRIGGER app_announcement_enqueue_notification
          AFTER INSERT ON club.app_announcements
          FOR EACH ROW EXECUTE FUNCTION club.enqueue_app_announcement_notification();
        INSERT INTO club.app_announcement_notification_events(
          announcement_id,organization_id,group_id,queued_at)
        SELECT id,organization_id,group_id,published_at
        FROM club.app_announcements
        ON CONFLICT (announcement_id) DO NOTHING;

        CREATE FUNCTION club.read_mobile_app_notification_inbox(
          _actor uuid,_phone text,_role text,_expires timestamptz,_offset integer)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $notification_inbox$
        DECLARE v_groups jsonb;
          v_opted_out boolean;
          v_items jsonb;
          v_unread integer;
        BEGIN
          IF _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN
            RAISE EXCEPTION 'notification_invalid' USING ERRCODE='22023';
          END IF;
          -- Re-resolve live bearer identity, role, guardian/coach/player links,
          -- selected management organization and canonical roster EVERY time.
          v_groups:=club.read_mobile_chat_groups(
            _actor,_phone,_role,_expires)->'groups';
          SELECT coalesce(i.notification_preferences @>
            '{"administrative": false}'::jsonb,false)
          INTO v_opted_out
          FROM identity.mobile_identities i
          WHERE i.id=_actor AND i.normalized_phone=_phone
            AND i.role=_role AND i.is_active
            AND _expires>statement_timestamp();
          IF NOT FOUND THEN
            RAISE EXCEPTION 'notification_denied' USING ERRCODE='42501';
          END IF;
          -- Preference disables notification delivery, NOT the separate
          -- published-announcements tab. Existing read receipts remain intact.
          IF v_opted_out THEN
            RETURN jsonb_build_object('notifications','[]'::jsonb,
              'unread_count',0,'next_offset',NULL,'enabled',false);
          END IF;

          SELECT count(*)::integer INTO v_unread
          FROM club.app_announcement_notification_events e
          JOIN club.app_announcements a ON a.id=e.announcement_id
          LEFT JOIN club.app_announcement_read_receipts r
            ON r.announcement_id=a.id AND r.reader_identity_id=_actor
          WHERE a.removed_at IS NULL AND r.announcement_id IS NULL
            AND EXISTS(SELECT 1 FROM jsonb_array_elements(v_groups) AS scoped(value)
              WHERE scoped.value->>'organization_id'=a.organization_id
                AND scoped.value->>'id'=a.group_id);
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'announcement_id',n.id,
            'organization_id',n.organization_id,'group_id',n.group_id,
            'group_name',n.group_name,'title',n.title,
            'published_at',n.published_at,'seen',n.seen
          ) ORDER BY n.published_at DESC,n.id DESC),'[]'::jsonb)
          INTO v_items
          FROM (
            SELECT a.id,a.organization_id,a.group_id,
              (SELECT scoped.value->>'name'
               FROM jsonb_array_elements(v_groups) AS scoped(value)
               WHERE scoped.value->>'organization_id'=a.organization_id
                 AND scoped.value->>'id'=a.group_id LIMIT 1) AS group_name,
              a.title,a.published_at,(r.announcement_id IS NOT NULL) AS seen
            FROM club.app_announcement_notification_events e
            JOIN club.app_announcements a ON a.id=e.announcement_id
            LEFT JOIN club.app_announcement_read_receipts r
              ON r.announcement_id=a.id AND r.reader_identity_id=_actor
            WHERE a.removed_at IS NULL
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(v_groups) AS scoped(value)
                WHERE scoped.value->>'organization_id'=a.organization_id
                  AND scoped.value->>'id'=a.group_id)
            ORDER BY a.published_at DESC,a.id DESC
            LIMIT 30 OFFSET _offset
          ) n;
          RETURN jsonb_build_object('notifications',v_items,
            'unread_count',least(v_unread,999),
            'next_offset',CASE WHEN jsonb_array_length(v_items)=30
              THEN _offset+30 ELSE NULL END,
            'enabled',true);
        END $notification_inbox$;
        REVOKE ALL ON FUNCTION club.read_mobile_app_notification_inbox(
          uuid,text,text,timestamptz,integer) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.read_mobile_app_notification_inbox(
          uuid,text,text,timestamptz,integer);
        DROP TRIGGER app_announcement_enqueue_notification
          ON club.app_announcements;
        DROP FUNCTION club.enqueue_app_announcement_notification();
        DROP TABLE club.app_announcement_notification_events;
        """);
}
