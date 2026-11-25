using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125071000_MobilePrivateChatReadReceipts")]
public sealed class MobilePrivateChatReadReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE club.app_private_read_receipts (
          message_id uuid NOT NULL REFERENCES club.app_private_messages(id) ON DELETE RESTRICT,
          reader_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
          message_version bigint NOT NULL CHECK (message_version>=1),
          read_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(message_id,reader_identity_id)
        );
        REVOKE ALL ON TABLE club.app_private_read_receipts FROM PUBLIC;

        CREATE FUNCTION club.ack_mobile_private_read(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _conversation uuid,_message uuid,_version bigint)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_seen$
        DECLARE v_id uuid;
        BEGIN
          IF _conversation IS NULL OR _message IS NULL OR _version IS NULL OR _version<1 THEN
            RAISE EXCEPTION 'private_receipt_invalid' USING ERRCODE='22023';
          END IF;
          -- Lock message version against edit/delete, then repeat full
          -- consent, roster and viewer-history checks in the receipt write.
          SELECT m.id INTO v_id FROM club.app_private_messages m
          JOIN club.app_private_conversations c ON c.id=m.conversation_id
          WHERE c.id=_conversation AND c.status='accepted'
            AND _actor IN(c.requested_by,c.invited_identity_id)
            AND m.id=_message AND m.version=_version AND NOT m.is_deleted
            AND m.sender_identity_id<>_actor
            AND m.sender_identity_id IN(c.requested_by,c.invited_identity_id)
            AND club.mobile_private_pair_allowed(_actor,_phone,_role,_expires,
              CASE WHEN _actor=c.requested_by THEN c.invited_identity_id
                ELSE c.requested_by END,c.organization_id,c.context_group_id)
            AND NOT EXISTS(SELECT 1 FROM club.app_private_conversation_views v
              WHERE v.conversation_id=c.id AND v.viewer_identity_id=_actor
                AND m.created_at<=v.hidden_before)
          FOR SHARE OF m;
          IF NOT FOUND THEN RAISE EXCEPTION 'private_receipt_denied' USING ERRCODE='42501'; END IF;

          INSERT INTO club.app_private_read_receipts AS receipt
            (message_id,reader_identity_id,message_version)
          SELECT m.id,_actor,m.version FROM club.app_private_messages m
          JOIN club.app_private_conversations c ON c.id=m.conversation_id
          WHERE c.id=_conversation AND c.status='accepted'
            AND _actor IN(c.requested_by,c.invited_identity_id)
            AND m.id=v_id AND m.version=_version AND NOT m.is_deleted
            AND m.sender_identity_id<>_actor
            AND m.sender_identity_id IN(c.requested_by,c.invited_identity_id)
            AND club.mobile_private_pair_allowed(_actor,_phone,_role,_expires,
              CASE WHEN _actor=c.requested_by THEN c.invited_identity_id
                ELSE c.requested_by END,c.organization_id,c.context_group_id)
            AND NOT EXISTS(SELECT 1 FROM club.app_private_conversation_views v
              WHERE v.conversation_id=c.id AND v.viewer_identity_id=_actor
                AND m.created_at<=v.hidden_before)
          ON CONFLICT(message_id,reader_identity_id) DO UPDATE
            SET message_version=EXCLUDED.message_version,
                read_at=CASE WHEN receipt.message_version=EXCLUDED.message_version
                  THEN receipt.read_at ELSE clock_timestamp() END
          RETURNING message_id INTO v_id;
          IF NOT FOUND THEN RAISE EXCEPTION 'private_receipt_denied' USING ERRCODE='42501'; END IF;
          RETURN jsonb_build_object('conversation_id',_conversation,'message_id',v_id,
            'seen',true,'version',_version);
        END $private_seen$;
        REVOKE ALL ON FUNCTION club.ack_mobile_private_read(
          uuid,text,text,timestamptz,uuid,uuid,bigint) FROM PUBLIC;

        CREATE FUNCTION club.read_mobile_private_unread_summary(
          _actor uuid,_phone text,_role text,_expires timestamptz)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $private_unread$
        DECLARE v_opted_out boolean;
          v_count integer;
        BEGIN
          IF _role NOT IN ('parent','coach','management') OR NOT EXISTS(
            SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups') g(value)
          ) THEN RAISE EXCEPTION 'private_unread_denied' USING ERRCODE='42501'; END IF;
          SELECT coalesce(i.notification_preferences @> '{"messages":false}'::jsonb,false)
          INTO v_opted_out FROM identity.mobile_identities i
          WHERE i.id=_actor AND i.normalized_phone=_phone AND i.role=_role
            AND i.is_active AND _expires>statement_timestamp();
          IF NOT FOUND THEN RAISE EXCEPTION 'private_unread_denied' USING ERRCODE='42501'; END IF;
          IF v_opted_out THEN
            RETURN jsonb_build_object('unread_count',0,'enabled',false);
          END IF;
          WITH permitted AS MATERIALIZED (
            SELECT c.id,v.hidden_before FROM club.app_private_conversations c
            LEFT JOIN club.app_private_conversation_views v
              ON v.conversation_id=c.id AND v.viewer_identity_id=_actor
            WHERE c.status='accepted' AND _actor IN(c.requested_by,c.invited_identity_id)
              AND club.mobile_private_pair_allowed(_actor,_phone,_role,_expires,
                CASE WHEN _actor=c.requested_by THEN c.invited_identity_id
                  ELSE c.requested_by END,c.organization_id,c.context_group_id)
          )
          SELECT count(*)::integer INTO v_count FROM (
            SELECT 1 FROM permitted p JOIN club.app_private_messages m ON m.conversation_id=p.id
            WHERE NOT m.is_deleted AND m.sender_identity_id<>_actor
              AND (p.hidden_before IS NULL OR m.created_at>p.hidden_before)
              AND NOT EXISTS(SELECT 1 FROM club.app_private_read_receipts r
                WHERE r.message_id=m.id AND r.reader_identity_id=_actor
                  AND r.message_version=m.version)
            LIMIT 999
          ) unread;
          RETURN jsonb_build_object('unread_count',v_count,'enabled',true);
        END $private_unread$;
        REVOKE ALL ON FUNCTION club.read_mobile_private_unread_summary(
          uuid,text,text,timestamptz) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.read_mobile_private_unread_summary(uuid,text,text,timestamptz);
        DROP FUNCTION club.ack_mobile_private_read(uuid,text,text,timestamptz,uuid,uuid,bigint);
        DROP TABLE club.app_private_read_receipts;
        """);
}
