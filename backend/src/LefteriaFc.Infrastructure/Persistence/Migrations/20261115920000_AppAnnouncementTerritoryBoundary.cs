using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Enforce exact First Team or Academy city+location territory for every
/// private member-app announcement admin operation. No new notification
/// delivery or public content authority. Disposable rollback only.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115920000_AppAnnouncementTerritoryBoundary")]
public sealed class AppAnnouncementTerritoryBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""

        -- This internal, non-callable-by-runtime helper is deliberately tied
        -- to the live canonical football topology. Merely holding an FC or
        -- Academy organization grant never authorizes another site.
        CREATE FUNCTION club.admin_app_notice_group_allowed(
          _actor uuid,_owner text,_group text)
        RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $notice_scope$
        DECLARE v_role text; v_scope uuid; v_location uuid;
        BEGIN
          IF _actor IS NULL OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR _group !~ '^[a-zA-Z0-9_-]{1,128}$' THEN
            RETURN false;
          END IF;
          SELECT a.role INTO v_role FROM identity.admin_accounts a
            WHERE a.id=_actor AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled
              AND a.password_hash IS NOT NULL AND a.role IN ('manager','super_admin');
          IF NOT FOUND THEN RETURN false; END IF;

          IF _owner='lefteria_fc' THEN
            SELECT s.id INTO v_scope
              FROM club.teams t
              JOIN club.football_operational_scopes s
                ON s.organization_id=t.organization_id AND s.team_id=t.id
              WHERE t.organization_id='lefteria_fc' AND t.id=_group;
            RETURN v_scope IS NOT NULL AND club.admin_football_allowed(
              _actor,v_role,_owner,v_scope,NULL,true);
          END IF;

          SELECT m.scope_id,m.location_id INTO v_scope,v_location
            FROM club.academy_group_location_assignments m
            JOIN club.academy_groups g
              ON g.organization_id=m.organization_id AND g.id=m.group_id
            JOIN club.football_locations l
              ON l.id=m.location_id AND l.city_id=m.city_id AND l.is_active
            JOIN club.football_cities c
              ON c.id=m.city_id AND c.is_active
            JOIN club.football_operational_scopes s
              ON s.id=m.scope_id AND s.organization_id=m.organization_id
                AND s.academy_city_id=m.city_id
            WHERE m.organization_id='lefteria_fc_academy' AND m.group_id=_group;
          RETURN v_scope IS NOT NULL AND v_location IS NOT NULL
            AND club.admin_football_allowed(_actor,v_role,_owner,
              v_scope,v_location,true);
        END $notice_scope$;
        REVOKE ALL ON FUNCTION club.admin_app_notice_group_allowed(
          uuid,text,text) FROM PUBLIC;

CREATE OR REPLACE FUNCTION club.create_admin_announcement_draft(
          _digest text,_owner text,_id text,_group text,_title text,_body text
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $draft_write$
        DECLARE
          v_actor uuid;
          v_session uuid;
          v_role text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _id IS NULL OR _id !~ '^ann_[0-9a-f]{32}$'
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _group !~ '^[a-zA-Z0-9_-]+$'
            OR _title IS NULL OR char_length(_title) NOT BETWEEN 1 AND 140
            OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
            OR _body IS NULL OR char_length(_body) NOT BETWEEN 1 AND 4000
            OR _body<>btrim(_body) THEN
            RAISE EXCEPTION 'draft_invalid' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
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
          IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
            RAISE EXCEPTION 'draft_roster_unready' USING ERRCODE='55000';
          END IF;
          IF (_owner='lefteria_fc' AND NOT EXISTS(
              SELECT 1 FROM club.teams t
              WHERE t.organization_id=_owner AND t.id=_group))
            OR (_owner='lefteria_fc_academy' AND NOT EXISTS(
              SELECT 1 FROM club.academy_groups g
              WHERE g.organization_id=_owner AND g.id=_group)) THEN
            RAISE EXCEPTION 'draft_group_invalid' USING ERRCODE='22023';
          END IF;
          -- Reject cross-city, neighboring-location, FC/Academy and unassigned
          -- targets BEFORE any private draft/audit mutation.
          IF NOT club.admin_app_notice_group_allowed(v_actor,_owner,_group) THEN
            RAISE EXCEPTION 'notice_territory_denied' USING ERRCODE='42501';
          END IF;
          -- Draft target remains a canonical group reference, not a saved
          -- list of children, parents, contacts, or recipients.
          INSERT INTO club.admin_announcement_drafts(
            id,organization_id,group_id,title,body,created_by)
          VALUES(_id,_owner,_group,_title,_body,v_actor);
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,
            device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
            'admin.announcement.draft.create','succeeded',_owner,'announcement_draft');
          RETURN jsonb_build_object('id',_id,'organization_id',_owner,
            'group_id',_group,'title',_title,'body',_body,'status','draft','version',1);
        END;
        $draft_write$;

