using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125073000_MobileNotificationActivityFeed")]
public sealed class MobileNotificationActivityFeed : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION club.read_mobile_notification_activity(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _kind text,_offset integer)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $activity$
        DECLARE v_groups jsonb;
          v_admin_enabled boolean;
          v_messages_enabled boolean;
          v_items jsonb;
          v_next integer;
        BEGIN
          IF _kind IS NULL OR _kind NOT IN ('all','announcement','group_message','private_message')
             OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN
            RAISE EXCEPTION 'notification_activity_invalid' USING ERRCODE='22023';
          END IF;
          v_groups:=club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups';
          SELECT NOT coalesce(i.notification_preferences @> '{"administrative":false}'::jsonb,false),
                 NOT coalesce(i.notification_preferences @> '{"messages":false}'::jsonb,false)
          INTO v_admin_enabled,v_messages_enabled FROM identity.mobile_identities i
          WHERE i.id=_actor AND i.normalized_phone=_phone AND i.role=_role
            AND i.is_active AND _expires>statement_timestamp();
          IF NOT FOUND THEN RAISE EXCEPTION 'notification_activity_denied' USING ERRCODE='42501'; END IF;

          WITH rooms AS MATERIALIZED (
            SELECT g.value->>'organization_id' AS organization_id,
              g.value->>'id' AS group_id,g.value->>'kind' AS group_kind,
              g.value->>'name' AS group_name
            FROM jsonb_array_elements(v_groups) g(value)
          ), permitted_private AS MATERIALIZED (
            SELECT c.id,c.organization_id,c.context_group_id,v.hidden_before,
              c.requested_by,c.invited_identity_id
            FROM club.app_private_conversations c
            LEFT JOIN club.app_private_conversation_views v
              ON v.conversation_id=c.id AND v.viewer_identity_id=_actor
            WHERE _role IN ('parent','coach','management')
              AND v_messages_enabled AND _kind IN ('all','private_message')
              AND c.status='accepted' AND _actor IN(c.requested_by,c.invited_identity_id)
              AND EXISTS(SELECT 1 FROM rooms r WHERE r.organization_id=c.organization_id
                AND r.group_id=c.context_group_id)
              AND club.mobile_private_pair_allowed(_actor,_phone,_role,_expires,
                CASE WHEN _actor=c.requested_by THEN c.invited_identity_id
                  ELSE c.requested_by END,c.organization_id,c.context_group_id)
          ), activity AS (
            SELECT 'announcement'::text AS kind,a.id::text AS entity_id,
              a.organization_id::text AS organization_id,a.group_id::text AS group_id,
              NULL::uuid AS conversation_id,a.published_at AS occurred_at,
              a.title::text AS title,r.group_name AS group_name
            FROM club.app_announcement_notification_events e
            JOIN club.app_announcements a ON a.id=e.announcement_id
            JOIN rooms r ON r.organization_id=a.organization_id AND r.group_id=a.group_id
            WHERE v_admin_enabled AND _kind IN ('all','announcement') AND a.removed_at IS NULL
              AND NOT EXISTS(SELECT 1 FROM club.app_announcement_read_receipts receipt
                WHERE receipt.announcement_id=a.id AND receipt.reader_identity_id=_actor)
            UNION ALL
            SELECT 'group_message',m.id::text,m.organization_id::text,m.group_id::text,
              NULL::uuid,coalesce(m.edited_at,m.created_at),NULL::text,r.group_name
            FROM club.app_chat_messages m
            JOIN rooms r ON r.organization_id=m.organization_id AND r.group_id=m.group_id
              AND r.group_kind=m.group_kind
            WHERE v_messages_enabled AND _kind IN ('all','group_message')
              AND NOT m.is_deleted AND m.sender_identity_id<>_actor
              AND NOT EXISTS(SELECT 1 FROM club.app_chat_read_receipts receipt
                WHERE receipt.message_id=m.id AND receipt.reader_identity_id=_actor
                  AND receipt.message_version=m.version)
            UNION ALL
            SELECT 'private_message',m.id::text,c.organization_id::text,c.context_group_id::text,
              c.id,coalesce(m.edited_at,m.created_at),NULL::text,NULL::text
            FROM permitted_private c JOIN club.app_private_messages m ON m.conversation_id=c.id
            WHERE NOT m.is_deleted AND m.sender_identity_id<>_actor
              AND m.sender_identity_id IN(c.requested_by,c.invited_identity_id)
              AND (c.hidden_before IS NULL OR m.created_at>c.hidden_before)
              AND NOT EXISTS(SELECT 1 FROM club.app_private_read_receipts receipt
                WHERE receipt.message_id=m.id AND receipt.reader_identity_id=_actor
                  AND receipt.message_version=m.version)
          ), limited AS (
            SELECT * FROM activity ORDER BY occurred_at DESC,kind COLLATE "C",entity_id COLLATE "C"
            LIMIT 31 OFFSET _offset
          ), visible AS (
            SELECT * FROM limited ORDER BY occurred_at DESC,kind COLLATE "C",entity_id COLLATE "C" LIMIT 30
          )
          SELECT coalesce((SELECT jsonb_agg(jsonb_build_object(
            'kind',kind,'entity_id',entity_id,'organization_id',organization_id,
            'group_id',group_id,'conversation_id',conversation_id,'occurred_at',occurred_at,
            'title',title,'group_name',group_name)
            ORDER BY occurred_at DESC,kind COLLATE "C",entity_id COLLATE "C") FROM visible),'[]'::jsonb),
            CASE WHEN (SELECT count(*) FROM limited)>30 AND _offset+30<=100000
              THEN _offset+30 ELSE NULL END
          INTO v_items,v_next;
          RETURN jsonb_build_object('items',v_items,'next_offset',v_next);
        END $activity$;
        REVOKE ALL ON FUNCTION club.read_mobile_notification_activity(
          uuid,text,text,timestamptz,text,integer) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.read_mobile_notification_activity(uuid,text,text,timestamptz,text,integer);
        """);
}
