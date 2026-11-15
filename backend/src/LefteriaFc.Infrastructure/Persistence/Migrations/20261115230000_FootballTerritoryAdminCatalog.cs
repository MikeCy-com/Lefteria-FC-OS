using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Session-locked, audited football territorial catalogue admin readers and
/// creation operations. Academy opponents and playing fields are per CITY;
/// training bases and group/fixture access are per LOCATION. First Team is
/// isolated by squad scope. Web-admin DB never receives table privileges.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115230000_FootballTerritoryAdminCatalog")]
public sealed class FootballTerritoryAdminCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- All private catalogue operations are session-bound and performed by
      -- the schema owner; the web-admin login never gets direct table SELECT.
      CREATE FUNCTION club.authorize_admin_football(_digest text,_owner text)
      RETURNS TABLE(actor_id uuid,account_role text)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $auth$
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[a-f0-9]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
        THEN RAISE EXCEPTION 'football_admin_invalid_session' USING ERRCODE='42501'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        RETURN QUERY
          SELECT a.id,a.role::text FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL
            AND a.role IN ('manager','super_admin');
        IF NOT FOUND THEN
          RAISE EXCEPTION 'football_admin_access_denied' USING ERRCODE='42501';
        END IF;
      END $auth$;

      -- Exact city or site grants. A location manager may read city-shared
      -- opponent/field choices but cannot mutate city-wide catalogues or
      -- enumerate sibling locations. A city manager sees every location in
      -- their city. Team ownership is entirely separate.
      CREATE FUNCTION club.admin_football_allowed(
        _actor uuid,_role text,_owner text,_scope uuid,
        _location uuid,_write boolean
      ) RETURNS boolean LANGUAGE plpgsql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $allowed$
      DECLARE v_city uuid;
      BEGIN
        IF _actor IS NULL OR _scope IS NULL OR _owner NOT IN
          ('lefteria_fc','lefteria_fc_academy') THEN RETURN false; END IF;
        IF _role='super_admin' THEN
          RETURN EXISTS(SELECT 1 FROM club.football_operational_scopes s
            WHERE s.id=_scope AND s.organization_id=_owner AND
              (_location IS NULL OR (_owner='lefteria_fc_academy'
                AND EXISTS(SELECT 1 FROM club.football_locations l
                  WHERE l.id=_location AND l.city_id=s.academy_city_id))));
        END IF;
        IF _role<>'manager' THEN RETURN false; END IF;
        IF _owner='lefteria_fc' THEN
          RETURN _location IS NULL AND EXISTS (
            SELECT 1 FROM club.football_team_admin_grants g
            JOIN club.football_operational_scopes s ON s.id=g.scope_id
            WHERE g.admin_account_id=_actor AND g.scope_id=_scope
              AND s.organization_id='lefteria_fc'
              AND (NOT _write OR g.role='team_manager'));
        END IF;
        SELECT academy_city_id INTO v_city
          FROM club.football_operational_scopes
          WHERE id=_scope AND organization_id='lefteria_fc_academy';
        IF v_city IS NULL THEN RETURN false; END IF;
        IF _location IS NOT NULL AND NOT EXISTS (
          SELECT 1 FROM club.football_locations
          WHERE id=_location AND city_id=v_city) THEN RETURN false; END IF;
        IF EXISTS (SELECT 1 FROM club.football_city_admin_grants
          WHERE admin_account_id=_actor AND city_id=v_city
            AND role='city_manager') THEN RETURN true; END IF;
        IF _location IS NOT NULL THEN
          RETURN EXISTS (SELECT 1 FROM club.football_location_admin_grants g
            WHERE g.admin_account_id=_actor AND g.location_id=_location
              AND (NOT _write OR g.role='location_manager'));
        END IF;
        RETURN NOT _write AND EXISTS (
          SELECT 1 FROM club.football_location_admin_grants g
          JOIN club.football_locations l ON l.id=g.location_id
          WHERE g.admin_account_id=_actor AND l.city_id=v_city);
      END $allowed$;

      -- Explicit allowlist: no players, guardians, invoices, payments,
      -- private coach evaluations or authentication data in the response.
      CREATE FUNCTION club.read_admin_football_directory(_digest text,_owner text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        RETURN jsonb_build_object(
          'organization_id',_owner,
          'cities',CASE WHEN _owner='lefteria_fc_academy' THEN (
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',c.id,'name',c.name,'slug',c.slug,'is_active',c.is_active
            ) ORDER BY c.name,c.id),'[]'::jsonb)
            FROM club.football_cities c
            JOIN club.football_operational_scopes s ON s.academy_city_id=c.id
              AND s.organization_id='lefteria_fc_academy'
            WHERE club.admin_football_allowed(v_actor,v_role,_owner,s.id,NULL,false)
          ) ELSE '[]'::jsonb END,
          'locations',CASE WHEN _owner='lefteria_fc_academy' THEN (
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',l.id,'city_id',l.city_id,'name',l.name,'slug',l.slug,
              'address',l.address,'google_maps_url',l.google_maps_url,
              'is_active',l.is_active
            ) ORDER BY l.name,l.id),'[]'::jsonb)
            FROM club.football_locations l
            JOIN club.football_operational_scopes s ON s.academy_city_id=l.city_id
            WHERE s.organization_id='lefteria_fc_academy'
              AND club.admin_football_allowed(v_actor,v_role,_owner,s.id,l.id,false)
          ) ELSE '[]'::jsonb END,
          'scopes',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',s.id,'organization_id',s.organization_id,
              'academy_city_id',s.academy_city_id,'team_id',s.team_id,
              'team_name',t.name
            ) ORDER BY coalesce(t.name,c.name),s.id),'[]'::jsonb)
            FROM club.football_operational_scopes s
            LEFT JOIN club.teams t ON t.id=s.team_id AND t.organization_id='lefteria_fc'
            LEFT JOIN club.football_cities c ON c.id=s.academy_city_id
            WHERE s.organization_id=_owner
              AND club.admin_football_allowed(v_actor,v_role,_owner,s.id,NULL,false)
          ),
          'groups',CASE WHEN _owner='lefteria_fc_academy' THEN (
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',g.group_id,'name',a.name,'city_id',g.city_id,
              'location_id',g.location_id,'scope_id',g.scope_id
            ) ORDER BY a.name,g.group_id),'[]'::jsonb)
            FROM club.academy_group_location_assignments g
            JOIN club.academy_groups a ON a.id=g.group_id
              AND a.organization_id='lefteria_fc_academy'
            WHERE club.admin_football_allowed(v_actor,v_role,_owner,g.scope_id,g.location_id,false)
          ) ELSE '[]'::jsonb END,
          'unassigned_groups',CASE WHEN _owner='lefteria_fc_academy'
            AND v_role='super_admin' THEN (
            SELECT coalesce(jsonb_agg(jsonb_build_object('id',g.id,'name',g.name)
              ORDER BY g.name,g.id),'[]'::jsonb)
            FROM club.academy_groups g
            WHERE g.organization_id='lefteria_fc_academy' AND NOT EXISTS (
              SELECT 1 FROM club.academy_group_location_assignments a
                WHERE a.group_id=g.id AND a.organization_id=g.organization_id)
          ) ELSE '[]'::jsonb END,
          'opponents',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',o.id,'scope_id',o.scope_id,'organization_id',o.organization_id,
              'name',o.name,'is_active',o.is_active
            ) ORDER BY o.name,o.id),'[]'::jsonb)
            FROM club.football_opponents o
            WHERE o.organization_id=_owner
              AND club.admin_football_allowed(v_actor,v_role,_owner,o.scope_id,NULL,false)
          ),
          'playing_fields',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',f.id,'scope_id',f.scope_id,'organization_id',f.organization_id,
              'name',f.name,'address',f.address,
              'google_maps_url',f.google_maps_url,'is_active',f.is_active
            ) ORDER BY f.name,f.id),'[]'::jsonb)
            FROM club.football_playing_fields f
            WHERE f.organization_id=_owner
              AND club.admin_football_allowed(v_actor,v_role,_owner,f.scope_id,NULL,false)
          ),
          'training_bases',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',b.id,'scope_id',b.scope_id,'organization_id',b.organization_id,
              'academy_city_id',b.academy_city_id,
              'academy_location_id',b.academy_location_id,
              'name',b.name,'playing_field_id',b.playing_field_id,
              'address',b.address,'google_maps_url',b.google_maps_url,
              'is_active',b.is_active
            ) ORDER BY b.name,b.id),'[]'::jsonb)
            FROM club.football_training_bases b
            WHERE b.organization_id=_owner
              AND club.admin_football_allowed(v_actor,v_role,_owner,b.scope_id,b.academy_location_id,false)
          ),
          'match_drafts',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',m.id,'scope_id',m.scope_id,'organization_id',m.organization_id,
              'academy_group_id',m.academy_group_id,
              'academy_location_id',m.academy_location_id,
              'club_team_id',m.club_team_id,
              'opponent_id',m.opponent_id,'playing_field_id',m.playing_field_id,
              'match_date',m.match_date,'kickoff_local',m.kickoff_local,
              'home_game',m.home_game,'competition',m.competition,
              'status',m.status
            ) ORDER BY m.match_date,m.kickoff_local,m.id),'[]'::jsonb)
            FROM club.football_match_drafts m
            WHERE m.organization_id=_owner
              AND club.admin_football_allowed(v_actor,v_role,_owner,m.scope_id,m.academy_location_id,false)
          )
        );
      END $read$;

      -- A single restricted creation endpoint, with action-specific mandatory
      -- fields and database-side authority verification before each mutation.
      CREATE FUNCTION club.create_admin_football_resource(
        _digest text,_owner text,_kind text,_data jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $write$
      DECLARE
        v_actor uuid; v_role text; v_id uuid:=gen_random_uuid();
        v_city uuid; v_location uuid; v_scope uuid; v_team text;
        v_group text; v_name text; v_slug text; v_address text;
        v_maps text; v_field uuid; v_opp uuid; v_date date;
        v_time time; v_home boolean; v_competition text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _kind NOT IN ('city','location','team_scope','group_assignment',
          'opponent','playing_field','training_base','match_draft')
          OR _data IS NULL OR jsonb_typeof(_data)<>'object'
          OR (SELECT count(*) FROM jsonb_object_keys(_data))>12
        THEN RAISE EXCEPTION 'football_invalid_operation' USING ERRCODE='22023'; END IF;

        v_name:=_data->>'name';
        v_slug:=_data->>'slug';
        v_address:=nullif(_data->>'address','');
        v_maps:=nullif(_data->>'google_maps_url','');
        IF _kind IN ('city','location','opponent','playing_field','training_base')
          AND (v_name IS NULL OR v_name<>btrim(v_name)
            OR length(v_name) NOT BETWEEN 1 AND 160
            OR v_name ~ '[[:cntrl:]]')
        THEN RAISE EXCEPTION 'football_invalid_name' USING ERRCODE='22023'; END IF;
        IF v_address IS NOT NULL AND
          (length(v_address)>300 OR v_address ~ '[[:cntrl:]]')
        THEN RAISE EXCEPTION 'football_invalid_address' USING ERRCODE='22023'; END IF;
        IF v_maps IS NOT NULL AND (
          length(v_maps)>2048 OR NOT (
            v_maps ~ '^https://(www[.])?google[.][a-z.]+/maps(/|[?]|$)'
            OR v_maps ~ '^https://maps[.]app[.]goo[.]gl/')
          OR v_maps ~ '[[:cntrl:][:space:]]')
        THEN RAISE EXCEPTION 'football_invalid_maps' USING ERRCODE='22023'; END IF;
        IF _kind IN ('city','location') AND
          (v_name IS NULL OR length(v_name)>120 OR
            v_slug IS NULL OR length(v_slug)>120 OR
            v_slug !~ '^[a-z0-9]+(-[a-z0-9]+)*$')
        THEN RAISE EXCEPTION 'football_invalid_slug' USING ERRCODE='22023'; END IF;

        IF _kind='group_assignment' AND _owner<>'lefteria_fc_academy' THEN
          RAISE EXCEPTION 'football_group_owner_denied' USING ERRCODE='42501';
        END IF;

        IF _kind='city' THEN
          IF _owner<>'lefteria_fc_academy' OR v_role<>'super_admin'
          THEN RAISE EXCEPTION 'football_city_denied' USING ERRCODE='42501'; END IF;
          INSERT INTO club.football_cities(id,name,slug)
            VALUES(v_id,v_name,v_slug);
          INSERT INTO club.football_operational_scopes(organization_id,academy_city_id)
            VALUES(_owner,v_id);
        ELSIF _kind='team_scope' THEN
          v_team:=_data->>'team_id';
          IF _owner<>'lefteria_fc' OR v_role<>'super_admin'
            OR v_team IS NULL OR v_team !~ '^[A-Za-z0-9_-]{1,128}$'
            OR NOT EXISTS (SELECT 1 FROM club.teams
              WHERE id=v_team AND organization_id='lefteria_fc')
          THEN RAISE EXCEPTION 'football_team_denied' USING ERRCODE='42501'; END IF;
          INSERT INTO club.football_operational_scopes(id,organization_id,team_id)
            VALUES(v_id,_owner,v_team);
        ELSE
          IF (_data->>'scope_id') IS NULL OR
            (_data->>'scope_id') !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
          THEN
            IF _kind='location' THEN
              IF (_data->>'city_id') IS NULL OR
                (_data->>'city_id') !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
              THEN RAISE EXCEPTION 'football_city_id_invalid' USING ERRCODE='22023'; END IF;
              v_city:=(_data->>'city_id')::uuid;
              SELECT id INTO v_scope FROM club.football_operational_scopes
                WHERE academy_city_id=v_city AND organization_id='lefteria_fc_academy';
            ELSE RAISE EXCEPTION 'football_scope_id_invalid' USING ERRCODE='22023'; END IF;
          ELSE
            v_scope:=(_data->>'scope_id')::uuid;
          END IF;
          IF NOT EXISTS(SELECT 1 FROM club.football_operational_scopes
            WHERE id=v_scope AND organization_id=_owner)
          THEN RAISE EXCEPTION 'football_scope_missing' USING ERRCODE='42501'; END IF;
          SELECT academy_city_id,team_id INTO v_city,v_team
            FROM club.football_operational_scopes WHERE id=v_scope;

          IF _kind='location' THEN
            IF _owner<>'lefteria_fc_academy' OR v_city IS NULL
              OR NOT EXISTS(SELECT 1 FROM club.football_cities
                WHERE id=v_city AND is_active)
              OR NOT club.admin_football_allowed(v_actor,v_role,_owner,v_scope,NULL,true)
            THEN RAISE EXCEPTION 'football_location_denied' USING ERRCODE='42501'; END IF;
            INSERT INTO club.football_locations(id,city_id,name,slug,address,google_maps_url)
              VALUES(v_id,v_city,v_name,v_slug,v_address,v_maps);
          ELSE
            IF _owner='lefteria_fc_academy' AND NOT EXISTS (
              SELECT 1 FROM club.football_cities WHERE id=v_city AND is_active)
            THEN RAISE EXCEPTION 'football_inactive_city' USING ERRCODE='42501'; END IF;
            IF _kind IN ('group_assignment','training_base','match_draft')
              AND _owner='lefteria_fc_academy' THEN
              IF (_data->>'location_id') IS NULL OR
                (_data->>'location_id') !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
              THEN RAISE EXCEPTION 'football_location_id_invalid' USING ERRCODE='22023'; END IF;
              v_location:=(_data->>'location_id')::uuid;
              IF NOT EXISTS(SELECT 1 FROM club.football_locations
                WHERE id=v_location AND city_id=v_city AND is_active)
              THEN RAISE EXCEPTION 'football_location_missing' USING ERRCODE='42501'; END IF;
            END IF;
            IF NOT club.admin_football_allowed(
              v_actor,v_role,_owner,v_scope,
              CASE WHEN _kind IN ('group_assignment','training_base','match_draft')
                THEN v_location ELSE NULL END,true)
            THEN RAISE EXCEPTION 'football_scope_denied' USING ERRCODE='42501'; END IF;

            IF _kind='group_assignment' THEN
              v_group:=_data->>'group_id';
              IF v_group IS NULL OR v_group !~ '^[A-Za-z0-9_-]{1,128}$'
                OR NOT EXISTS(SELECT 1 FROM club.academy_groups
                    WHERE id=v_group AND organization_id='lefteria_fc_academy')
                OR EXISTS(SELECT 1 FROM club.academy_group_location_assignments
                    WHERE group_id=v_group)
              THEN RAISE EXCEPTION 'football_group_assignment_denied'
                USING ERRCODE='42501'; END IF;
              INSERT INTO club.academy_group_location_assignments
                (organization_id,group_id,city_id,location_id,scope_id)
                VALUES('lefteria_fc_academy',v_group,v_city,v_location,v_scope);
            ELSIF _kind='opponent' THEN
              INSERT INTO club.football_opponents(id,organization_id,scope_id,name)
                VALUES(v_id,_owner,v_scope,v_name);
            ELSIF _kind='playing_field' THEN
              INSERT INTO club.football_playing_fields
                (id,organization_id,scope_id,name,address,google_maps_url)
                VALUES(v_id,_owner,v_scope,v_name,v_address,v_maps);
            ELSIF _kind='training_base' THEN
              IF (_data->>'playing_field_id') IS NOT NULL AND
                (_data->>'playing_field_id') !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
              THEN RAISE EXCEPTION 'football_field_invalid' USING ERRCODE='22023'; END IF;
              v_field:=nullif(_data->>'playing_field_id','')::uuid;
              INSERT INTO club.football_training_bases(
                id,organization_id,scope_id,academy_city_id,academy_location_id,
                name,playing_field_id,address,google_maps_url)
              VALUES(v_id,_owner,v_scope,v_city,v_location,
                v_name,v_field,v_address,v_maps);
            ELSIF _kind='match_draft' THEN
              v_group:=_data->>'group_id';
              IF (_data->>'opponent_id') !~* '^[0-9a-f-]{36}$' OR
                 (_data->>'playing_field_id') !~* '^[0-9a-f-]{36}$' OR
                 (_data->>'match_date') !~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' OR
                 (_data->>'kickoff_local') !~ '^[0-9]{2}:[0-9]{2}$'
              THEN RAISE EXCEPTION 'football_match_invalid' USING ERRCODE='22023'; END IF;
              v_opp:=(_data->>'opponent_id')::uuid;
              v_field:=(_data->>'playing_field_id')::uuid;
              v_date:=(_data->>'match_date')::date;
              v_time:=(_data->>'kickoff_local')::time;
              v_home:=coalesce((_data->>'home_game')::boolean,true);
              v_competition:=coalesce(_data->>'competition','');
              IF length(v_competition)>160 OR v_competition ~ '[[:cntrl:]]'
                OR v_date<current_date-interval '1 year'
              THEN RAISE EXCEPTION 'football_match_invalid' USING ERRCODE='22023'; END IF;
              IF NOT EXISTS(SELECT 1 FROM club.football_opponents
                WHERE id=v_opp AND scope_id=v_scope AND is_active)
                OR NOT EXISTS(SELECT 1 FROM club.football_playing_fields
                WHERE id=v_field AND scope_id=v_scope AND is_active)
              THEN RAISE EXCEPTION 'football_match_catalog_denied' USING ERRCODE='42501'; END IF;
              IF _owner='lefteria_fc_academy' AND (v_group IS NULL
                OR NOT EXISTS(SELECT 1 FROM club.academy_group_location_assignments
                  WHERE group_id=v_group AND scope_id=v_scope AND location_id=v_location))
              THEN RAISE EXCEPTION 'football_match_group_denied' USING ERRCODE='42501'; END IF;
              INSERT INTO club.football_match_drafts(
                id,organization_id,scope_id,club_team_id,
                academy_group_id,academy_location_id,
                opponent_id,playing_field_id,match_date,kickoff_local,
                home_game,competition)
              VALUES(v_id,_owner,v_scope,
                CASE WHEN _owner='lefteria_fc' THEN v_team ELSE NULL END,
                CASE WHEN _owner='lefteria_fc_academy' THEN v_group ELSE NULL END,
                v_location,v_opp,v_field,v_date,v_time,v_home,v_competition);
            END IF;
          END IF;
        END IF;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,action,outcome,
          organization_id,reason_code
        ) VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          'admin.football.catalog.create','succeeded',_owner,_kind);
        RETURN jsonb_build_object('id',CASE WHEN _kind='group_assignment' THEN
          to_jsonb(v_group) ELSE to_jsonb(v_id) END,'kind',_kind,
          'organization_id',_owner);
      END $write$;

      REVOKE ALL ON FUNCTION club.authorize_admin_football(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.admin_football_allowed(uuid,text,text,uuid,uuid,boolean)
        FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.read_admin_football_directory(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.create_admin_football_resource(text,text,text,jsonb)
        FROM PUBLIC;

    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.create_admin_football_resource(text,text,text,jsonb);
      DROP FUNCTION club.read_admin_football_directory(text,text);
      DROP FUNCTION club.admin_football_allowed(uuid,text,text,uuid,uuid,boolean);
      DROP FUNCTION club.authorize_admin_football(text,text);
    """);
}
