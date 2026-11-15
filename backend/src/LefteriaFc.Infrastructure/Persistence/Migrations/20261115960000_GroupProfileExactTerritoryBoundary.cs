using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Enforce exact current team and Academy city/location authority for
/// every existing group profile read/write and public visibility change.
/// Super Admin retains controlled unassigned-legacy reconciliation.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115960000_GroupProfileExactTerritoryBoundary")]
public sealed class GroupProfileExactTerritoryBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
      CREATE FUNCTION club.require_admin_group_profile_scope(
        _digest text,_owner text,_group text,_write boolean
      ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $group_scope$
      DECLARE
        v_actor uuid; v_role text;
        v_scope uuid; v_location uuid; v_city uuid;
      BEGIN
        IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _group IS NULL OR _group !~ '^[A-Za-z0-9_-]{1,128}$'
          OR _write IS NULL THEN
          RAISE EXCEPTION 'profile_group_scope_invalid'
            USING ERRCODE='42501';
        END IF;
        -- Recheck AND lock the active session, exact organization grant and role
        -- on EVERY operation, including visibility changes and server-side replays.
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);

        IF _owner='lefteria_fc' THEN
          PERFORM 1 FROM club.teams t
            WHERE t.organization_id='lefteria_fc' AND t.id=_group
            FOR SHARE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'first_team_profile_group_denied'
              USING ERRCODE='42501';
          END IF;
          SELECT s.id INTO v_scope
            FROM club.football_operational_scopes s
            WHERE s.organization_id='lefteria_fc' AND s.team_id=_group
            FOR SHARE;
          -- Old imported unscoped teams have a Super Admin reconciliation path.
          -- Managers never inherit it through their organization membership.
          IF v_scope IS NULL THEN
            IF v_role='super_admin' THEN RETURN v_actor; END IF;
            RAISE EXCEPTION 'first_team_profile_scope_missing'
              USING ERRCODE='42501';
          END IF;
          IF NOT club.admin_football_allowed(
              v_actor,v_role,_owner,v_scope,NULL,_write) THEN
            RAISE EXCEPTION 'first_team_profile_scope_denied'
              USING ERRCODE='42501';
          END IF;
          RETURN v_actor;
        END IF;

        PERFORM 1 FROM club.academy_groups g
          WHERE g.organization_id='lefteria_fc_academy' AND g.id=_group
          FOR SHARE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'academy_profile_group_denied'
            USING ERRCODE='42501';
        END IF;

        SELECT a.scope_id,a.location_id,a.city_id
          INTO v_scope,v_location,v_city
          FROM club.academy_group_location_assignments a
          JOIN club.football_operational_scopes s
            ON s.id=a.scope_id AND s.organization_id=a.organization_id
              AND s.academy_city_id=a.city_id
          JOIN club.football_cities c
            ON c.id=a.city_id AND c.is_active
          JOIN club.football_locations l
            ON l.id=a.location_id AND l.city_id=a.city_id AND l.is_active
          WHERE a.organization_id='lefteria_fc_academy' AND a.group_id=_group
          FOR SHARE OF a,s,c,l;
        IF v_scope IS NULL OR v_location IS NULL OR v_city IS NULL THEN
          -- A mapped but deactivated site is NOT treated as an unassigned group.
          -- Super Admin may inspect legacy UNASSIGNED groups during reconciliation.
          IF v_role='super_admin' AND NOT EXISTS(
            SELECT 1 FROM club.academy_group_location_assignments a
            WHERE a.organization_id='lefteria_fc_academy'
              AND a.group_id=_group
          ) THEN RETURN v_actor; END IF;
          RAISE EXCEPTION 'academy_profile_active_site_required'
            USING ERRCODE='42501';
        END IF;
        -- The exact active location MUST be supplied, not NULL (which could
        -- authorize an unrelated location manager via broader city read access).
        IF NOT club.admin_football_allowed(
            v_actor,v_role,_owner,v_scope,v_location,_write) THEN
          RAISE EXCEPTION 'academy_profile_location_denied'
            USING ERRCODE='42501';
        END IF;
        RETURN v_actor;
      END $group_scope$;
      REVOKE ALL ON FUNCTION club.require_admin_group_profile_scope(
        text,text,text,boolean) FROM PUBLIC;

      CREATE OR REPLACE FUNCTION club.read_admin_first_team_profile(
              _digest text,_team text
            ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
            SET search_path=pg_catalog,pg_temp AS $read$
            DECLARE v_actor uuid; v_row club.first_team_profiles%ROWTYPE;
            BEGIN
              v_actor:=club.authorize_admin_first_team_profile(_digest);
              -- A global organization grant is insufficient for this group.
              PERFORM club.require_admin_group_profile_scope(_digest,'lefteria_fc',_team,false);
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

      CREATE OR REPLACE FUNCTION club.write_admin_first_team_profile(
              _digest text,_team text,_expected bigint,_body jsonb
            ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
            SET search_path=pg_catalog,pg_temp AS $write$
            DECLARE v_actor uuid; v_session uuid;
              v_data record; v_old bigint; v_next bigint;
            BEGIN
              v_actor:=club.authorize_admin_first_team_profile(_digest);
              -- A global organization grant is insufficient for this group.
              PERFORM club.require_admin_group_profile_scope(_digest,'lefteria_fc',_team,true);
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

      CREATE OR REPLACE FUNCTION club.read_admin_academy_group_profile(
              _digest text,_group text
            ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
            SET search_path=pg_catalog,pg_temp AS $read$
            DECLARE v_actor uuid; v_row club.academy_group_profiles%ROWTYPE;
            BEGIN
              v_actor:=club.authorize_admin_academy_profile(_digest);
              -- A global organization grant is insufficient for this group.
              PERFORM club.require_admin_group_profile_scope(_digest,'lefteria_fc_academy',_group,false);
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

      CREATE OR REPLACE FUNCTION club.write_admin_academy_group_profile(
              _digest text,_group text,_expected bigint,_body jsonb
            ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
            SET search_path=pg_catalog,pg_temp AS $write$
            DECLARE v_actor uuid; v_data record;
              v_previous bigint; v_enrolled bigint; v_revision bigint;
            BEGIN
              v_actor:=club.authorize_admin_academy_profile(_digest);
              -- A global organization grant is insufficient for this group.
              PERFORM club.require_admin_group_profile_scope(_digest,'lefteria_fc_academy',_group,true);
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

      CREATE OR REPLACE FUNCTION club.read_admin_academy_group_publicity(
              _digest text,_group text
            ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
            SET search_path=pg_catalog,pg_temp AS $read_visibility$
            DECLARE v_actor uuid; v_public boolean;
            BEGIN
              v_actor:=club.authorize_admin_academy_profile(_digest);
              -- A global organization grant is insufficient for this group.
              PERFORM club.require_admin_group_profile_scope(_digest,'lefteria_fc_academy',_group,false);
              IF _group IS NULL OR _group !~ '^[A-Za-z0-9_-]{1,128}$'
                OR NOT EXISTS(SELECT 1 FROM club.academy_groups
                  WHERE organization_id='lefteria_fc_academy' AND id=_group)
              THEN RAISE EXCEPTION 'academy_visibility_group_not_found'
                USING ERRCODE='42501'; END IF;
              SELECT p.is_public INTO v_public
                FROM club.academy_group_profiles p
                WHERE p.organization_id='lefteria_fc_academy' AND p.group_id=_group;
              RETURN jsonb_build_object('is_public',coalesce(v_public,false));
            END $read_visibility$;

      CREATE OR REPLACE FUNCTION club.set_admin_academy_group_publicity(
              _digest text,_group text,_expected bigint,_public boolean
            ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
            SET search_path=pg_catalog,pg_temp AS $write_visibility$
            DECLARE v_actor uuid; v_new_revision bigint;
            BEGIN
              v_actor:=club.authorize_admin_academy_profile(_digest);
              -- A global organization grant is insufficient for this group.
              PERFORM club.require_admin_group_profile_scope(_digest,'lefteria_fc_academy',_group,true);
              IF _group IS NULL OR _group !~ '^[A-Za-z0-9_-]{1,128}$'
                OR _expected IS NULL OR _expected<1 OR _expected>1000000000
                OR _public IS NULL
              THEN RAISE EXCEPTION 'academy_visibility_invalid'
                USING ERRCODE='22023'; END IF;

              UPDATE club.academy_group_profiles p
                SET is_public=_public,
                  revision=p.revision+1,
                  updated_by=v_actor,
                  updated_at=clock_timestamp()
                WHERE p.organization_id='lefteria_fc_academy'
                  AND p.group_id=_group AND p.revision=_expected
                RETURNING p.revision INTO v_new_revision;
              IF NOT FOUND THEN
                IF EXISTS(SELECT 1 FROM club.academy_group_profiles
                  WHERE organization_id='lefteria_fc_academy' AND group_id=_group)
                THEN RAISE EXCEPTION 'academy_visibility_revision_conflict'
                  USING ERRCODE='23505'; END IF;
                RAISE EXCEPTION 'academy_visibility_group_not_found'
                  USING ERRCODE='42501';
              END IF;
              INSERT INTO audit.security_events(id,actor_realm,occurred_at,
                actor_identity_id,action,outcome,organization_id,reason_code)
              VALUES(gen_random_uuid(),'admin',clock_timestamp(),
                v_actor,'admin.academy.group.public_visibility.update',
                'succeeded','lefteria_fc_academy',
                CASE WHEN _public THEN 'published' ELSE 'unpublished' END);
              RETURN club.read_admin_academy_group_profile(_digest,_group)
                || jsonb_build_object('is_public',_public);
            END $write_visibility$;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION club.read_admin_first_team_profile(
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

      CREATE OR REPLACE FUNCTION club.write_admin_first_team_profile(
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

      CREATE OR REPLACE FUNCTION club.read_admin_academy_group_profile(
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

      CREATE OR REPLACE FUNCTION club.write_admin_academy_group_profile(
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

      CREATE OR REPLACE FUNCTION club.read_admin_academy_group_publicity(
              _digest text,_group text
            ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
            SET search_path=pg_catalog,pg_temp AS $read_visibility$
            DECLARE v_actor uuid; v_public boolean;
            BEGIN
              v_actor:=club.authorize_admin_academy_profile(_digest);
              IF _group IS NULL OR _group !~ '^[A-Za-z0-9_-]{1,128}$'
                OR NOT EXISTS(SELECT 1 FROM club.academy_groups
                  WHERE organization_id='lefteria_fc_academy' AND id=_group)
              THEN RAISE EXCEPTION 'academy_visibility_group_not_found'
                USING ERRCODE='42501'; END IF;
              SELECT p.is_public INTO v_public
                FROM club.academy_group_profiles p
                WHERE p.organization_id='lefteria_fc_academy' AND p.group_id=_group;
              RETURN jsonb_build_object('is_public',coalesce(v_public,false));
            END $read_visibility$;

      CREATE OR REPLACE FUNCTION club.set_admin_academy_group_publicity(
              _digest text,_group text,_expected bigint,_public boolean
            ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
            SET search_path=pg_catalog,pg_temp AS $write_visibility$
            DECLARE v_actor uuid; v_new_revision bigint;
            BEGIN
              v_actor:=club.authorize_admin_academy_profile(_digest);
              IF _group IS NULL OR _group !~ '^[A-Za-z0-9_-]{1,128}$'
                OR _expected IS NULL OR _expected<1 OR _expected>1000000000
                OR _public IS NULL
              THEN RAISE EXCEPTION 'academy_visibility_invalid'
                USING ERRCODE='22023'; END IF;

              UPDATE club.academy_group_profiles p
                SET is_public=_public,
                  revision=p.revision+1,
                  updated_by=v_actor,
                  updated_at=clock_timestamp()
                WHERE p.organization_id='lefteria_fc_academy'
                  AND p.group_id=_group AND p.revision=_expected
                RETURNING p.revision INTO v_new_revision;
              IF NOT FOUND THEN
                IF EXISTS(SELECT 1 FROM club.academy_group_profiles
                  WHERE organization_id='lefteria_fc_academy' AND group_id=_group)
                THEN RAISE EXCEPTION 'academy_visibility_revision_conflict'
                  USING ERRCODE='23505'; END IF;
                RAISE EXCEPTION 'academy_visibility_group_not_found'
                  USING ERRCODE='42501';
              END IF;
              INSERT INTO audit.security_events(id,actor_realm,occurred_at,
                actor_identity_id,action,outcome,organization_id,reason_code)
              VALUES(gen_random_uuid(),'admin',clock_timestamp(),
                v_actor,'admin.academy.group.public_visibility.update',
                'succeeded','lefteria_fc_academy',
                CASE WHEN _public THEN 'published' ELSE 'unpublished' END);
              RETURN club.read_admin_academy_group_profile(_digest,_group)
                || jsonb_build_object('is_public',_public);
            END $write_visibility$;

      DROP FUNCTION club.require_admin_group_profile_scope(text,text,text,boolean);
      """);
}
