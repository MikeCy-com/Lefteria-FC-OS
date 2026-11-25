using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125070000_MobileGroupChatUnreadSummary")]
public sealed class MobileGroupChatUnreadSummary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION club.read_mobile_chat_unread_summary(
          _actor uuid,_phone text,_role text,_expires timestamptz)
        RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $unread$
        DECLARE v_groups jsonb;
          v_opted_out boolean;
          v_count integer;
        BEGIN
          -- Resolve canonical membership and live session, including selected
          -- management organization, even when the user has opted out.
          v_groups:=club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups';
          SELECT coalesce(i.notification_preferences @> '{"messages":false}'::jsonb,false)
          INTO v_opted_out
          FROM identity.mobile_identities i
          WHERE i.id=_actor AND i.normalized_phone=_phone AND i.role=_role
            AND i.is_active AND _expires>statement_timestamp();
          IF NOT FOUND THEN
            RAISE EXCEPTION 'chat_unread_denied' USING ERRCODE='42501';
          END IF;
          IF v_opted_out THEN
            RETURN jsonb_build_object('unread_count',0,'enabled',false);
          END IF;
          -- Count only unseen current versions, not own messages, deleted
          -- messages, or rooms no longer present in the live directory.
          -- A capped projection avoids returning message bodies or identities.
          SELECT count(*)::integer INTO v_count
          FROM (
            SELECT 1 FROM club.app_chat_messages m
            WHERE NOT m.is_deleted AND m.sender_identity_id<>_actor
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(v_groups) g(value)
                WHERE g.value->>'organization_id'=m.organization_id
                  AND g.value->>'id'=m.group_id
                  AND g.value->>'kind'=m.group_kind)
              AND NOT EXISTS(SELECT 1 FROM club.app_chat_read_receipts r
                WHERE r.message_id=m.id AND r.reader_identity_id=_actor
                  AND r.message_version=m.version)
            LIMIT 999
          ) unread;
          RETURN jsonb_build_object('unread_count',v_count,'enabled',true);
        END $unread$;
        REVOKE ALL ON FUNCTION club.read_mobile_chat_unread_summary(
          uuid,text,text,timestamptz) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.read_mobile_chat_unread_summary(uuid,text,text,timestamptz);
        """);
}
