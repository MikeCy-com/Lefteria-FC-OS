using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Explicit member read acknowledgments and management-sender-only
/// reader names. Never infer receipts from history GET or expose phone data.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108250000_MobileChatVerifiedReadReceipts")]
public sealed class MobileChatVerifiedReadReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

        CREATE TABLE club.app_chat_read_receipts(
          message_id uuid NOT NULL REFERENCES club.app_chat_messages(id) ON DELETE RESTRICT,
          reader_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
          seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(message_id,reader_identity_id)
        );
        CREATE INDEX ix_app_chat_receipts_message
          ON club.app_chat_read_receipts(message_id,seen_at);
        REVOKE ALL ON TABLE club.app_chat_read_receipts FROM PUBLIC;

        CREATE FUNCTION club.acknowledge_mobile_chat_read(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_message uuid)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $ack$
        DECLARE v_found uuid;v_changed integer;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _message IS NULL THEN
            RAISE EXCEPTION 'invalid_receipt' USING ERRCODE='22023';
          END IF;
          -- A read receipt is explicitly requested by an authenticated,
          -- currently authorized viewer, never created by a GET/list.
          -- Lock the message against concurrent author deletion.
          SELECT m.id INTO v_found FROM club.app_chat_messages m
          WHERE m.id=_message AND m.organization_id=_organization
            AND m.group_id=_group AND NOT m.is_deleted
            AND m.sender_identity_id<>_actor
            AND m.group_kind=CASE WHEN _organization='lefteria_fc'
              THEN 'team' ELSE 'academy_group' END
            AND EXISTS(SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
            ) AS scope(g) WHERE scope.g->>'organization_id'=_organization
              AND scope.g->>'id'=_group)
          FOR SHARE OF m;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'receipt_denied' USING ERRCODE='42501';
          END IF;

          INSERT INTO club.app_chat_read_receipts(message_id,reader_identity_id)
          SELECT m.id,_actor FROM club.app_chat_messages m
          WHERE m.id=v_found AND m.organization_id=_organization
            AND m.group_id=_group AND NOT m.is_deleted
            AND m.sender_identity_id<>_actor
            AND EXISTS(SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
            ) AS scope(g) WHERE scope.g->>'organization_id'=_organization
              AND scope.g->>'id'=_group)
          ON CONFLICT DO NOTHING;
          GET DIAGNOSTICS v_changed=ROW_COUNT;
          IF v_changed>0 THEN
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,
              action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_actor,
              'app.chat.read.ack','succeeded',_organization,'group_message');
          END IF;
          -- This same-transaction check also protects idempotent requests
          -- from returning success after a revoked or expired role.
          IF NOT EXISTS(SELECT 1 FROM club.app_chat_read_receipts r
            JOIN club.app_chat_messages m ON m.id=r.message_id
            WHERE r.message_id=v_found AND r.reader_identity_id=_actor
              AND m.organization_id=_organization AND m.group_id=_group
              AND NOT m.is_deleted
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
              ) AS scope(g) WHERE scope.g->>'organization_id'=_organization
                AND scope.g->>'id'=_group)) THEN
            RAISE EXCEPTION 'receipt_denied' USING ERRCODE='42501';
          END IF;
          RETURN jsonb_build_object('message_id',v_found,'acknowledged',true);
        END $ack$;

        CREATE FUNCTION club.read_mobile_chat_readers(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_message uuid)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $reader_list$
        DECLARE v_readers jsonb;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _message IS NULL THEN
            RAISE EXCEPTION 'invalid_readers' USING ERRCODE='22023';
          END IF;
          -- A named roster-bound receipt is visible only to management
          -- while viewing a message THAT SAME MANAGEMENT ID sent. Merely
          -- belonging to management does not permit inspecting other senders.
          IF _role IS DISTINCT FROM 'management'
            OR NOT EXISTS(SELECT 1 FROM club.app_chat_messages m
              WHERE m.id=_message AND m.organization_id=_organization
                AND m.group_id=_group AND m.sender_identity_id=_actor
                AND m.group_kind=CASE WHEN _organization='lefteria_fc'
                  THEN 'team' ELSE 'academy_group' END
                AND NOT m.is_deleted
                AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                  club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
                ) AS scope(g) WHERE scope.g->>'organization_id'=_organization
                  AND scope.g->>'id'=_group)) THEN
            RAISE EXCEPTION 'readers_denied' USING ERRCODE='42501';
          END IF;
          -- Expose ONLY ID/display name for the first 50 currently valid
          -- room members that actively acknowledged. Never raw phones or
          -- deleted/revoked identities and never unverified inferred views.
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',reader.id,'name',reader.display_name)
            ORDER BY reader.seen_at ASC,reader.id),'[]'::jsonb)
          INTO v_readers FROM (
            SELECT i.id,i.display_name,r.seen_at
            FROM club.app_chat_read_receipts r
              JOIN identity.mobile_identities i ON i.id=r.reader_identity_id
            WHERE r.message_id=_message AND i.is_active
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                club.read_mobile_chat_groups(i.id,i.normalized_phone,i.role,
                  statement_timestamp()+interval '1 day')->'groups'
              ) AS membership(g) WHERE membership.g->>'organization_id'=_organization
                AND membership.g->>'id'=_group)
            ORDER BY r.seen_at ASC,i.id
            LIMIT 50
          ) reader;
          RETURN jsonb_build_object('readers',v_readers);
        END $reader_list$;

        REVOKE ALL ON FUNCTION club.acknowledge_mobile_chat_read(
          uuid,text,text,timestamptz,text,text,uuid) FROM PUBLIC;
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
            DELETE FROM club.app_chat_read_receipts WHERE message_id=v_row.id;
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

        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.read_mobile_chat_readers(
          uuid,text,text,timestamptz,text,text,uuid);
        DROP FUNCTION club.acknowledge_mobile_chat_read(
          uuid,text,text,timestamptz,text,text,uuid);
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
        DROP TABLE club.app_chat_read_receipts;

        """);
}
