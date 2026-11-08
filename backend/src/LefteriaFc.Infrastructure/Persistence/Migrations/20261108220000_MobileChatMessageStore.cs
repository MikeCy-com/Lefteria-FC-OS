using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// First live app-communications slice: canonical roster-scoped team/Academy
/// rooms and durable text messages. Private chats, files, reactions, receipts
/// and announcement publishing are deliberately not enabled by this schema.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108220000_MobileChatMessageStore")]
public sealed class MobileChatMessageStore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE club.app_chat_messages (
          id uuid PRIMARY KEY,
          organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
          group_id varchar(128) COLLATE "C" NOT NULL,
          group_kind varchar(16) NOT NULL,
          sender_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
          content varchar(10000) NOT NULL,
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          CONSTRAINT ck_app_chat_kind CHECK (group_kind IN ('team','academy_group')),
          CONSTRAINT ck_app_chat_content CHECK (
            char_length(content) BETWEEN 1 AND 10000 AND content=btrim(content)
            AND content !~ '[[:cntrl:]]'),
          CONSTRAINT ck_app_chat_scope CHECK (
            (organization_id='lefteria_fc' AND group_kind='team') OR
            (organization_id='lefteria_fc_academy' AND group_kind='academy_group'))
        );
        CREATE INDEX ix_app_chat_messages_room_recent
          ON club.app_chat_messages(organization_id,group_kind,group_id,created_at DESC,id DESC);

        -- Always resolve the current actor AND exact room before accessing rows.
        -- No general inbox, cross-team search or private conversation exposure.
        CREATE FUNCTION club.read_mobile_chat_messages(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_offset integer)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $chat_read$
        DECLARE v_items jsonb;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN
            RAISE EXCEPTION 'chat_invalid_scope' USING ERRCODE='22023';
          END IF;
          IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(
            club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups') AS entry(value)
            WHERE entry.value->>'organization_id'=_organization AND entry.value->>'id'=_group) THEN
            RAISE EXCEPTION 'chat_denied' USING ERRCODE='42501';
          END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',m.id,'organization_id',m.organization_id,'group_id',m.group_id,
            'sender_id',m.sender_identity_id,'sender_name',m.sender_name,
            'content',m.content,'created_at',m.created_at)
            ORDER BY m.created_at DESC,m.id DESC),'[]'::jsonb)
          INTO v_items FROM (
            SELECT c.*,i.display_name AS sender_name
            FROM club.app_chat_messages c JOIN identity.mobile_identities i
              ON i.id=c.sender_identity_id
            WHERE c.organization_id=_organization AND c.group_id=_group
              AND c.group_kind=CASE WHEN _organization='lefteria_fc'
                THEN 'team' ELSE 'academy_group' END
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                  club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups') AS entry(value)
                  WHERE entry.value->>'organization_id'=_organization AND entry.value->>'id'=_group)
            ORDER BY c.created_at DESC,c.id DESC
            LIMIT 50 OFFSET _offset
          ) m;
          RETURN jsonb_build_object('messages',v_items,
            'next_offset',CASE WHEN jsonb_array_length(v_items)=50 THEN _offset+50 ELSE NULL END);
        END $chat_read$;

        CREATE FUNCTION club.send_mobile_chat_message(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_content text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $chat_send$
        DECLARE v_id uuid := gen_random_uuid();
          v_row club.app_chat_messages%ROWTYPE;
          v_name text;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _content IS NULL OR char_length(_content) NOT BETWEEN 1 AND 10000
            OR _content<>btrim(_content) OR _content ~ '[[:cntrl:]]' THEN
            RAISE EXCEPTION 'chat_invalid_message' USING ERRCODE='22023';
          END IF;
          -- The final INSERT repeats the roster check in the SAME SQL statement
          -- as the write. An invalid room cannot be created by supplying IDs.
          INSERT INTO club.app_chat_messages(id,organization_id,group_id,group_kind,
            sender_identity_id,content)
          SELECT v_id,_organization,_group,
            CASE WHEN _organization='lefteria_fc' THEN 'team' ELSE 'academy_group' END,
            _actor,_content
          WHERE EXISTS (SELECT 1 FROM jsonb_array_elements(
            club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups') AS entry(value)
            WHERE entry.value->>'organization_id'=_organization AND entry.value->>'id'=_group)
          RETURNING * INTO v_row;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'chat_denied' USING ERRCODE='42501';
          END IF;
          SELECT i.display_name INTO v_name FROM identity.mobile_identities i WHERE i.id=_actor;
          INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_actor,
            'app.chat.message.send','succeeded',_organization,'group_message');
          RETURN jsonb_build_object('id',v_row.id,'organization_id',_organization,
            'group_id',_group,'sender_id',_actor,'sender_name',v_name,
            'content',v_row.content,'created_at',v_row.created_at);
        END $chat_send$;

        REVOKE ALL ON TABLE club.app_chat_messages FROM PUBLIC;
        REVOKE ALL ON FUNCTION club.read_mobile_chat_messages(
          uuid,text,text,timestamptz,text,text,integer) FROM PUBLIC;
        REVOKE ALL ON FUNCTION club.send_mobile_chat_message(
          uuid,text,text,timestamptz,text,text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.send_mobile_chat_message(uuid,text,text,timestamptz,text,text,text);
        DROP FUNCTION club.read_mobile_chat_messages(uuid,text,text,timestamptz,text,text,integer);
        DROP TABLE club.app_chat_messages;
        """);
}
