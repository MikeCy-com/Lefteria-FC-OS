using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Explicit live-room read receipts. Only current management authors
/// see named current participants, never fans or public website visitors.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108250000_MobileChatReadReceipts")]
public sealed class MobileChatReadReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

        CREATE TABLE club.app_chat_read_receipts (
          message_id uuid NOT NULL REFERENCES club.app_chat_messages(id) ON DELETE RESTRICT,
          reader_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
          message_version bigint NOT NULL CHECK (message_version>=1),
          read_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(message_id,reader_identity_id)
        );
        CREATE INDEX ix_app_chat_receipts_reader ON club.app_chat_read_receipts(reader_identity_id);
        REVOKE ALL ON TABLE club.app_chat_read_receipts FROM PUBLIC;

        -- Internal helper: verifies a recorded reader still belongs to this
        -- exact canonical team/Academy room TODAY. A previous read never
        -- authorizes ongoing name disclosure after membership revocation.
        CREATE FUNCTION club.is_current_mobile_chat_reader(
          _reader uuid,_organization text,_group text)
        RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $current_reader$
        DECLARE v_identity identity.mobile_identities%ROWTYPE;
        BEGIN
          SELECT * INTO v_identity FROM identity.mobile_identities
          WHERE id=_reader AND is_active;
          IF NOT FOUND THEN RETURN false; END IF;
          RETURN EXISTS(SELECT 1 FROM jsonb_array_elements(
            club.read_mobile_chat_groups(v_identity.id,v_identity.normalized_phone,
              v_identity.role,statement_timestamp()+interval '1 minute')->'groups'
            ) AS g(value)
            WHERE g.value->>'organization_id'=_organization
              AND g.value->>'id'=_group);
        EXCEPTION WHEN SQLSTATE '42501' OR SQLSTATE '55000' OR SQLSTATE '54000'
          THEN RETURN false;
        END $current_reader$;
        REVOKE ALL ON FUNCTION club.is_current_mobile_chat_reader(
          uuid,text,text) FROM PUBLIC;

        CREATE FUNCTION club.ack_mobile_chat_read(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_message uuid,_version bigint)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $ack_read$
        DECLARE v_id uuid;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
             OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
             OR _message IS NULL OR _version IS NULL OR _version<1 THEN
            RAISE EXCEPTION 'receipt_invalid' USING ERRCODE='22023';
          END IF;
          -- Lock to serialize explicit acknowledgments against deletion.
          -- GET and message hydration NEVER call this function.
          SELECT m.id INTO v_id
          FROM club.app_chat_messages m
          WHERE m.id=_message AND m.version=_version AND m.organization_id=_organization
            AND m.group_id=_group AND NOT m.is_deleted
            AND m.sender_identity_id<>_actor
            AND m.group_kind=CASE WHEN _organization='lefteria_fc'
              THEN 'team' ELSE 'academy_group' END
            AND EXISTS(SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
            ) AS g(value) WHERE g.value->>'organization_id'=_organization
              AND g.value->>'id'=_group)
          FOR SHARE OF m;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'receipt_denied' USING ERRCODE='42501';
          END IF;
          -- Final write repeats current-authority verification in this very
          -- INSERT statement. Conflicts are idempotent (first seen_at wins).
          INSERT INTO club.app_chat_read_receipts(message_id,reader_identity_id,message_version)
          SELECT m.id,_actor,m.version FROM club.app_chat_messages m
          WHERE m.id=v_id AND m.version=_version AND m.organization_id=_organization
            AND m.group_id=_group AND NOT m.is_deleted
            AND m.sender_identity_id<>_actor
            AND EXISTS(SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
            ) AS g(value) WHERE g.value->>'organization_id'=_organization
              AND g.value->>'id'=_group)
          ON CONFLICT DO NOTHING;
          RETURN jsonb_build_object('message_id',v_id,'seen',true,'version',_version);
        END $ack_read$;
        REVOKE ALL ON FUNCTION club.ack_mobile_chat_read(
          uuid,text,text,timestamptz,text,text,uuid,bigint) FROM PUBLIC;

        CREATE FUNCTION club.read_mobile_chat_readers(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_message uuid)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $readers$
        DECLARE v_readers jsonb;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
             OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
             OR _message IS NULL THEN
            RAISE EXCEPTION 'receipt_invalid' USING ERRCODE='22023';
          END IF;
          -- A team manager can inspect ONLY a message they authored,
          -- while their currently selected organization and room access
          -- remain valid. Coaches/parents/players never get names.
          IF _role IS DISTINCT FROM 'management' OR NOT EXISTS(
            SELECT 1 FROM club.app_chat_messages m
            WHERE m.id=_message AND m.organization_id=_organization
              AND m.group_id=_group AND NOT m.is_deleted
              AND m.sender_identity_id=_actor
              AND m.group_kind=CASE WHEN _organization='lefteria_fc'
                THEN 'team' ELSE 'academy_group' END
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
              ) AS g(value) WHERE g.value->>'organization_id'=_organization
                AND g.value->>'id'=_group)) THEN
            RAISE EXCEPTION 'readers_denied' USING ERRCODE='42501';
          END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object('id',entry.id,'name',entry.display_name)
            ORDER BY entry.display_name COLLATE "C",entry.id),'[]'::jsonb)
            INTO v_readers
          FROM (SELECT i.id,i.display_name
            FROM club.app_chat_read_receipts receipt
            JOIN identity.mobile_identities i ON i.id=receipt.reader_identity_id
            WHERE receipt.message_id=_message
              AND receipt.message_version=(SELECT m.version FROM club.app_chat_messages m WHERE m.id=_message)
              AND club.is_current_mobile_chat_reader(i.id,_organization,_group)
            ORDER BY i.display_name COLLATE "C",i.id LIMIT 151) entry;
          IF jsonb_array_length(v_readers)>150 THEN
            RAISE EXCEPTION 'readers_limit' USING ERRCODE='54000';
          END IF;
          RETURN jsonb_build_object('readers',v_readers);
        END $readers$;
        REVOKE ALL ON FUNCTION club.read_mobile_chat_readers(
          uuid,text,text,timestamptz,text,text,uuid) FROM PUBLIC;

        CREATE OR REPLACE FUNCTION club.mutate_mobile_chat_message(
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
            OR _operation IS NULL OR _operation NOT IN ('edit','delete')
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

          IF _operation='delete' THEN
            DELETE FROM club.app_chat_reactions WHERE message_id=v_row.id;
          END IF;
          -- A message edit invalidates receipts for its old content version.
          DELETE FROM club.app_chat_read_receipts WHERE message_id=v_row.id;
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
            'edited_at',v_row.edited_at,'is_deleted',v_row.is_deleted,'version',v_row.version,
            'reactions',CASE WHEN v_row.is_deleted THEN '[]'::jsonb ELSE coalesce((SELECT jsonb_agg(jsonb_build_object(
              'emoji',counts.emoji,'count',counts.total,'mine',counts.mine
            ) ORDER BY counts.emoji COLLATE "C")
              FROM (SELECT reaction.emoji,count(*)::integer AS total,
                bool_or(reaction.actor_identity_id=_actor) AS mine
                FROM club.app_chat_reactions reaction
                WHERE reaction.message_id=v_row.id
                GROUP BY reaction.emoji) counts),'[]'::jsonb) END);
        END $chat_mutate$;

        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Disposable CI only. Downgrades lose receipts permanently.
        CREATE OR REPLACE FUNCTION club.mutate_mobile_chat_message(
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
            OR _operation IS NULL OR _operation NOT IN ('edit','delete')
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

          IF _operation='delete' THEN
            DELETE FROM club.app_chat_reactions WHERE message_id=v_row.id;
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
            'edited_at',v_row.edited_at,'is_deleted',v_row.is_deleted,'version',v_row.version,
            'reactions',CASE WHEN v_row.is_deleted THEN '[]'::jsonb ELSE coalesce((SELECT jsonb_agg(jsonb_build_object(
              'emoji',counts.emoji,'count',counts.total,'mine',counts.mine
            ) ORDER BY counts.emoji COLLATE "C")
              FROM (SELECT reaction.emoji,count(*)::integer AS total,
                bool_or(reaction.actor_identity_id=_actor) AS mine
                FROM club.app_chat_reactions reaction
                WHERE reaction.message_id=v_row.id
                GROUP BY reaction.emoji) counts),'[]'::jsonb) END);
        END $chat_mutate$;
        DROP FUNCTION club.read_mobile_chat_readers(
          uuid,text,text,timestamptz,text,text,uuid);
        DROP FUNCTION club.ack_mobile_chat_read(
          uuid,text,text,timestamptz,text,text,uuid,bigint);
        DROP FUNCTION club.is_current_mobile_chat_reader(uuid,text,text);
        DROP TABLE club.app_chat_read_receipts;
        """);
}