CREATE OR REPLACE FUNCTION club.read_admin_announcement_drafts(
          _digest text,_owner text
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $draft_read$
        DECLARE
          v_role text;
          v_actor uuid;
          v_drafts jsonb;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
            RAISE EXCEPTION 'draft_invalid_owner' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.id,a.role INTO v_actor,v_role
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
              AND club.admin_app_notice_group_allowed(v_actor,_owner,group_id)
            ORDER BY created_at DESC,id LIMIT 100) d;
          RETURN jsonb_build_object('organization_id',_owner,'drafts',v_drafts);
        END;
        $draft_read$;

CREATE OR REPLACE FUNCTION club.publish_admin_app_announcement(
          _digest text,_owner text,_id text,_expected_version bigint)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $app_publish$
        DECLARE v_actor uuid;
          v_draft club.admin_announcement_drafts%ROWTYPE;
          v_item club.app_announcements%ROWTYPE;
        BEGIN
          IF _id IS NULL OR _id !~ '^ann_[0-9a-f]{32}$'
            OR _expected_version IS NULL OR _expected_version<1 THEN
            RAISE EXCEPTION 'app_announcement_invalid' USING ERRCODE='22023';
          END IF;
          v_actor:=club.app_announcement_admin_actor(_digest,_owner);
          -- A real canonical target remains compulsory AT PUBLISH TIME.
          UPDATE club.admin_announcement_drafts d
          SET status='published',version=version+1
          WHERE d.id=_id AND d.organization_id=_owner
            AND d.status='draft' AND d.version=_expected_version
            AND club.admin_app_notice_group_allowed(v_actor,_owner,d.group_id)
            AND EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
            AND ((
              _owner='lefteria_fc' AND EXISTS(
                SELECT 1 FROM club.teams t
                WHERE t.organization_id=_owner AND t.id=d.group_id))
              OR (_owner='lefteria_fc_academy' AND EXISTS(
                SELECT 1 FROM club.academy_groups g
                WHERE g.organization_id=_owner AND g.id=d.group_id)))
          RETURNING * INTO v_draft;
          IF NOT FOUND THEN
            IF EXISTS(SELECT 1 FROM club.admin_announcement_drafts d
              WHERE d.id=_id AND d.organization_id=_owner AND d.status='draft'
                AND NOT club.admin_app_notice_group_allowed(v_actor,_owner,d.group_id)) THEN
              RAISE EXCEPTION 'notice_territory_denied' USING ERRCODE='42501';
            END IF;
            IF EXISTS(SELECT 1 FROM club.admin_announcement_drafts d
              WHERE d.id=_id AND d.organization_id=_owner AND d.status='draft')
            THEN RAISE EXCEPTION 'app_announcement_stale' USING ERRCODE='40001';
            END IF;
            RAISE EXCEPTION 'app_announcement_denied' USING ERRCODE='42501';
          END IF;
          INSERT INTO club.app_announcements(
            id,organization_id,group_id,title,body,published_by)
          VALUES(v_draft.id,_owner,v_draft.group_id,v_draft.title,v_draft.body,v_actor)
          RETURNING * INTO v_item;
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.app.announcement.publish','succeeded',_owner,'canonical_group');
          RETURN jsonb_build_object(
            'id',v_item.id,'organization_id',_owner,'group_id',v_item.group_id,
            'title',v_item.title,'body',v_item.body,
            'published_at',v_item.published_at,'status','published',
            'version',v_item.version);
        END $app_publish$;

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
              AND club.admin_app_notice_group_allowed(v_actor,_owner,group_id)
            ORDER BY published_at DESC,id DESC LIMIT 101) a;
          IF jsonb_array_length(v_items)>100 THEN
            RAISE EXCEPTION 'app_announcement_list_limit' USING ERRCODE='54000';
          END IF;
          RETURN jsonb_build_object('organization_id',_owner,'announcements',v_items);
        END $admin_announcements$;

