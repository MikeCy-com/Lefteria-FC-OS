using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Explicit, currently roster-scoped app notice readership. Only
/// verified web management can request active member names. Disposable rollback.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108290000_AppAnnouncementReadReceipts")]
public sealed class AppAnnouncementReadReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE club.app_announcement_read_receipts (
          announcement_id varchar(128) COLLATE "C" NOT NULL
            REFERENCES club.app_announcements(id) ON DELETE RESTRICT,
          reader_identity_id uuid NOT NULL
            REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
          read_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(announcement_id,reader_identity_id)
        );
        CREATE INDEX ix_app_announcement_reads_reader
          ON club.app_announcement_read_receipts(reader_identity_id);
        REVOKE ALL ON TABLE club.app_announcement_read_receipts FROM PUBLIC;

        CREATE FUNCTION club.ack_mobile_app_announcement_read(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_announcement text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $notice_ack$
        DECLARE v_announcement text;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
             OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
             OR _announcement IS NULL OR _announcement !~ '^ann_[0-9a-f]{32}$' THEN
            RAISE EXCEPTION 'notice_read_invalid' USING ERRCODE='22023';
          END IF;
          -- Displaying/fetching a list does NOT call this procedure. A
          -- visible, focused card in the member app triggers explicit POST.
          -- SHARE conflicts with the admin's notice-removal UPDATE.
          SELECT a.id INTO v_announcement FROM club.app_announcements a
          WHERE a.id=_announcement AND a.organization_id=_organization
            AND a.group_id=_group AND a.removed_at IS NULL
            AND EXISTS(SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
            ) AS member(value) WHERE member.value->>'organization_id'=_organization
              AND member.value->>'id'=_group)
          FOR SHARE OF a;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'notice_read_denied' USING ERRCODE='42501';
          END IF;
          INSERT INTO club.app_announcement_read_receipts(
            announcement_id,reader_identity_id)
          SELECT a.id,_actor FROM club.app_announcements a
          WHERE a.id=v_announcement AND a.organization_id=_organization
            AND a.group_id=_group AND a.removed_at IS NULL
            AND EXISTS(SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
            ) AS member(value) WHERE member.value->>'organization_id'=_organization
              AND member.value->>'id'=_group)
          ON CONFLICT DO NOTHING;
          -- An existing receipt is a successful, idempotent acknowledgement.
          RETURN jsonb_build_object('announcement_id',v_announcement,'seen',true);
        END $notice_ack$;
        REVOKE ALL ON FUNCTION club.ack_mobile_app_announcement_read(
          uuid,text,text,timestamptz,text,text,text) FROM PUBLIC;

        CREATE FUNCTION club.read_admin_app_announcement_readers(
          _digest text,_owner text,_announcement text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $notice_admin_readers$
        DECLARE v_group text;
          v_readers jsonb;
          v_actor uuid;
        BEGIN
          IF _announcement IS NULL OR _announcement !~ '^ann_[0-9a-f]{32}$' THEN
            RAISE EXCEPTION 'notice_read_invalid' USING ERRCODE='22023';
          END IF;
          -- This locks and rechecks the active admin session, management
          -- rank and exact FC or Academy grant, never a mobile bearer.
          v_actor:=club.app_announcement_admin_actor(_digest,_owner);
          SELECT a.group_id INTO v_group
          FROM club.app_announcements a
          WHERE a.id=_announcement AND a.organization_id=_owner
            AND a.removed_at IS NULL;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'notice_read_denied' USING ERRCODE='42501';
          END IF;
          -- Historical receipts never justify disclosing a person's name
          -- after their canonical membership or active role is revoked.
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',current_reader.id,'name',current_reader.display_name)
            ORDER BY current_reader.display_name COLLATE "C",current_reader.id),'[]'::jsonb)
          INTO v_readers
          FROM (SELECT i.id,i.display_name
            FROM club.app_announcement_read_receipts r
            JOIN identity.mobile_identities i ON i.id=r.reader_identity_id
            WHERE r.announcement_id=_announcement
              AND club.is_current_mobile_chat_reader(i.id,_owner,v_group)
            ORDER BY i.display_name COLLATE "C",i.id LIMIT 151) current_reader;
          IF jsonb_array_length(v_readers)>150 THEN
            RAISE EXCEPTION 'notice_read_limit' USING ERRCODE='54000';
          END IF;
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.app.announcement.readers.view','succeeded',_owner,
            'current_member_names');
          RETURN jsonb_build_object('announcement_id',_announcement,'readers',v_readers);
        END $notice_admin_readers$;
        REVOKE ALL ON FUNCTION club.read_admin_app_announcement_readers(
          text,text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.read_admin_app_announcement_readers(text,text,text);
        DROP FUNCTION club.ack_mobile_app_announcement_read(
          uuid,text,text,timestamptz,text,text,text);
        DROP TABLE club.app_announcement_read_receipts;
        """);
}
