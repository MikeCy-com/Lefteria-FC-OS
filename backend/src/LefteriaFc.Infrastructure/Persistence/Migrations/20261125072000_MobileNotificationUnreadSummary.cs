using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125072000_MobileNotificationUnreadSummary")]
public sealed class MobileNotificationUnreadSummary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION club.read_mobile_notification_unread_summary(
          _actor uuid,_phone text,_role text,_expires timestamptz)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $summary$
        DECLARE v_groups jsonb;
          v_ann jsonb;
          v_group jsonb;
          v_private jsonb:=jsonb_build_object('unread_count',0,'enabled',false);
          v_ann_count integer;
          v_group_count integer;
          v_private_count integer;
        BEGIN
          -- Reuse the existing authorization boundary, including empty
          -- but valid roster directories and the selected management owner.
          v_groups:=club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups';
          v_ann:=club.read_mobile_app_notification_inbox(_actor,_phone,_role,_expires,0);
          v_group:=club.read_mobile_chat_unread_summary(_actor,_phone,_role,_expires);
          IF _role IN ('parent','coach','management') AND jsonb_array_length(v_groups)>0 THEN
            v_private:=club.read_mobile_private_unread_summary(_actor,_phone,_role,_expires);
          END IF;
          v_ann_count:=(v_ann->>'unread_count')::integer;
          v_group_count:=(v_group->>'unread_count')::integer;
          v_private_count:=(v_private->>'unread_count')::integer;
          -- No raw notification rows, names, IDs or message bodies in the
          -- summary. Preserve each destination's independent preference.
          RETURN jsonb_build_object(
            'announcements',jsonb_build_object('unread_count',v_ann_count,'enabled',(v_ann->>'enabled')::boolean),
            'group_messages',v_group,'private_messages',v_private,
            'unread_count',least(v_ann_count+v_group_count+v_private_count,999));
        END $summary$;
        REVOKE ALL ON FUNCTION club.read_mobile_notification_unread_summary(
          uuid,text,text,timestamptz) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.read_mobile_notification_unread_summary(uuid,text,text,timestamptz);
        """);
}