CREATE OR REPLACE FUNCTION club.remove_admin_app_announcement(
          _digest text,_owner text,_id text,_expected_version bigint)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $app_remove$
        DECLARE v_actor uuid;
          v_item club.app_announcements%ROWTYPE;
        BEGIN
          IF _id IS NULL OR _id !~ '^ann_[0-9a-f]{32}$'
            OR _expected_version IS NULL OR _expected_version<1 THEN
            RAISE EXCEPTION 'app_announcement_invalid' USING ERRCODE='22023';
          END IF;
          v_actor:=club.app_announcement_admin_actor(_digest,_owner);
          UPDATE club.app_announcements a
          SET removed_at=clock_timestamp(),version=a.version+1
          WHERE a.id=_id AND a.organization_id=_owner
            AND a.removed_at IS NULL AND a.version=_expected_version
            AND club.admin_app_notice_group_allowed(v_actor,_owner,a.group_id)
          RETURNING * INTO v_item;
          IF NOT FOUND THEN
            IF EXISTS(SELECT 1 FROM club.app_announcements a
              WHERE a.id=_id AND a.organization_id=_owner AND a.removed_at IS NULL
                AND NOT club.admin_app_notice_group_allowed(v_actor,_owner,a.group_id)) THEN
              RAISE EXCEPTION 'notice_territory_denied' USING ERRCODE='42501';
            END IF;
            IF EXISTS(SELECT 1 FROM club.app_announcements a
              WHERE a.id=_id AND a.organization_id=_owner AND a.removed_at IS NULL)
            THEN RAISE EXCEPTION 'app_announcement_stale' USING ERRCODE='40001';
            END IF;
            RAISE EXCEPTION 'app_announcement_denied' USING ERRCODE='42501';
          END IF;
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.app.announcement.remove','succeeded',_owner,'admin_only');
          RETURN jsonb_build_object('id',v_item.id,'removed',true,
            'version',v_item.version);
        END $app_remove$;

