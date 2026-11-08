using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Publish an approved draft to a roster-scoped app-only notice;
/// admin-only removal and no mobile draft exposure. Disposable rollback only.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108270000_AppAnnouncementPublishing")]
public sealed class AppAnnouncementPublishing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

        ALTER TABLE club.admin_announcement_drafts
          DROP CONSTRAINT ck_announcement_draft_status;
        ALTER TABLE club.admin_announcement_drafts
          ADD CONSTRAINT ck_announcement_draft_status
            CHECK (status IN ('draft','published'));

        CREATE TABLE club.app_announcements (
          id varchar(128) COLLATE "C" PRIMARY KEY
            REFERENCES club.admin_announcement_drafts(id) ON DELETE RESTRICT,
          organization_id varchar(32) NOT NULL
            REFERENCES club.organizations(id) ON DELETE RESTRICT,
          group_id varchar(128) COLLATE "C" NOT NULL,
          title varchar(140) NOT NULL,
          body varchar(4000) NOT NULL,
          version bigint NOT NULL DEFAULT 1 CHECK (version>=1),
          published_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
          published_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          removed_at timestamptz,
          CONSTRAINT ck_app_announcement_title CHECK (
            char_length(title) BETWEEN 1 AND 140 AND title=btrim(title)),
          CONSTRAINT ck_app_announcement_body CHECK (
            char_length(body) BETWEEN 1 AND 4000 AND body=btrim(body))
        );
        CREATE INDEX ix_app_announcement_group_recent ON club.app_announcements(
          organization_id,group_id,published_at DESC,id DESC)
          WHERE removed_at IS NULL;
        REVOKE ALL ON TABLE club.app_announcements FROM PUBLIC;

        CREATE FUNCTION club.app_announcement_admin_actor(
          _digest text,_owner text)
        RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $admin_announcement_actor$
        DECLARE v_actor uuid;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
            RAISE EXCEPTION 'app_announcement_invalid' USING ERRCODE='22023';
          END IF;
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
          SELECT a.id INTO v_actor FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g ON
            g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL
            AND a.role IN ('manager','super_admin');
          IF NOT FOUND THEN
            RAISE EXCEPTION 'app_announcement_denied' USING ERRCODE='42501';
          END IF;
          RETURN v_actor;
        END $admin_announcement_actor$;
        REVOKE ALL ON FUNCTION club.app_announcement_admin_actor(
          text,text) FROM PUBLIC;

        CREATE FUNCTION club.publish_admin_app_announcement(
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
        REVOKE ALL ON FUNCTION club.publish_admin_app_announcement(
          text,text,text,bigint) FROM PUBLIC;

        CREATE FUNCTION club.read_admin_app_announcements(
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
        REVOKE ALL ON FUNCTION club.read_admin_app_announcements(
          text,text) FROM PUBLIC;

        CREATE FUNCTION club.remove_admin_app_announcement(
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
        REVOKE ALL ON FUNCTION club.remove_admin_app_announcement(
          text,text,text,bigint) FROM PUBLIC;

        CREATE FUNCTION club.read_mobile_app_announcements(
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
            'published_at',item.published_at,'status','published')
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
        REVOKE ALL ON FUNCTION club.read_mobile_app_announcements(
          uuid,text,text,timestamptz,text,text,integer) FROM PUBLIC;

        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.read_mobile_app_announcements(
          uuid,text,text,timestamptz,text,text,integer);
        DROP FUNCTION club.remove_admin_app_announcement(text,text,text,bigint);
        DROP FUNCTION club.read_admin_app_announcements(text,text);
        DROP FUNCTION club.publish_admin_app_announcement(text,text,text,bigint);
        DROP FUNCTION club.app_announcement_admin_actor(text,text);
        DROP TABLE club.app_announcements;
        -- Disposable databases only: do not undo live publication history.
        UPDATE club.admin_announcement_drafts
          SET status='draft',version=1 WHERE status='published';
        ALTER TABLE club.admin_announcement_drafts
          DROP CONSTRAINT ck_announcement_draft_status;
        ALTER TABLE club.admin_announcement_drafts
          ADD CONSTRAINT ck_announcement_draft_status CHECK (status='draft');
        """);
}
