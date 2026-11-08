using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Scoped, author-only message edits and tombstone deletes. No public
/// message bodies, direct runtime table grants, or private-chat actions.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108230000_MobileChatAuthorMutations")]
public sealed class MobileChatAuthorMutations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE club.app_chat_messages
          DROP CONSTRAINT ck_app_chat_content;
        ALTER TABLE club.app_chat_messages
          ADD COLUMN edited_at timestamptz NULL,
          ADD COLUMN is_deleted boolean NOT NULL DEFAULT false,
          ADD COLUMN version bigint NOT NULL DEFAULT 1;
        ALTER TABLE club.app_chat_messages
          ADD CONSTRAINT ck_app_chat_version CHECK (version>=1),
          ADD CONSTRAINT ck_app_chat_content CHECK (
            (is_deleted AND content='') OR
            (NOT is_deleted AND char_length(content) BETWEEN 1 AND 10000
              AND content=btrim(content) AND content !~ '[[:cntrl:]]')
          );

        CREATE TABLE club.app_chat_message_revisions (
          message_id uuid NOT NULL REFERENCES club.app_chat_messages(id) ON DELETE RESTRICT,
          version bigint NOT NULL CHECK (version>=2),
          actor_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
          operation varchar(8) NOT NULL CHECK (operation IN ('edit','delete')),
          occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(message_id,version)
        );
        REVOKE ALL ON TABLE club.app_chat_message_revisions FROM PUBLIC;


        CREATE OR REPLACE FUNCTION club.read_mobile_chat_messages(
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
            'content',m.content,'created_at',m.created_at,
            'edited_at',m.edited_at,'is_deleted',m.is_deleted,'version',m.version)
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


        CREATE FUNCTION club.mutate_mobile_chat_message(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_message uuid,
          _expected_version bigint,_operation text,_content text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $chat_mutate$
        DECLARE
          v_row club.app_chat_messages%ROWTYPE;
          v_name text;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _message IS NULL OR _expected_version IS NULL OR _expected_version<1
            OR _operation NOT IN ('edit','delete')
            OR (_operation='edit' AND
              (_content IS NULL OR char_length(_content) NOT BETWEEN 1 AND 10000
               OR _content<>btrim(_content) OR _content ~ '[[:cntrl:]]'))
            OR (_operation='delete' AND _content IS NOT NULL) THEN
            RAISE EXCEPTION 'chat_mutation_invalid' USING ERRCODE='22023';
          END IF;

          -- UPDATE is the final authorization boundary, not an earlier HTTP
          -- preflight. Match current canonical group, room, sender, version
          -- and active row in the same SQL statement that mutates content.
          UPDATE club.app_chat_messages AS m
          SET content=CASE WHEN _operation='delete' THEN '' ELSE _content END,
              is_deleted=(_operation='delete'),
              edited_at=CASE WHEN _operation='edit' THEN clock_timestamp() ELSE m.edited_at END,
              version=m.version+1
          WHERE m.id=_message AND m.organization_id=_organization
            AND m.group_id=_group AND m.group_kind=CASE
              WHEN _organization='lefteria_fc' THEN 'team' ELSE 'academy_group' END
            AND m.sender_identity_id=_actor AND NOT m.is_deleted
            AND m.version=_expected_version
            AND EXISTS(SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
            ) AS scoped(group_row) WHERE scoped.group_row->>'organization_id'=_organization
              AND scoped.group_row->>'id'=_group)
          RETURNING * INTO v_row;

          IF NOT FOUND THEN
            -- Conflict is revealed only to the still-authorized room member
            -- and original author, not to guessed message IDs.
            IF EXISTS(SELECT 1 FROM club.app_chat_messages m
              WHERE m.id=_message AND m.organization_id=_organization
                AND m.group_id=_group AND m.sender_identity_id=_actor
                AND NOT m.is_deleted
                AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                  club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
                ) AS scoped(group_row) WHERE scoped.group_row->>'organization_id'=_organization
                  AND scoped.group_row->>'id'=_group)) THEN
              RAISE EXCEPTION 'chat_message_stale' USING ERRCODE='40001';
            END IF;
            RAISE EXCEPTION 'chat_message_denied' USING ERRCODE='42501';
          END IF;

          INSERT INTO club.app_chat_message_revisions(
            message_id,version,actor_identity_id,operation)
          VALUES(v_row.id,v_row.version,_actor,_operation);
          INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_actor,
            CASE WHEN _operation='edit' THEN 'app.chat.message.edit'
              ELSE 'app.chat.message.delete' END,
            'succeeded',_organization,'group_message');

          SELECT i.display_name INTO v_name FROM identity.mobile_identities i WHERE i.id=_actor;
          RETURN jsonb_build_object('id',v_row.id,'organization_id',_organization,
            'group_id',_group,'sender_id',_actor,'sender_name',v_name,
            'content',v_row.content,'created_at',v_row.created_at,
            'edited_at',v_row.edited_at,'is_deleted',v_row.is_deleted,'version',v_row.version);
        END $chat_mutate$;

        REVOKE ALL ON FUNCTION club.mutate_mobile_chat_message(
          uuid,text,text,timestamptz,text,text,uuid,bigint,text,text) FROM PUBLIC;

        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.mutate_mobile_chat_message(
          uuid,text,text,timestamptz,text,text,uuid,bigint,text,text);
        DROP TABLE club.app_chat_message_revisions;
        CREATE OR REPLACE FUNCTION club.read_mobile_chat_messages(
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
        ALTER TABLE club.app_chat_messages
          DROP CONSTRAINT ck_app_chat_content,
          DROP CONSTRAINT ck_app_chat_version;
        ALTER TABLE club.app_chat_messages
          ADD CONSTRAINT ck_app_chat_content CHECK (
            char_length(content) BETWEEN 1 AND 10000 AND content=btrim(content)
            AND content !~ '[[:cntrl:]]');
        -- Deletion tombstones cannot be reversed into original content.
        -- Operators must never downgrade a database containing deletions.
        ALTER TABLE club.app_chat_messages
          DROP COLUMN edited_at,
          DROP COLUMN is_deleted,
          DROP COLUMN version;
        """);
}