CREATE OR REPLACE FUNCTION club.read_admin_app_announcement_readers(
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
          IF NOT club.admin_app_notice_group_allowed(v_actor,_owner,v_group) THEN
            RAISE EXCEPTION 'notice_territory_denied' USING ERRCODE='42501';
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

CREATE OR REPLACE FUNCTION club.write_admin_app_announcement_media(
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
          IF NOT club.admin_app_notice_group_allowed(v_actor,_owner,v_draft.group_id) THEN
            RAISE EXCEPTION 'notice_territory_denied' USING ERRCODE='42501';
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

CREATE OR REPLACE FUNCTION club.remove_admin_app_announcement_media(
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
          IF NOT club.admin_app_notice_group_allowed(v_actor,_owner,v_draft.group_id) THEN
            RAISE EXCEPTION 'notice_territory_denied' USING ERRCODE='42501';
          END IF;
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
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
CREATE OR REPLACE FUNCTION club.create_admin_announcement_draft(
          _digest text,_owner text,_id text,_group text,_title text,_body text
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $draft_write$
        DECLARE
          v_actor uuid;
          v_session uuid;
          v_role text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _id IS NULL OR _id !~ '^ann_[0-9a-f]{32}$'
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _group !~ '^[a-zA-Z0-9_-]+$'
            OR _title IS NULL OR char_length(_title) NOT BETWEEN 1 AND 140
            OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
            OR _body IS NULL OR char_length(_body) NOT BETWEEN 1 AND 4000
            OR _body<>btrim(_body) THEN
            RAISE EXCEPTION 'draft_invalid' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
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
          IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
            RAISE EXCEPTION 'draft_roster_unready' USING ERRCODE='55000';
          END IF;
          IF (_owner='lefteria_fc' AND NOT EXISTS(
              SELECT 1 FROM club.teams t
              WHERE t.organization_id=_owner AND t.id=_group))
            OR (_owner='lefteria_fc_academy' AND NOT EXISTS(
              SELECT 1 FROM club.academy_groups g
              WHERE g.organization_id=_owner AND g.id=_group)) THEN
            RAISE EXCEPTION 'draft_group_invalid' USING ERRCODE='22023';
          END IF;
          -- Draft target remains a canonical group reference, not a saved
          -- list of children, parents, contacts, or recipients.
          INSERT INTO club.admin_announcement_drafts(
            id,organization_id,group_id,title,body,created_by)
          VALUES(_id,_owner,_group,_title,_body,v_actor);
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,
            device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
            'admin.announcement.draft.create','succeeded',_owner,'announcement_draft');
          RETURN jsonb_build_object('id',_id,'organization_id',_owner,
            'group_id',_group,'title',_title,'body',_body,'status','draft','version',1);
        END;
        $draft_write$;

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

CREATE OR REPLACE FUNCTION club.publish_admin_app_announcement(
          _digest text,_owner text,_id text,_expected_version bigint)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $app_publish$
        DECLARE v_actor uuid;
          v_draft club.admin_announcement_drafts%ROWTYPE;
          v_item club.app_announcements%ROWTYPE;
        BEGIN
          IF _id IS NULL OR _id !~ '^ann_[0-9a-f]{32}$'
            OR _expected_version IS NULL OR _expected_version<1 THEN
            RAISE EXCEPTION 'app_announcement_invalid' USING ERRCODE='22023';
          END IF;
          v_actor:=club.app_announcement_admin_actor(_digest,_owner);
          -- A real canonical target remains compulsory AT PUBLISH TIME.
          UPDATE club.admin_announcement_drafts d
          SET status='published',version=version+1
          WHERE d.id=_id AND d.organization_id=_owner
            AND d.status='draft' AND d.version=_expected_version
            AND EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
            AND ((
              _owner='lefteria_fc' AND EXISTS(
                SELECT 1 FROM club.teams t
                WHERE t.organization_id=_owner AND t.id=d.group_id))
              OR (_owner='lefteria_fc_academy' AND EXISTS(
                SELECT 1 FROM club.academy_groups g
                WHERE g.organization_id=_owner AND g.id=d.group_id)))
          RETURNING * INTO v_draft;
          IF NOT FOUND THEN
            IF EXISTS(SELECT 1 FROM club.admin_announcement_drafts d
              WHERE d.id=_id AND d.organization_id=_owner AND d.status='draft')
            THEN RAISE EXCEPTION 'app_announcement_stale' USING ERRCODE='40001';
            END IF;
            RAISE EXCEPTION 'app_announcement_denied' USING ERRCODE='42501';
          END IF;
          INSERT INTO club.app_announcements(
            id,organization_id,group_id,title,body,published_by)
          VALUES(v_draft.id,_owner,v_draft.group_id,v_draft.title,v_draft.body,v_actor)
          RETURNING * INTO v_item;
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.app.announcement.publish','succeeded',_owner,'canonical_group');
          RETURN jsonb_build_object(
            'id',v_item.id,'organization_id',_owner,'group_id',v_item.group_id,
            'title',v_item.title,'body',v_item.body,
            'published_at',v_item.published_at,'status','published',
            'version',v_item.version);
        END $app_publish$;

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

CREATE OR REPLACE FUNCTION club.remove_admin_app_announcement(
          _digest text,_owner text,_id text,_expected_version bigint)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $app_remove$
        DECLARE v_actor uuid;
          v_item club.app_announcements%ROWTYPE;
        BEGIN
          IF _id IS NULL OR _id !~ '^ann_[0-9a-f]{32}$'
            OR _expected_version IS NULL OR _expected_version<1 THEN
            RAISE EXCEPTION 'app_announcement_invalid' USING ERRCODE='22023';
          END IF;
          v_actor:=club.app_announcement_admin_actor(_digest,_owner);
          UPDATE club.app_announcements a
          SET removed_at=clock_timestamp(),version=a.version+1
          WHERE a.id=_id AND a.organization_id=_owner
            AND a.removed_at IS NULL AND a.version=_expected_version
          RETURNING * INTO v_item;
          IF NOT FOUND THEN
            IF EXISTS(SELECT 1 FROM club.app_announcements a
              WHERE a.id=_id AND a.organization_id=_owner AND a.removed_at IS NULL)
            THEN RAISE EXCEPTION 'app_announcement_stale' USING ERRCODE='40001';
            END IF;
            RAISE EXCEPTION 'app_announcement_denied' USING ERRCODE='42501';
          END IF;
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.app.announcement.remove','succeeded',_owner,'admin_only');
          RETURN jsonb_build_object('id',v_item.id,'removed',true,
            'version',v_item.version);
        END $app_remove$;

CREATE OR REPLACE FUNCTION club.read_admin_app_announcement_readers(
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

CREATE OR REPLACE FUNCTION club.write_admin_app_announcement_media(
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

CREATE OR REPLACE FUNCTION club.remove_admin_app_announcement_media(
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
        DROP FUNCTION club.admin_app_notice_group_allowed(uuid,text,text);
        """);
}
