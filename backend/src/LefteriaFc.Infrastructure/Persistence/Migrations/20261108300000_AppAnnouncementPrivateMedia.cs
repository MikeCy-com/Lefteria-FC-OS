using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private bounded banner/PDF media for official app-only notices.
/// Every admin mutation is version checked, scoped and audited. Raw bytes are
/// never publicly served and are not included in draft/announcement lists.
/// Down is only for disposable databases; production rollbacks require review.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108300000_AppAnnouncementPrivateMedia")]
public sealed class AppAnnouncementPrivateMedia : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE club.app_announcement_media (
          id uuid PRIMARY KEY,
          announcement_id varchar(128) COLLATE "C" NOT NULL
            REFERENCES club.admin_announcement_drafts(id) ON DELETE RESTRICT,
          kind text NOT NULL CHECK (kind IN ('banner','attachment')),
          file_name varchar(96) NOT NULL
            CHECK (file_name COLLATE "C" ~ '^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,95}$'),
          content_type text NOT NULL,
          content bytea NOT NULL,
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          CONSTRAINT ck_app_announcement_media_payload CHECK (
            (kind='banner' AND content_type='image/jpeg'
             AND file_name='banner.jpg' AND octet_length(content) BETWEEN 4 AND 1048576)
            OR (kind='attachment' AND content_type='application/pdf'
             AND file_name ~* '^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,91}\.pdf$'
             AND octet_length(content) BETWEEN 8 AND 2097152))
        );
        CREATE UNIQUE INDEX ux_app_announcement_single_banner
          ON club.app_announcement_media(announcement_id) WHERE kind='banner';
        CREATE INDEX ix_app_announcement_media_order
          ON club.app_announcement_media(announcement_id,kind,created_at,id);
        REVOKE ALL ON TABLE club.app_announcement_media FROM PUBLIC;

        CREATE FUNCTION club.write_admin_app_announcement_media(
          _digest text,_owner text,_announcement text,_version bigint,
          _media uuid,_kind text,_name text,_type text,_content bytea)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $media_write$
        DECLARE v_actor uuid;
          v_draft club.admin_announcement_drafts%ROWTYPE;
          v_media jsonb;
        BEGIN
          IF _announcement IS NULL OR _announcement !~ '^ann_[0-9a-f]{32}$'
            OR _version IS NULL OR _version<1 OR _media IS NULL
            OR _kind NOT IN ('banner','attachment')
            OR _name IS NULL OR _name COLLATE "C" !~ '^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,95}$'
            OR _content IS NULL
            OR (_kind='banner' AND (_name<>'banner.jpg' OR _type<>'image/jpeg'
              OR octet_length(_content) NOT BETWEEN 4 AND 1048576
              OR substring(_content from 1 for 2)<>decode('ffd8','hex')
              OR substring(_content from octet_length(_content)-1 for 2)<>decode('ffd9','hex')))
            OR (_kind='attachment' AND (_name !~* '^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,91}\.pdf$'
              OR _type<>'application/pdf' OR octet_length(_content) NOT BETWEEN 8 AND 2097152
              OR substring(_content from 1 for 5)<>decode('255044462d','hex')
              OR substring(_content from octet_length(_content)-4 for 5)<>decode('2525454f46','hex'))) THEN
            RAISE EXCEPTION 'media_invalid' USING ERRCODE='22023';
          END IF;
          v_actor:=club.app_announcement_admin_actor(_digest,_owner);
          SELECT * INTO v_draft FROM club.admin_announcement_drafts d
          WHERE d.id=_announcement AND d.organization_id=_owner
            AND d.status='draft' FOR UPDATE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'media_denied' USING ERRCODE='42501';
          END IF;
          IF v_draft.version<>_version THEN
            RAISE EXCEPTION 'media_stale' USING ERRCODE='40001';
          END IF;
          IF _kind='banner' THEN
            DELETE FROM club.app_announcement_media
            WHERE announcement_id=_announcement AND kind='banner';
          ELSIF (SELECT count(*) FROM club.app_announcement_media
            WHERE announcement_id=_announcement AND kind='attachment')>=3 THEN
            RAISE EXCEPTION 'media_limit' USING ERRCODE='54000';
          END IF;
          INSERT INTO club.app_announcement_media(
            id,announcement_id,kind,file_name,content_type,content)
          VALUES(_media,_announcement,_kind,_name,_type,_content);
          UPDATE club.admin_announcement_drafts
          SET version=version+1 WHERE id=_announcement AND organization_id=_owner;
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.app.announcement.media.add','succeeded',_owner,_kind);
          SELECT coalesce((SELECT jsonb_agg(jsonb_build_object(
              'id',f.id,'kind',f.kind,'file_name',f.file_name,
              'content_type',f.content_type,'size',octet_length(f.content))
            ORDER BY f.kind,f.created_at,f.id)
            FROM club.app_announcement_media f
            WHERE f.announcement_id=_announcement),'[]'::jsonb) INTO v_media;
          RETURN jsonb_build_object('announcement_id',_announcement,
            'version',v_draft.version+1,'media',v_media);
        END $media_write$;
        REVOKE ALL ON FUNCTION club.write_admin_app_announcement_media(
          text,text,text,bigint,uuid,text,text,text,bytea) FROM PUBLIC;

        CREATE FUNCTION club.remove_admin_app_announcement_media(
          _digest text,_owner text,_announcement text,_version bigint,_media uuid)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $media_remove$
        DECLARE v_actor uuid;
          v_draft club.admin_announcement_drafts%ROWTYPE;
          v_media jsonb;
        BEGIN
          IF _announcement IS NULL OR _announcement !~ '^ann_[0-9a-f]{32}$'
            OR _version IS NULL OR _version<1 OR _media IS NULL THEN
            RAISE EXCEPTION 'media_invalid' USING ERRCODE='22023';
          END IF;
          v_actor:=club.app_announcement_admin_actor(_digest,_owner);
          SELECT * INTO v_draft FROM club.admin_announcement_drafts d
          WHERE d.id=_announcement AND d.organization_id=_owner AND d.status='draft'
          FOR UPDATE;
          IF NOT FOUND THEN RAISE EXCEPTION 'media_denied' USING ERRCODE='42501'; END IF;
          IF v_draft.version<>_version THEN
            RAISE EXCEPTION 'media_stale' USING ERRCODE='40001';
          END IF;
          DELETE FROM club.app_announcement_media
          WHERE id=_media AND announcement_id=_announcement;
          IF NOT FOUND THEN RAISE EXCEPTION 'media_denied' USING ERRCODE='42501'; END IF;
          UPDATE club.admin_announcement_drafts
          SET version=version+1 WHERE id=_announcement AND organization_id=_owner;
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.app.announcement.media.remove','succeeded',_owner,'draft_only');
          SELECT coalesce((SELECT jsonb_agg(jsonb_build_object(
              'id',f.id,'kind',f.kind,'file_name',f.file_name,
              'content_type',f.content_type,'size',octet_length(f.content))
            ORDER BY f.kind,f.created_at,f.id)
            FROM club.app_announcement_media f
            WHERE f.announcement_id=_announcement),'[]'::jsonb) INTO v_media;
          RETURN jsonb_build_object('announcement_id',_announcement,
            'version',v_draft.version+1,'media',v_media);
        END $media_remove$;
        REVOKE ALL ON FUNCTION club.remove_admin_app_announcement_media(
          text,text,text,bigint,uuid) FROM PUBLIC;

        CREATE FUNCTION club.read_mobile_app_announcement_media(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_announcement text,_media uuid)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $media_read$
        DECLARE v_item jsonb;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _announcement IS NULL OR _announcement !~ '^ann_[0-9a-f]{32}$'
            OR _media IS NULL THEN
            RAISE EXCEPTION 'media_invalid' USING ERRCODE='22023';
          END IF;
          -- A published notice and the caller's current canonical roster
          -- membership are checked on EVERY individual binary retrieval.
          SELECT jsonb_build_object(
            'id',f.id,'kind',f.kind,'file_name',f.file_name,
            'content_type',f.content_type,'size',octet_length(f.content),
            'base64',encode(f.content,'base64')) INTO v_item
          FROM club.app_announcement_media f
          JOIN club.app_announcements a ON a.id=f.announcement_id
          WHERE f.id=_media AND a.id=_announcement
            AND a.organization_id=_organization AND a.group_id=_group
            AND a.removed_at IS NULL AND
            EXISTS(SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
            ) AS current_member(value)
            WHERE current_member.value->>'organization_id'=_organization
              AND current_member.value->>'id'=_group)
          FOR SHARE OF a;
          IF NOT FOUND THEN RAISE EXCEPTION 'media_denied' USING ERRCODE='42501'; END IF;
          RETURN v_item;
        END $media_read$;
        REVOKE ALL ON FUNCTION club.read_mobile_app_announcement_media(
          uuid,text,text,timestamptz,text,text,text,uuid) FROM PUBLIC;

        CREATE OR REPLACE FUNCTION club.read_admin_announcement_drafts(
          _digest text,_owner text
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $draft_read$
        DECLARE
          v_role text;
          v_drafts jsonb;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
            RAISE EXCEPTION 'draft_invalid_owner' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.role INTO v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
          IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
            RAISE EXCEPTION 'draft_denied' USING ERRCODE='42501';
          END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',d.id,'organization_id',d.organization_id,
            'group_id',d.group_id,'title',d.title,'body',d.body,
            'status',d.status,'version',d.version,
            'media',coalesce((SELECT jsonb_agg(jsonb_build_object(
              'id',f.id,'kind',f.kind,'file_name',f.file_name,
              'content_type',f.content_type,'size',octet_length(f.content))
            ORDER BY f.kind,f.created_at,f.id)
            FROM club.app_announcement_media f
            WHERE f.announcement_id=d.id),'[]'::jsonb))
            ORDER BY d.created_at DESC,d.id),'[]'::jsonb)
          INTO v_drafts
          FROM (SELECT * FROM club.admin_announcement_drafts
            WHERE organization_id=_owner AND status='draft'
            ORDER BY created_at DESC,id LIMIT 100) d;
          RETURN jsonb_build_object('organization_id',_owner,'drafts',v_drafts);
        END;
        $draft_read$;
        CREATE OR REPLACE FUNCTION club.read_admin_app_announcements(
          _digest text,_owner text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $admin_announcements$
        DECLARE v_items jsonb;
          v_actor uuid;
        BEGIN
          v_actor:=club.app_announcement_admin_actor(_digest,_owner);
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',a.id,'organization_id',a.organization_id,
            'group_id',a.group_id,'title',a.title,'body',a.body,
            'published_at',a.published_at,
            'status','published','version',a.version,
            'media',coalesce((SELECT jsonb_agg(jsonb_build_object(
              'id',f.id,'kind',f.kind,'file_name',f.file_name,
              'content_type',f.content_type,'size',octet_length(f.content))
            ORDER BY f.kind,f.created_at,f.id)
            FROM club.app_announcement_media f
            WHERE f.announcement_id=a.id),'[]'::jsonb))
            ORDER BY a.published_at DESC,a.id DESC),'[]'::jsonb)
          INTO v_items FROM (SELECT * FROM club.app_announcements
            WHERE organization_id=_owner AND removed_at IS NULL
            ORDER BY published_at DESC,id DESC LIMIT 101) a;
          IF jsonb_array_length(v_items)>100 THEN
            RAISE EXCEPTION 'app_announcement_list_limit' USING ERRCODE='54000';
          END IF;
          RETURN jsonb_build_object('organization_id',_owner,'announcements',v_items);
        END $admin_announcements$;
        CREATE OR REPLACE FUNCTION club.read_mobile_app_announcements(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_offset integer)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $mobile_announcements$
        DECLARE v_items jsonb;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN
            RAISE EXCEPTION 'app_announcement_invalid' USING ERRCODE='22023';
          END IF;
          IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(
            club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
          ) AS scoped(value)
            WHERE scoped.value->>'organization_id'=_organization
              AND scoped.value->>'id'=_group) THEN
            RAISE EXCEPTION 'app_announcement_denied' USING ERRCODE='42501';
          END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',item.id,'organization_id',item.organization_id,
            'group_id',item.group_id,'title',item.title,'body',item.body,
            'published_at',item.published_at,'status','published',
            'reactions',coalesce((SELECT jsonb_agg(jsonb_build_object(
              'emoji',reaction_totals.emoji,'count',reaction_totals.total,
              'mine',reaction_totals.mine
            ) ORDER BY reaction_totals.emoji COLLATE "C")
              FROM (SELECT r.emoji,count(*)::integer AS total,
                bool_or(r.actor_identity_id=_actor) AS mine
                FROM club.app_announcement_reactions r
                WHERE r.announcement_id=item.id
                GROUP BY r.emoji) reaction_totals),'[]'::jsonb),
            'media',coalesce((SELECT jsonb_agg(jsonb_build_object(
              'id',f.id,'kind',f.kind,'file_name',f.file_name,
              'content_type',f.content_type,'size',octet_length(f.content))
            ORDER BY f.kind,f.created_at,f.id)
            FROM club.app_announcement_media f
            WHERE f.announcement_id=item.id),'[]'::jsonb))
            ORDER BY item.published_at DESC,item.id DESC),'[]'::jsonb)
          INTO v_items FROM (
            SELECT a.* FROM club.app_announcements a
            WHERE a.organization_id=_organization AND a.group_id=_group
              AND a.removed_at IS NULL
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
              ) AS scoped(value)
                WHERE scoped.value->>'organization_id'=_organization
                  AND scoped.value->>'id'=_group)
            ORDER BY a.published_at DESC,a.id DESC
            LIMIT 50 OFFSET _offset
          ) item;
          RETURN jsonb_build_object('announcements',v_items,
            'next_offset',CASE WHEN jsonb_array_length(v_items)=50
              THEN _offset+50 ELSE NULL END);
        END $mobile_announcements$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION club.read_mobile_app_announcements(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_offset integer)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $mobile_announcements$
        DECLARE v_items jsonb;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN
            RAISE EXCEPTION 'app_announcement_invalid' USING ERRCODE='22023';
          END IF;
          IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(
            club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
          ) AS scoped(value)
            WHERE scoped.value->>'organization_id'=_organization
              AND scoped.value->>'id'=_group) THEN
            RAISE EXCEPTION 'app_announcement_denied' USING ERRCODE='42501';
          END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',item.id,'organization_id',item.organization_id,
            'group_id',item.group_id,'title',item.title,'body',item.body,
            'published_at',item.published_at,'status','published',
            'reactions',coalesce((SELECT jsonb_agg(jsonb_build_object(
              'emoji',reaction_totals.emoji,'count',reaction_totals.total,
              'mine',reaction_totals.mine
            ) ORDER BY reaction_totals.emoji COLLATE "C")
              FROM (SELECT r.emoji,count(*)::integer AS total,
                bool_or(r.actor_identity_id=_actor) AS mine
                FROM club.app_announcement_reactions r
                WHERE r.announcement_id=item.id
                GROUP BY r.emoji) reaction_totals),'[]'::jsonb))
            ORDER BY item.published_at DESC,item.id DESC),'[]'::jsonb)
          INTO v_items FROM (
            SELECT a.* FROM club.app_announcements a
            WHERE a.organization_id=_organization AND a.group_id=_group
              AND a.removed_at IS NULL
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
              ) AS scoped(value)
                WHERE scoped.value->>'organization_id'=_organization
                  AND scoped.value->>'id'=_group)
            ORDER BY a.published_at DESC,a.id DESC
            LIMIT 50 OFFSET _offset
          ) item;
          RETURN jsonb_build_object('announcements',v_items,
            'next_offset',CASE WHEN jsonb_array_length(v_items)=50
              THEN _offset+50 ELSE NULL END);
        END $mobile_announcements$;
        CREATE OR REPLACE FUNCTION club.read_admin_app_announcements(
          _digest text,_owner text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $admin_announcements$
        DECLARE v_items jsonb;
          v_actor uuid;
        BEGIN
          v_actor:=club.app_announcement_admin_actor(_digest,_owner);
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',a.id,'organization_id',a.organization_id,
            'group_id',a.group_id,'title',a.title,'body',a.body,
            'published_at',a.published_at,
            'status','published','version',a.version)
            ORDER BY a.published_at DESC,a.id DESC),'[]'::jsonb)
          INTO v_items FROM (SELECT * FROM club.app_announcements
            WHERE organization_id=_owner AND removed_at IS NULL
            ORDER BY published_at DESC,id DESC LIMIT 101) a;
          IF jsonb_array_length(v_items)>100 THEN
            RAISE EXCEPTION 'app_announcement_list_limit' USING ERRCODE='54000';
          END IF;
          RETURN jsonb_build_object('organization_id',_owner,'announcements',v_items);
        END $admin_announcements$;
        CREATE OR REPLACE FUNCTION club.read_admin_announcement_drafts(
          _digest text,_owner text
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $draft_read$
        DECLARE
          v_role text;
          v_drafts jsonb;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
            RAISE EXCEPTION 'draft_invalid_owner' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.role INTO v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
          IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
            RAISE EXCEPTION 'draft_denied' USING ERRCODE='42501';
          END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',d.id,'organization_id',d.organization_id,
            'group_id',d.group_id,'title',d.title,'body',d.body,
            'status',d.status,'version',d.version)
            ORDER BY d.created_at DESC,d.id),'[]'::jsonb)
          INTO v_drafts
          FROM (SELECT * FROM club.admin_announcement_drafts
            WHERE organization_id=_owner AND status='draft'
            ORDER BY created_at DESC,id LIMIT 100) d;
          RETURN jsonb_build_object('organization_id',_owner,'drafts',v_drafts);
        END;
        $draft_read$;
        DROP FUNCTION club.read_mobile_app_announcement_media(
          uuid,text,text,timestamptz,text,text,text,uuid);
        DROP FUNCTION club.remove_admin_app_announcement_media(
          text,text,text,bigint,uuid);
        DROP FUNCTION club.write_admin_app_announcement_media(
          text,text,text,bigint,uuid,text,text,text,bytea);
        DROP TABLE club.app_announcement_media;
        """);
}
