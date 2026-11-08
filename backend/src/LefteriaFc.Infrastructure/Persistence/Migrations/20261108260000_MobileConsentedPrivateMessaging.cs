using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Consented adult-role private conversations, isolated from group chat.
/// No player/fan direct messaging. Rollback discards private data: disposable CI only.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108260000_MobileConsentedPrivateMessaging")]
public sealed class MobileConsentedPrivateMessaging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

        CREATE TABLE club.app_private_conversations (
          id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
          organization_id varchar(32) NOT NULL REFERENCES club.organizations(id),
          context_group_id varchar(128) COLLATE "C" NOT NULL,
          requested_by uuid NOT NULL REFERENCES identity.mobile_identities(id),
          invited_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id),
          status varchar(16) NOT NULL DEFAULT 'pending'
            CHECK (status IN ('pending','accepted','declined')),
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          CONSTRAINT ck_private_pair_distinct CHECK (requested_by<>invited_identity_id),
          CONSTRAINT ck_private_organization CHECK (
            organization_id IN ('lefteria_fc','lefteria_fc_academy'))
        );
        -- One current invitation/accepted room per unordered pair and
        -- canonical roster context. Declined requests can be re-proposed
        -- only after a 24h cooldown enforced in the guarded function.
        CREATE UNIQUE INDEX ux_private_active_pair ON club.app_private_conversations(
          organization_id,context_group_id,
          LEAST(requested_by,invited_identity_id),
          GREATEST(requested_by,invited_identity_id))
          WHERE status IN ('pending','accepted');
        CREATE INDEX ix_private_incoming ON club.app_private_conversations(
          invited_identity_id,status,updated_at DESC);
        CREATE INDEX ix_private_outgoing ON club.app_private_conversations(
          requested_by,status,updated_at DESC);

        CREATE TABLE club.app_private_messages (
          id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
          conversation_id uuid NOT NULL REFERENCES club.app_private_conversations(id),
          sender_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id),
          content varchar(10000) NOT NULL,
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          edited_at timestamptz,
          is_deleted boolean NOT NULL DEFAULT false,
          version bigint NOT NULL DEFAULT 1 CHECK (version>=1),
          CONSTRAINT ck_private_message_content CHECK (
            (is_deleted AND content='') OR
            (NOT is_deleted AND char_length(content) BETWEEN 1 AND 10000
             AND content=btrim(content) AND content !~ '[[:cntrl:]]'))
        );
        CREATE INDEX ix_private_message_page ON club.app_private_messages(
          conversation_id,created_at DESC,id DESC);
        CREATE TABLE club.app_private_message_revisions (
          message_id uuid NOT NULL REFERENCES club.app_private_messages(id),
          version bigint NOT NULL,
          actor_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id),
          operation varchar(8) NOT NULL CHECK (operation IN ('edit','delete')),
          occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(message_id,version)
        );
        CREATE TABLE club.app_private_conversation_views (
          conversation_id uuid NOT NULL REFERENCES club.app_private_conversations(id),
          viewer_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id),
          hidden_before timestamptz,
          PRIMARY KEY(conversation_id,viewer_identity_id)
        );
        REVOKE ALL ON TABLE club.app_private_conversations,
          club.app_private_messages,club.app_private_message_revisions,
          club.app_private_conversation_views FROM PUBLIC;

        CREATE FUNCTION club.mobile_private_pair_allowed(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _other uuid,_organization text,_group text)
        RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_pair$
        DECLARE v_other identity.mobile_identities%ROWTYPE;
        BEGIN
          -- Exclude every player and fan identity, including First Team
          -- players without independently verified adult eligibility.
          IF _actor IS NULL OR _other IS NULL OR _actor=_other
             OR _role NOT IN ('parent','coach','management')
             OR _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
             OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128 THEN
            RETURN false;
          END IF;
          SELECT * INTO v_other FROM identity.mobile_identities
            WHERE id=_other AND is_active AND role IN ('parent','coach','management');
          IF NOT FOUND OR (_role='parent' AND v_other.role='parent') THEN
            RETURN false;
          END IF;
          -- The inviter AND recipient must still belong to exactly this
          -- canonical FC/Academy group. A displayed contact/role is NOT
          -- a grant; the actual write repeats this guard.
          RETURN EXISTS(SELECT 1 FROM jsonb_array_elements(
            club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
          ) AS g(value)
            WHERE g.value->>'organization_id'=_organization
              AND g.value->>'id'=_group)
          AND EXISTS(SELECT 1 FROM jsonb_array_elements(
            club.read_mobile_chat_groups(v_other.id,v_other.normalized_phone,
              v_other.role,statement_timestamp()+interval '1 minute')->'groups'
          ) AS g(value)
            WHERE g.value->>'organization_id'=_organization
              AND g.value->>'id'=_group);
        EXCEPTION WHEN SQLSTATE '42501' OR SQLSTATE '55000' OR SQLSTATE '54000'
          THEN RETURN false;
        END $private_pair$;
        REVOKE ALL ON FUNCTION club.mobile_private_pair_allowed(
          uuid,text,text,timestamptz,uuid,text,text) FROM PUBLIC;

        CREATE FUNCTION club.private_conversation_json(
          _conversation club.app_private_conversations,_actor uuid)
        RETURNS jsonb LANGUAGE sql STABLE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_json$
          SELECT jsonb_build_object(
            'id',_conversation.id,
            'organization_id',_conversation.organization_id,
            'group_id',_conversation.context_group_id,
            'partner_id',peer.id,'partner_name',peer.display_name,
            'partner_role',peer.role,'status',_conversation.status,
            'is_initiator',_conversation.requested_by=_actor)
          FROM identity.mobile_identities peer
          WHERE peer.id=CASE WHEN _conversation.requested_by=_actor
            THEN _conversation.invited_identity_id ELSE _conversation.requested_by END
        $private_json$;
        REVOKE ALL ON FUNCTION club.private_conversation_json(
          club.app_private_conversations,uuid) FROM PUBLIC;

        CREATE FUNCTION club.read_mobile_private_contacts(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_contacts$
        DECLARE v_items jsonb;
        BEGIN
          IF _role NOT IN ('parent','coach','management') OR NOT EXISTS(
            SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
            ) AS g(value) WHERE g.value->>'organization_id'=_organization
              AND g.value->>'id'=_group) THEN
            RAISE EXCEPTION 'private_contact_denied' USING ERRCODE='42501';
          END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',contact.id,'name',contact.display_name,'role',contact.role)
            ORDER BY contact.display_name COLLATE "C",contact.id),'[]'::jsonb)
            INTO v_items
          FROM (SELECT i.id,i.display_name,i.role
            FROM identity.mobile_identities i
            WHERE i.id<>_actor AND i.is_active
              AND i.role IN ('parent','coach','management')
              AND club.mobile_private_pair_allowed(
                _actor,_phone,_role,_expires,i.id,_organization,_group)
            ORDER BY i.display_name COLLATE "C",i.id LIMIT 101
          ) contact;
          IF jsonb_array_length(v_items)>100 THEN
            RAISE EXCEPTION 'private_directory_too_large' USING ERRCODE='54000';
          END IF;
          RETURN jsonb_build_object('contacts',v_items);
        END $private_contacts$;
        REVOKE ALL ON FUNCTION club.read_mobile_private_contacts(
          uuid,text,text,timestamptz,text,text) FROM PUBLIC;

        CREATE FUNCTION club.read_mobile_private_conversations(
          _actor uuid,_phone text,_role text,_expires timestamptz)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_inbox$
        DECLARE v_items jsonb;
        BEGIN
          IF _role NOT IN ('parent','coach','management') OR NOT EXISTS(
             SELECT 1 FROM jsonb_array_elements(
               club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
             ) g(value)) THEN
            RAISE EXCEPTION 'private_inbox_denied' USING ERRCODE='42501';
          END IF;
          SELECT coalesce(jsonb_agg(
            club.private_conversation_json(c,_actor)
            ORDER BY c.updated_at DESC,c.id DESC),'[]'::jsonb)
          INTO v_items FROM (SELECT conv.*
            FROM club.app_private_conversations conv
            LEFT JOIN club.app_private_conversation_views v
              ON v.conversation_id=conv.id AND v.viewer_identity_id=_actor
            WHERE conv.status IN ('pending','accepted')
              AND _actor IN (conv.requested_by,conv.invited_identity_id)
              AND club.mobile_private_pair_allowed(
                _actor,_phone,_role,_expires,
                CASE WHEN _actor=conv.requested_by
                  THEN conv.invited_identity_id ELSE conv.requested_by END,
                conv.organization_id,conv.context_group_id)
              AND (conv.status='pending' OR v.hidden_before IS NULL OR EXISTS(
                SELECT 1 FROM club.app_private_messages m
                WHERE m.conversation_id=conv.id AND m.created_at>v.hidden_before))
            ORDER BY conv.updated_at DESC,conv.id DESC LIMIT 101) c;
          IF jsonb_array_length(v_items)>100 THEN
            RAISE EXCEPTION 'private_inbox_too_large' USING ERRCODE='54000';
          END IF;
          RETURN jsonb_build_object('conversations',v_items);
        END $private_inbox$;
        REVOKE ALL ON FUNCTION club.read_mobile_private_conversations(
          uuid,text,text,timestamptz) FROM PUBLIC;

        CREATE FUNCTION club.request_mobile_private_conversation(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_recipient uuid)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_request$
        DECLARE v_conv club.app_private_conversations%ROWTYPE;
        BEGIN
          IF _recipient IS NULL OR NOT club.mobile_private_pair_allowed(
               _actor,_phone,_role,_expires,_recipient,_organization,_group) THEN
            RAISE EXCEPTION 'private_invite_denied' USING ERRCODE='42501';
          END IF;
          INSERT INTO club.app_private_conversations(
            organization_id,context_group_id,requested_by,invited_identity_id)
          SELECT _organization,_group,_actor,_recipient
          WHERE club.mobile_private_pair_allowed(
            _actor,_phone,_role,_expires,_recipient,_organization,_group)
            AND NOT EXISTS(SELECT 1 FROM club.app_private_conversations previous
              WHERE previous.organization_id=_organization
                AND previous.context_group_id=_group
                AND _actor IN(previous.requested_by,previous.invited_identity_id)
                AND _recipient IN(previous.requested_by,previous.invited_identity_id)
                AND previous.status='declined'
                AND previous.updated_at>clock_timestamp()-interval '1 day')
          ON CONFLICT DO NOTHING
          RETURNING * INTO v_conv;
          IF NOT FOUND THEN
            SELECT * INTO v_conv FROM club.app_private_conversations existing
            WHERE existing.organization_id=_organization
              AND existing.context_group_id=_group
              AND existing.requested_by=_actor
              AND existing.invited_identity_id=_recipient
              AND existing.status='pending';
            IF NOT FOUND THEN
              RAISE EXCEPTION 'private_invitation_unavailable' USING ERRCODE='42501';
            END IF;
          ELSE
            INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,
              action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_actor,
              'app.private.invitation.request','succeeded',_organization,'consent_required');
          END IF;
          RETURN club.private_conversation_json(v_conv,_actor);
        END $private_request$;
        REVOKE ALL ON FUNCTION club.request_mobile_private_conversation(
          uuid,text,text,timestamptz,text,text,uuid) FROM PUBLIC;

        CREATE FUNCTION club.respond_mobile_private_conversation(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _conversation uuid,_accept boolean)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_respond$
        DECLARE v_conv club.app_private_conversations%ROWTYPE;
        BEGIN
          IF _conversation IS NULL OR _accept IS NULL THEN
            RAISE EXCEPTION 'private_response_invalid' USING ERRCODE='22023';
          END IF;
          UPDATE club.app_private_conversations c
          SET status=CASE WHEN _accept THEN 'accepted' ELSE 'declined' END,
            updated_at=clock_timestamp()
          WHERE c.id=_conversation AND c.status='pending'
            AND c.invited_identity_id=_actor
            AND club.mobile_private_pair_allowed(
              _actor,_phone,_role,_expires,c.requested_by,
              c.organization_id,c.context_group_id)
          RETURNING * INTO v_conv;
          IF NOT FOUND THEN RAISE EXCEPTION 'private_response_denied' USING ERRCODE='42501'; END IF;
          INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_actor,
            CASE WHEN _accept THEN 'app.private.invitation.accept'
              ELSE 'app.private.invitation.decline' END,
            'succeeded',v_conv.organization_id,'recipient_decision');
          RETURN club.private_conversation_json(v_conv,_actor);
        END $private_respond$;
        REVOKE ALL ON FUNCTION club.respond_mobile_private_conversation(
          uuid,text,text,timestamptz,uuid,boolean) FROM PUBLIC;

        CREATE FUNCTION club.read_mobile_private_messages(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _conversation uuid,_offset integer)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_messages$
        DECLARE v_conv club.app_private_conversations%ROWTYPE;
          v_hidden timestamptz;
          v_items jsonb;
        BEGIN
          IF _conversation IS NULL OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN
            RAISE EXCEPTION 'private_page_invalid' USING ERRCODE='22023';
          END IF;
          SELECT * INTO v_conv FROM club.app_private_conversations c
          WHERE c.id=_conversation AND c.status='accepted'
            AND _actor IN(c.requested_by,c.invited_identity_id)
            AND club.mobile_private_pair_allowed(
              _actor,_phone,_role,_expires,
              CASE WHEN _actor=c.requested_by THEN c.invited_identity_id
                ELSE c.requested_by END,c.organization_id,c.context_group_id);
          IF NOT FOUND THEN RAISE EXCEPTION 'private_page_denied' USING ERRCODE='42501'; END IF;
          SELECT v.hidden_before INTO v_hidden FROM club.app_private_conversation_views v
          WHERE v.conversation_id=_conversation AND v.viewer_identity_id=_actor;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',m.id,'conversation_id',m.conversation_id,
            'sender_id',m.sender_identity_id,'sender_name',m.sender_name,
            'content',m.content,'created_at',m.created_at,'edited_at',m.edited_at,
            'is_deleted',m.is_deleted,'version',m.version)
            ORDER BY m.created_at DESC,m.id DESC),'[]'::jsonb)
          INTO v_items FROM (
            SELECT msg.*,i.display_name AS sender_name
            FROM club.app_private_messages msg
            JOIN identity.mobile_identities i ON i.id=msg.sender_identity_id
            WHERE msg.conversation_id=_conversation
              AND (v_hidden IS NULL OR msg.created_at>v_hidden)
              AND EXISTS(SELECT 1 FROM club.app_private_conversations c
                WHERE c.id=_conversation AND c.status='accepted'
                  AND _actor IN(c.requested_by,c.invited_identity_id)
                  AND club.mobile_private_pair_allowed(
                    _actor,_phone,_role,_expires,
                    CASE WHEN _actor=c.requested_by THEN c.invited_identity_id
                      ELSE c.requested_by END,c.organization_id,c.context_group_id))
            ORDER BY msg.created_at DESC,msg.id DESC
            LIMIT 50 OFFSET _offset) m;
          RETURN jsonb_build_object('messages',v_items,
            'next_offset',CASE WHEN jsonb_array_length(v_items)=50
              THEN _offset+50 ELSE NULL END);
        END $private_messages$;
        REVOKE ALL ON FUNCTION club.read_mobile_private_messages(
          uuid,text,text,timestamptz,uuid,integer) FROM PUBLIC;

        CREATE FUNCTION club.write_mobile_private_message(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _conversation uuid,_operation text,_message uuid,
          _expected_version bigint,_content text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_write$
        DECLARE v_row club.app_private_messages%ROWTYPE;
          v_conv club.app_private_conversations%ROWTYPE;
          v_name text;
          v_hidden timestamptz;
        BEGIN
          IF _conversation IS NULL OR _operation NOT IN ('send','edit','delete')
            OR (_operation='send' AND (_message IS NOT NULL OR _expected_version IS NOT NULL))
            OR (_operation IN ('edit','delete') AND
              (_message IS NULL OR _expected_version IS NULL OR _expected_version<1))
            OR (_operation IN ('send','edit') AND
              (_content IS NULL OR char_length(_content) NOT BETWEEN 1 AND 10000
                OR _content<>btrim(_content) OR _content ~ '[[:cntrl:]]'))
            OR (_operation='delete' AND _content IS NOT NULL) THEN
            RAISE EXCEPTION 'private_message_invalid' USING ERRCODE='22023';
          END IF;
          SELECT * INTO v_conv FROM club.app_private_conversations c
          WHERE c.id=_conversation AND c.status='accepted'
            AND _actor IN(c.requested_by,c.invited_identity_id)
            AND club.mobile_private_pair_allowed(
              _actor,_phone,_role,_expires,
              CASE WHEN _actor=c.requested_by THEN c.invited_identity_id
                ELSE c.requested_by END,c.organization_id,c.context_group_id);
          IF NOT FOUND THEN RAISE EXCEPTION 'private_write_denied' USING ERRCODE='42501'; END IF;
          SELECT v.hidden_before INTO v_hidden FROM club.app_private_conversation_views v
            WHERE v.conversation_id=_conversation AND v.viewer_identity_id=_actor;
          IF _operation='send' THEN
            INSERT INTO club.app_private_messages(conversation_id,sender_identity_id,content)
            SELECT c.id,_actor,_content FROM club.app_private_conversations c
            WHERE c.id=_conversation AND c.status='accepted'
              AND _actor IN(c.requested_by,c.invited_identity_id)
              AND club.mobile_private_pair_allowed(
                _actor,_phone,_role,_expires,
                CASE WHEN _actor=c.requested_by THEN c.invited_identity_id
                  ELSE c.requested_by END,c.organization_id,c.context_group_id)
            RETURNING * INTO v_row;
          ELSE
            UPDATE club.app_private_messages m SET
              content=CASE WHEN _operation='delete' THEN '' ELSE _content END,
              edited_at=CASE WHEN _operation='edit' THEN clock_timestamp() ELSE m.edited_at END,
              is_deleted=(_operation='delete'),version=m.version+1
            WHERE m.id=_message AND m.conversation_id=_conversation
              AND m.sender_identity_id=_actor AND NOT m.is_deleted
              AND m.version=_expected_version
              AND (v_hidden IS NULL OR m.created_at>v_hidden)
              AND EXISTS(SELECT 1 FROM club.app_private_conversations c
                WHERE c.id=_conversation AND c.status='accepted'
                  AND _actor IN(c.requested_by,c.invited_identity_id)
                  AND club.mobile_private_pair_allowed(
                    _actor,_phone,_role,_expires,
                    CASE WHEN _actor=c.requested_by THEN c.invited_identity_id
                      ELSE c.requested_by END,c.organization_id,c.context_group_id))
            RETURNING * INTO v_row;
          END IF;
          IF NOT FOUND THEN
            IF _operation<>'send' AND EXISTS(
              SELECT 1 FROM club.app_private_messages m
              WHERE m.id=_message AND m.conversation_id=_conversation
                AND m.sender_identity_id=_actor AND NOT m.is_deleted
                AND (v_hidden IS NULL OR m.created_at>v_hidden)) THEN
              RAISE EXCEPTION 'private_message_stale' USING ERRCODE='40001';
            END IF;
            RAISE EXCEPTION 'private_write_denied' USING ERRCODE='42501';
          END IF;
          IF _operation<>'send' THEN
            INSERT INTO club.app_private_message_revisions(
              message_id,version,actor_identity_id,operation)
            VALUES(v_row.id,v_row.version,_actor,_operation);
          END IF;
          IF _operation='send' THEN
            UPDATE club.app_private_conversations SET updated_at=v_row.created_at
              WHERE id=_conversation;
          END IF;
          INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_actor,
            'app.private.message.'||_operation,'succeeded',
            v_conv.organization_id,'consented_private');
          SELECT i.display_name INTO v_name FROM identity.mobile_identities i WHERE i.id=_actor;
          RETURN jsonb_build_object(
            'id',v_row.id,'conversation_id',v_row.conversation_id,
            'sender_id',v_row.sender_identity_id,'sender_name',v_name,
            'content',v_row.content,'created_at',v_row.created_at,
            'edited_at',v_row.edited_at,'is_deleted',v_row.is_deleted,'version',v_row.version);
        END $private_write$;
        REVOKE ALL ON FUNCTION club.write_mobile_private_message(
          uuid,text,text,timestamptz,uuid,text,uuid,bigint,text) FROM PUBLIC;

        CREATE FUNCTION club.hide_mobile_private_conversation(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _conversation uuid)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_hide$
        DECLARE v_conv club.app_private_conversations%ROWTYPE;
          v_hidden timestamptz;
        BEGIN
          IF _conversation IS NULL THEN
            RAISE EXCEPTION 'private_hide_invalid' USING ERRCODE='22023';
          END IF;
          -- Only the current viewer's cursor moves; the peer's messages and
          -- the other view remain untouched. It is NOT a global delete.
          INSERT INTO club.app_private_conversation_views(
            conversation_id,viewer_identity_id,hidden_before)
          SELECT c.id,_actor,clock_timestamp() FROM club.app_private_conversations c
          WHERE c.id=_conversation AND c.status='accepted'
            AND _actor IN(c.requested_by,c.invited_identity_id)
            AND club.mobile_private_pair_allowed(
              _actor,_phone,_role,_expires,
              CASE WHEN _actor=c.requested_by THEN c.invited_identity_id
                ELSE c.requested_by END,c.organization_id,c.context_group_id)
          ON CONFLICT(conversation_id,viewer_identity_id)
          DO UPDATE SET hidden_before=clock_timestamp()
          RETURNING hidden_before INTO v_hidden;
          IF NOT FOUND THEN RAISE EXCEPTION 'private_hide_denied' USING ERRCODE='42501'; END IF;
          SELECT * INTO v_conv FROM club.app_private_conversations WHERE id=_conversation;
          INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_actor,
            'app.private.conversation.hide','succeeded',v_conv.organization_id,'viewer_only');
          RETURN jsonb_build_object('conversation_id',_conversation,'hidden',true);
        END $private_hide$;
        REVOKE ALL ON FUNCTION club.hide_mobile_private_conversation(
          uuid,text,text,timestamptz,uuid) FROM PUBLIC;

        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.hide_mobile_private_conversation(
          uuid,text,text,timestamptz,uuid);
        DROP FUNCTION club.write_mobile_private_message(
          uuid,text,text,timestamptz,uuid,text,uuid,bigint,text);
        DROP FUNCTION club.read_mobile_private_messages(
          uuid,text,text,timestamptz,uuid,integer);
        DROP FUNCTION club.respond_mobile_private_conversation(
          uuid,text,text,timestamptz,uuid,boolean);
        DROP FUNCTION club.request_mobile_private_conversation(
          uuid,text,text,timestamptz,text,text,uuid);
        DROP FUNCTION club.read_mobile_private_conversations(uuid,text,text,timestamptz);
        DROP FUNCTION club.read_mobile_private_contacts(uuid,text,text,timestamptz,text,text);
        DROP FUNCTION club.private_conversation_json(club.app_private_conversations,uuid);
        DROP FUNCTION club.mobile_private_pair_allowed(
          uuid,text,text,timestamptz,uuid,text,text);
        DROP TABLE club.app_private_conversation_views;
        DROP TABLE club.app_private_message_revisions;
        DROP TABLE club.app_private_messages;
        DROP TABLE club.app_private_conversations;
        """);
}
