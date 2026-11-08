using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Draft-only announcement preparation. No publish, recipients,
/// messages, chat, push, outbox or mobile visibility are created here.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108190000_AdminAnnouncementDrafts")]
public sealed class AdminAnnouncementDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE club.admin_announcement_drafts (
          id varchar(128) COLLATE "C" PRIMARY KEY,
          organization_id varchar(32) NOT NULL
            REFERENCES club.organizations(id) ON DELETE RESTRICT,
          group_id varchar(128) COLLATE "C" NOT NULL,
          title varchar(140) NOT NULL,
          body varchar(4000) NOT NULL,
          status varchar(16) NOT NULL DEFAULT 'draft',
          version bigint NOT NULL DEFAULT 1,
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          created_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
          CONSTRAINT ck_announcement_draft_status CHECK (status='draft'),
          CONSTRAINT ck_announcement_draft_version CHECK (version>=1),
          CONSTRAINT ck_announcement_draft_title CHECK (
            char_length(title) BETWEEN 1 AND 140 AND title=btrim(title)),
          CONSTRAINT ck_announcement_draft_body CHECK (
            char_length(body) BETWEEN 1 AND 4000 AND body=btrim(body))
        );
        CREATE INDEX ix_admin_announcement_drafts_owner_recent
          ON club.admin_announcement_drafts(organization_id,created_at DESC,id);

        CREATE FUNCTION club.create_admin_announcement_draft(
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
            OR _body<>btrim(_body) OR position(chr(0) in _body)>0 THEN
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

        CREATE FUNCTION club.read_admin_announcement_drafts(
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
        REVOKE ALL ON TABLE club.admin_announcement_drafts FROM PUBLIC;
        REVOKE ALL ON FUNCTION club.create_admin_announcement_draft(
          text,text,text,text,text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION club.read_admin_announcement_drafts(text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.read_admin_announcement_drafts(text,text);
        DROP FUNCTION club.create_admin_announcement_draft(text,text,text,text,text,text);
        DROP TABLE club.admin_announcement_drafts;
        """);
}
