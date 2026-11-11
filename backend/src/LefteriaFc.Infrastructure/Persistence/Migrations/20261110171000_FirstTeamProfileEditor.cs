using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Original First Team profile fields; canonical team IDs and player/staff
/// relationships remain unchanged. No runtime grants in the migration.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110171000_FirstTeamProfileEditor")]
public sealed class FirstTeamProfileEditor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.first_team_profiles (
        organization_id varchar(32) NOT NULL DEFAULT 'lefteria_fc'
          CHECK(organization_id='lefteria_fc'),
        team_id varchar(128) COLLATE "C" NOT NULL,
        level varchar(100) NOT NULL DEFAULT 'Α'' Ομάδα',
        description varchar(2000) NOT NULL DEFAULT '',
        banner_url varchar(2048) NOT NULL DEFAULT '',
        revision bigint NOT NULL DEFAULT 1 CHECK(revision>=1),
        updated_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(organization_id,team_id),
        FOREIGN KEY(organization_id,team_id)
          REFERENCES club.teams(organization_id,id) ON DELETE RESTRICT,
        CHECK(level !~ '[[:cntrl:]]' AND description !~ '[[:cntrl:]]'
          AND banner_url !~ '[[:cntrl:]]')
      );
      ALTER TABLE club.first_team_profiles ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON TABLE club.first_team_profiles FROM PUBLIC;

      CREATE FUNCTION club.authorize_admin_first_team_profile(_digest text)
      RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER VOLATILE
      SET search_path=pg_catalog,pg_temp AS $auth$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[a-f0-9]{64}$' THEN
          RAISE EXCEPTION 'team_profile_session_invalid' USING ERRCODE='42501'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,'lefteria_fc');
        SELECT a.id,a.role INTO v_actor,v_role
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g
          ON g.admin_account_id=a.id AND g.organization_id='lefteria_fc'
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL
          AND s.expires_at>clock_timestamp() AND a.is_active
          AND NOT a.invite_pending AND NOT a.otp2fa_enabled
          AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin')
        THEN RAISE EXCEPTION 'team_profile_access_denied' USING ERRCODE='42501'; END IF;
        RETURN v_actor;
      END $auth$;

      CREATE FUNCTION club.read_admin_first_team_profile(
        _digest text,_team text
      ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
      SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_actor uuid; v_row club.first_team_profiles%ROWTYPE;
      BEGIN
        v_actor:=club.authorize_admin_first_team_profile(_digest);
        IF _team IS NULL OR _team !~ '^[A-Za-z0-9_-]{1,128}$'
          OR NOT EXISTS(SELECT 1 FROM club.teams
            WHERE organization_id='lefteria_fc' AND id=_team)
        THEN RAISE EXCEPTION 'team_profile_missing' USING ERRCODE='42501'; END IF;
        SELECT * INTO v_row FROM club.first_team_profiles
          WHERE organization_id='lefteria_fc' AND team_id=_team;
        RETURN jsonb_build_object(
          'organization_id','lefteria_fc','team_id',_team,
          'level',coalesce(v_row.level,'Α'' Ομάδα'),
          'description',coalesce(v_row.description,''),
          'banner_url',coalesce(v_row.banner_url,''),
          'revision',coalesce(v_row.revision,0));
      END $read$;

      CREATE FUNCTION club.write_admin_first_team_profile(
        _digest text,_team text,_expected bigint,_body jsonb
      ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
      SET search_path=pg_catalog,pg_temp AS $write$
      DECLARE v_actor uuid; v_session uuid;
        v_data record; v_old bigint; v_next bigint;
      BEGIN
        v_actor:=club.authorize_admin_first_team_profile(_digest);
        IF _team IS NULL OR _team !~ '^[A-Za-z0-9_-]{1,128}$'
          OR _expected IS NULL OR _expected NOT BETWEEN 0 AND 1000000000
          OR _body IS NULL OR jsonb_typeof(_body)<>'object'
          OR (SELECT count(*) FROM jsonb_object_keys(_body))<>3
          OR NOT (_body ?& ARRAY['level','description','banner_url'])
          OR jsonb_typeof(_body->'level')<>'string'
          OR jsonb_typeof(_body->'description')<>'string'
          OR jsonb_typeof(_body->'banner_url')<>'string'
        THEN RAISE EXCEPTION 'team_profile_invalid' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_data FROM jsonb_to_record(_body) AS x(
          level text, description text, banner_url text);
        IF length(v_data.level) NOT BETWEEN 1 AND 100
          OR v_data.level<>btrim(v_data.level)
          OR length(v_data.description)>2000 OR length(v_data.banner_url)>2048
          OR v_data.level ~ '[[:cntrl:]]' OR v_data.description ~ '[[:cntrl:]]'
          OR v_data.banner_url ~ '[[:cntrl:]]'
          OR (v_data.banner_url<>'' AND (
              v_data.banner_url !~ '^https://[A-Za-z0-9.-]+(/[A-Za-z0-9_./%?&=-]*)?$'
              OR v_data.banner_url ~* 'https://[^/]*(@|localhost|127[.]|0[.]0[.]0[.]0)'
            ))
        THEN RAISE EXCEPTION 'team_profile_fields_invalid' USING ERRCODE='22023'; END IF;

        PERFORM 1 FROM club.teams WHERE organization_id='lefteria_fc'
          AND id=_team FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'team_profile_missing' USING ERRCODE='42501'; END IF;
        SELECT revision INTO v_old FROM club.first_team_profiles
          WHERE organization_id='lefteria_fc' AND team_id=_team FOR UPDATE;
        IF coalesce(v_old,0)<>_expected
        THEN RAISE EXCEPTION 'team_profile_revision_conflict'
          USING ERRCODE='23505'; END IF;
        v_next:=_expected+1;
        INSERT INTO club.first_team_profiles(
          organization_id,team_id,level,description,banner_url,
          revision,updated_by,updated_at)
        VALUES('lefteria_fc',_team,v_data.level,v_data.description,
          v_data.banner_url,v_next,v_actor,clock_timestamp())
        ON CONFLICT(organization_id,team_id) DO UPDATE SET
          level=excluded.level,description=excluded.description,
          banner_url=excluded.banner_url,revision=excluded.revision,
          updated_by=excluded.updated_by,updated_at=excluded.updated_at;
        SELECT s.id INTO v_session FROM identity.admin_sessions s
          WHERE s.token_digest=_digest;
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,
          actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.roster.team.profile.update','succeeded','lefteria_fc','team_profile');
        RETURN club.read_admin_first_team_profile(_digest,_team);
      END $write$;
      REVOKE ALL ON FUNCTION club.authorize_admin_first_team_profile(text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.read_admin_first_team_profile(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.write_admin_first_team_profile(text,text,bigint,jsonb) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM club.first_team_profiles)
        THEN RAISE EXCEPTION 'team_profile_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION club.write_admin_first_team_profile(text,text,bigint,jsonb);
      DROP FUNCTION club.read_admin_first_team_profile(text,text);
      DROP FUNCTION club.authorize_admin_first_team_profile(text);
      DROP TABLE club.first_team_profiles;
    """);
}
