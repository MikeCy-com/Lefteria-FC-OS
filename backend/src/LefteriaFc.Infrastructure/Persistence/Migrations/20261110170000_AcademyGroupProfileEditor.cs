using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Source-compatible Academy group metadata is separate from the canonical
/// roster, retaining imported IDs/player/coach links. All reads and writes
/// require a freshly validated manager and exact Academy organization grant.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110170000_AcademyGroupProfileEditor")]
public sealed class AcademyGroupProfileEditor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.academy_group_profiles (
        organization_id varchar(32) NOT NULL DEFAULT 'lefteria_fc_academy'
          CHECK(organization_id='lefteria_fc_academy'),
        group_id varchar(128) COLLATE "C" NOT NULL,
        age_range varchar(80) NOT NULL DEFAULT '',
        coach_name varchar(120) NOT NULL DEFAULT '',
        training_schedule varchar(500) NOT NULL DEFAULT '',
        description varchar(2000) NOT NULL DEFAULT '',
        max_players integer NOT NULL DEFAULT 25
          CHECK(max_players BETWEEN 1 AND 200),
        season varchar(40) NOT NULL DEFAULT '',
        banner_url varchar(2048) NOT NULL DEFAULT '',
        display_order integer NOT NULL DEFAULT 0
          CHECK(display_order BETWEEN 0 AND 10000),
        revision bigint NOT NULL DEFAULT 1 CHECK(revision>=1),
        updated_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY (organization_id,group_id),
        FOREIGN KEY (organization_id,group_id)
          REFERENCES club.academy_groups(organization_id,id)
          ON DELETE RESTRICT,
        CHECK(age_range !~ '[[:cntrl:]]' AND coach_name !~ '[[:cntrl:]]'
          AND season !~ '[[:cntrl:]]' AND banner_url !~ '[[:cntrl:]]')
      );
      ALTER TABLE club.academy_group_profiles ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON TABLE club.academy_group_profiles FROM PUBLIC;

      CREATE FUNCTION club.authorize_admin_academy_profile(_digest text)
      RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER VOLATILE
      SET search_path=pg_catalog,pg_temp AS $auth$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[a-f0-9]{64}$' THEN
          RAISE EXCEPTION 'academy_profile_session_invalid' USING ERRCODE='42501';
        END IF;
        PERFORM identity.lock_admin_club_authority(_digest,'lefteria_fc_academy');
        SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id='lefteria_fc_academy'
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'academy_profile_access_denied' USING ERRCODE='42501';
        END IF;
        RETURN v_actor;
      END $auth$;

      CREATE FUNCTION club.read_admin_academy_group_profile(
        _digest text,_group text
      ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
      SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_actor uuid; v_row club.academy_group_profiles%ROWTYPE;
      BEGIN
        v_actor:=club.authorize_admin_academy_profile(_digest);
        IF _group IS NULL OR _group !~ '^[A-Za-z0-9_-]{1,128}$' OR
          NOT EXISTS(SELECT 1 FROM club.academy_groups
            WHERE id=_group AND organization_id='lefteria_fc_academy')
        THEN RAISE EXCEPTION 'academy_profile_group_not_found'
          USING ERRCODE='42501'; END IF;
        SELECT * INTO v_row FROM club.academy_group_profiles
          WHERE organization_id='lefteria_fc_academy' AND group_id=_group;
        RETURN jsonb_build_object(
          'organization_id','lefteria_fc_academy','group_id',_group,
          'age_range',coalesce(v_row.age_range,''),
          'coach_name',coalesce(v_row.coach_name,''),
          'training_schedule',coalesce(v_row.training_schedule,''),
          'description',coalesce(v_row.description,''),
          'max_players',coalesce(v_row.max_players,25),
          'season',coalesce(v_row.season,''),
          'banner_url',coalesce(v_row.banner_url,''),
          'display_order',coalesce(v_row.display_order,0),
          'revision',coalesce(v_row.revision,0));
      END $read$;

      CREATE FUNCTION club.write_admin_academy_group_profile(
        _digest text,_group text,_expected bigint,_body jsonb
      ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
      SET search_path=pg_catalog,pg_temp AS $write$
      DECLARE v_actor uuid; v_data record;
        v_previous bigint; v_enrolled bigint; v_revision bigint;
      BEGIN
        v_actor:=club.authorize_admin_academy_profile(_digest);
        IF _group IS NULL OR _group !~ '^[A-Za-z0-9_-]{1,128}$'
          OR _expected IS NULL OR _expected<0 OR _expected>1000000000
          OR _body IS NULL OR jsonb_typeof(_body)<>'object'
          OR (SELECT count(*) FROM jsonb_object_keys(_body))<>8
          OR NOT (_body ?& ARRAY['age_range','coach_name','training_schedule',
            'description','max_players','season','banner_url','display_order'])
          OR jsonb_typeof(_body->'age_range')<>'string'
          OR jsonb_typeof(_body->'coach_name')<>'string'
          OR jsonb_typeof(_body->'training_schedule')<>'string'
          OR jsonb_typeof(_body->'description')<>'string'
          OR jsonb_typeof(_body->'max_players')<>'number'
          OR jsonb_typeof(_body->'season')<>'string'
          OR jsonb_typeof(_body->'banner_url')<>'string'
          OR jsonb_typeof(_body->'display_order')<>'number'
          OR (_body->>'max_players') !~ '^[0-9]{1,3}$'
          OR (_body->>'display_order') !~ '^[0-9]{1,5}$'
        THEN RAISE EXCEPTION 'academy_profile_invalid' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_data FROM jsonb_to_record(_body) AS x(
          age_range text,coach_name text,training_schedule text,
          description text,max_players int,season text,banner_url text,display_order int);
        IF length(v_data.age_range)>80 OR length(v_data.coach_name)>120
          OR length(v_data.training_schedule)>500 OR length(v_data.description)>2000
          OR length(v_data.season)>40 OR length(v_data.banner_url)>2048
          OR v_data.max_players NOT BETWEEN 1 AND 200
          OR v_data.display_order NOT BETWEEN 0 AND 10000
          OR v_data.age_range ~ '[[:cntrl:]]' OR v_data.coach_name ~ '[[:cntrl:]]'
          OR v_data.season ~ '[[:cntrl:]]' OR v_data.banner_url ~ '[[:cntrl:]]'
          OR v_data.training_schedule ~ '[[:cntrl:]]'
          OR v_data.description ~ '[[:cntrl:]]'
          OR (v_data.banner_url<>'' AND (
              v_data.banner_url !~ '^https://[A-Za-z0-9.-]+(/[A-Za-z0-9_./%?&=-]*)?$'
              OR v_data.banner_url ~* 'https://[^/]*(@|localhost|127[.]|0[.]0[.]0[.]0)'
            ))
        THEN RAISE EXCEPTION 'academy_profile_fields_invalid'
          USING ERRCODE='22023'; END IF;

        PERFORM 1 FROM club.academy_groups WHERE id=_group
          AND organization_id='lefteria_fc_academy' FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'academy_profile_group_not_found'
          USING ERRCODE='42501'; END IF;
        SELECT count(DISTINCT player_id) INTO v_enrolled
          FROM club.player_academy_groups
          WHERE organization_id='lefteria_fc_academy' AND academy_group_id=_group;
        IF v_enrolled>v_data.max_players THEN
          RAISE EXCEPTION 'academy_profile_capacity_below_roster'
            USING ERRCODE='23514'; END IF;
        SELECT revision INTO v_previous FROM club.academy_group_profiles
          WHERE organization_id='lefteria_fc_academy' AND group_id=_group FOR UPDATE;
        IF coalesce(v_previous,0)<>_expected THEN
          RAISE EXCEPTION 'academy_profile_revision_conflict'
            USING ERRCODE='23505'; END IF;
        v_revision:=_expected+1;
        INSERT INTO club.academy_group_profiles(
          organization_id,group_id,age_range,coach_name,training_schedule,
          description,max_players,season,banner_url,display_order,
          revision,updated_by,updated_at)
        VALUES('lefteria_fc_academy',_group,v_data.age_range,v_data.coach_name,
          v_data.training_schedule,v_data.description,v_data.max_players,
          v_data.season,v_data.banner_url,v_data.display_order,
          v_revision,v_actor,clock_timestamp())
        ON CONFLICT(organization_id,group_id) DO UPDATE SET
          age_range=excluded.age_range,coach_name=excluded.coach_name,
          training_schedule=excluded.training_schedule,
          description=excluded.description,max_players=excluded.max_players,
          season=excluded.season,banner_url=excluded.banner_url,
          display_order=excluded.display_order,revision=excluded.revision,
          updated_by=excluded.updated_by,updated_at=excluded.updated_at;
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,
          actor_identity_id,action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),
          v_actor,'admin.academy.group.profile.update','succeeded',
          'lefteria_fc_academy','profile');
        RETURN club.read_admin_academy_group_profile(_digest,_group);
      END $write$;

      REVOKE ALL ON FUNCTION club.authorize_admin_academy_profile(text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.read_admin_academy_group_profile(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.write_admin_academy_group_profile(text,text,bigint,jsonb) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_group_profiles) THEN
          RAISE EXCEPTION 'academy_profile_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION club.write_admin_academy_group_profile(text,text,bigint,jsonb);
      DROP FUNCTION club.read_admin_academy_group_profile(text,text);
      DROP FUNCTION club.authorize_admin_academy_profile(text);
      DROP TABLE club.academy_group_profiles;
    """);
}