using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Delegated Academy cohorts are created only at a verified active site,
/// atomically alongside profile, idempotency request and audit. Flat legacy
/// creation remains Super Admin only. No roster, chat or payment writes.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115950000_ScopedFootballGroupCreation")]
public sealed class ScopedFootballGroupCreation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION club.create_admin_group(
                    _digest text,_owner text,_id text,_name text
                  ) RETURNS jsonb
                  LANGUAGE plpgsql VOLATILE SECURITY DEFINER
                  SET search_path=pg_catalog,pg_temp
                  AS $admin_group_create$
                  DECLARE
                    v_actor uuid;
                    v_session uuid;
                    v_role text;
                    v_count integer;
                    v_kind text;
                    v_action text;
                  BEGIN
                    IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                      OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                      OR _id IS NULL OR _id !~ '^grp_[0-9a-f]{32}$'
                      OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 120
                      OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]' THEN
                      RAISE EXCEPTION 'admin_group_invalid' USING ERRCODE='22023';
                    END IF;

                    -- The auth lock holds session, admin account and exact owner
                    -- grant against concurrent revocation throughout this write.
                    PERFORM identity.lock_admin_club_authority(_digest,_owner);
                    SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
                    FROM identity.admin_sessions s
                    JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                    JOIN identity.admin_organization_grants g
                      ON g.admin_account_id=a.id AND g.organization_id=_owner
                    WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                      AND s.expires_at>clock_timestamp()
                      AND a.is_active AND NOT a.invite_pending
                      AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
                    IF NOT FOUND OR v_role<>'super_admin' THEN
                      RAISE EXCEPTION 'admin_group_permission_denied' USING ERRCODE='42501';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
                      RAISE EXCEPTION 'admin_group_roster_not_ready' USING ERRCODE='55000';
                    END IF;

                    -- Scoped advisory lock ensures the 150-group selector budget
                    -- also holds under concurrent administrator creates.
                    PERFORM pg_advisory_xact_lock(
                      hashtextextended('lefteria:roster:groups:'||_owner,0)
                    );
                    IF _owner='lefteria_fc' THEN
                      SELECT count(*) INTO v_count FROM club.teams WHERE organization_id=_owner;
                      IF v_count>=150 THEN
                        RAISE EXCEPTION 'admin_group_quota' USING ERRCODE='54000';
                      END IF;
                      INSERT INTO club.teams(id,organization_id,name)
                        VALUES(_id,_owner,_name) ON CONFLICT(id) DO NOTHING;
                      GET DIAGNOSTICS v_count=ROW_COUNT;
                      v_kind := 'team';
                      v_action := 'admin.roster.team.create';
                    ELSE
                      SELECT count(*) INTO v_count FROM club.academy_groups WHERE organization_id=_owner;
                      IF v_count>=150 THEN
                        RAISE EXCEPTION 'admin_group_quota' USING ERRCODE='54000';
                      END IF;
                      INSERT INTO club.academy_groups(id,organization_id,name)
                        VALUES(_id,_owner,_name) ON CONFLICT(id) DO NOTHING;
                      GET DIAGNOSTICS v_count=ROW_COUNT;
                      v_kind := 'academy_group';
                      v_action := 'admin.roster.academy_group.create';
                    END IF;
                    IF v_count<>1 THEN
                      RAISE EXCEPTION 'admin_group_id_conflict' USING ERRCODE='23505';
                    END IF;

                    -- If the audit insert fails, the group creation is reverted in
                    -- the same PostgreSQL transaction. There is no unlogged group.
                    INSERT INTO audit.security_events(
                      id,actor_realm,occurred_at,actor_identity_id,
                      device_session_id,action,outcome,organization_id,reason_code
                    ) VALUES (
                      gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                      v_session,v_action,'succeeded',_owner,'roster_groups'
                    );
                    RETURN jsonb_build_object(
                      'id',_id,'name',_name,'kind',v_kind,'active_players',0
                    );
                  END
                  $admin_group_create$;

      CREATE OR REPLACE FUNCTION club.create_admin_group_with_profile(
              _digest text,_owner text,_id text,_name text,
              _request uuid,_profile jsonb
            ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
            SET search_path=pg_catalog,pg_temp AS $create$
            DECLARE
              v_actor uuid; v_hash text; v_existing record;
              v_new integer; v_saved jsonb; v_profile jsonb;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _id IS NULL OR _id !~ '^grp_[0-9a-f]{32}$'
                OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 120
                OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
                OR _request IS NULL OR
                  _request='00000000-0000-0000-0000-000000000000'::uuid
                OR _profile IS NULL OR jsonb_typeof(_profile)<>'object'
              THEN RAISE EXCEPTION 'profiled_group_create_invalid'
                USING ERRCODE='22023'; END IF;

              -- Use the existing manager+organization grant lock before reading or
              -- locking a client-request row. Revocation is checked on every replay.
              IF _owner='lefteria_fc' THEN
                v_actor:=club.authorize_admin_first_team_profile(_digest);
              ELSE
                v_actor:=club.authorize_admin_academy_profile(_digest);
              END IF;

              -- A legacy Academy create request lacks an exact city and location.
              -- Require the new site-specific transaction instead of making an
              -- unassigned child group via the organization-level grant.
              IF _owner='lefteria_fc_academy' THEN
                RAISE EXCEPTION 'academy_group_location_required' USING ERRCODE='42501';
              END IF;

              v_hash:=encode(sha256(convert_to(
                jsonb_build_object('name',_name,'profile',_profile)::text,'UTF8')),'hex');
              INSERT INTO club.admin_group_create_requests(
                organization_id,actor_id,request_id,group_id,input_sha256
              ) VALUES(_owner,v_actor,_request,_id,v_hash)
              ON CONFLICT(organization_id,actor_id,request_id) DO NOTHING;
              GET DIAGNOSTICS v_new=ROW_COUNT;
              IF v_new=0 THEN
                SELECT * INTO v_existing FROM club.admin_group_create_requests
                  WHERE organization_id=_owner AND actor_id=v_actor
                    AND request_id=_request FOR UPDATE;
                IF NOT FOUND OR v_existing.input_sha256<>v_hash THEN
                  RAISE EXCEPTION 'profiled_group_replay_mismatch'
                    USING ERRCODE='23505'; END IF;
                -- Cross-check the authoritative original group and stored profile,
                -- never trust a replayed group ID supplied by the client.
                IF _owner='lefteria_fc' THEN
                  SELECT jsonb_build_object('id',t.id,'name',t.name,
                    'kind','team','active_players',0)
                    INTO v_saved FROM club.teams t
                    WHERE t.organization_id=_owner AND t.id=v_existing.group_id;
                  v_profile:=club.read_admin_first_team_profile(
                    _digest,v_existing.group_id);
                ELSE
                  SELECT jsonb_build_object('id',g.id,'name',g.name,
                    'kind','academy_group','active_players',0)
                    INTO v_saved FROM club.academy_groups g
                    WHERE g.organization_id=_owner AND g.id=v_existing.group_id;
                  v_profile:=club.read_admin_academy_group_profile(
                    _digest,v_existing.group_id);
                END IF;
                IF v_saved IS NULL OR v_saved->>'name'<>_name OR
                  (v_profile->>'revision')::bigint<1 THEN
                  RAISE EXCEPTION 'profiled_group_replay_inconsistent'
                    USING ERRCODE='23514'; END IF;
                RETURN v_saved || jsonb_build_object(
                  'profile',v_profile,'replay',true);
              END IF;

              -- All operations are one PostgreSQL transaction. If any profile
              -- validation, capacity, audit or canonical group write fails, no
              -- group, profile, or idempotency request row is committed.
              v_saved:=club.create_admin_group(_digest,_owner,_id,_name);
              IF _owner='lefteria_fc' THEN
                v_profile:=club.write_admin_first_team_profile(
                  _digest,_id,0,_profile);
                -- First Team group has its own canonical catalog scope immediately.
                -- No cross-organization opponents or fields are inherited.
                INSERT INTO club.football_operational_scopes(organization_id,team_id)
                VALUES('lefteria_fc',_id);
              ELSE
                v_profile:=club.write_admin_academy_group_profile(
                  _digest,_id,0,_profile);
              END IF;
              RETURN v_saved || jsonb_build_object(
                'profile',v_profile,'replay',false);
            END $create$;

      CREATE FUNCTION club.create_admin_academy_location_group_with_profile(
        _digest text,_id text,_name text,_request uuid,_profile jsonb,_location uuid
      ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
      SET search_path=pg_catalog,pg_temp AS $create_site$
      DECLARE
        v_actor uuid; v_role text; v_scope uuid; v_city uuid;
        v_hash text; v_existing record; v_new integer;
        v_profile jsonb; v_session uuid;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _id IS NULL OR _id !~ '^grp_[0-9a-f]{32}$'
          OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 120
          OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
          OR _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _profile IS NULL OR jsonb_typeof(_profile)<>'object'
          OR _location IS NULL THEN
          RAISE EXCEPTION 'academy_group_location_invalid' USING ERRCODE='22023';
        END IF;

        -- Locks the live account, session and exact Academy organization grant.
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,'lefteria_fc_academy');
        -- All three canonical rows are verified and locked against deactivation.
        SELECT s.id,c.id INTO v_scope,v_city
          FROM club.football_locations l
          JOIN club.football_cities c ON c.id=l.city_id AND c.is_active
          JOIN club.football_operational_scopes s
            ON s.academy_city_id=c.id AND s.organization_id='lefteria_fc_academy'
          WHERE l.id=_location AND l.is_active
          FOR SHARE OF l,c,s;
        IF v_scope IS NULL OR v_city IS NULL OR NOT
            club.admin_football_allowed(v_actor,v_role,'lefteria_fc_academy',
              v_scope,_location,true) THEN
          RAISE EXCEPTION 'academy_group_location_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
          RAISE EXCEPTION 'academy_group_roster_unready' USING ERRCODE='55000';
        END IF;

        -- Tenant-scoped advisory lock serializes quota and duplicate-name checks.
        PERFORM pg_advisory_xact_lock(
          hashtextextended('lefteria:roster:groups:lefteria_fc_academy',0));
        v_hash:=encode(sha256(convert_to(
          jsonb_build_object('name',_name,'profile',_profile,
            'location_id',_location)::text,'UTF8')),'hex');
        INSERT INTO club.admin_group_create_requests(
          organization_id,actor_id,request_id,group_id,input_sha256)
        VALUES('lefteria_fc_academy',v_actor,_request,_id,v_hash)
        ON CONFLICT(organization_id,actor_id,request_id) DO NOTHING;
        GET DIAGNOSTICS v_new=ROW_COUNT;
        IF v_new=0 THEN
          SELECT * INTO v_existing FROM club.admin_group_create_requests
            WHERE organization_id='lefteria_fc_academy'
              AND actor_id=v_actor AND request_id=_request FOR UPDATE;
          IF NOT FOUND OR v_existing.input_sha256<>v_hash THEN
            RAISE EXCEPTION 'academy_group_request_mismatch' USING ERRCODE='23505';
          END IF;
          -- Never return an old group following a site reassignment or revocation.
          IF NOT EXISTS(SELECT 1 FROM club.academy_group_location_assignments a
            WHERE a.organization_id='lefteria_fc_academy'
              AND a.group_id=v_existing.group_id AND a.location_id=_location
              AND a.city_id=v_city AND a.scope_id=v_scope) THEN
            RAISE EXCEPTION 'academy_group_replay_denied' USING ERRCODE='42501';
          END IF;
          v_profile:=club.read_admin_academy_group_profile(_digest,v_existing.group_id);
          IF (v_profile->>'revision')::bigint<1 OR NOT EXISTS(
            SELECT 1 FROM club.academy_groups g
            WHERE g.id=v_existing.group_id AND g.organization_id='lefteria_fc_academy'
              AND g.name=_name) THEN
            RAISE EXCEPTION 'academy_group_replay_invalid' USING ERRCODE='23514';
          END IF;
          RETURN jsonb_build_object(
            'id',v_existing.group_id,'name',_name,'kind','academy_group',
            'active_players',0,'profile',v_profile,'city_id',v_city,
            'location_id',_location,'replay',true);
        END IF;

        IF (SELECT count(*) FROM club.academy_groups
            WHERE organization_id='lefteria_fc_academy')>=150 THEN
          RAISE EXCEPTION 'academy_group_quota' USING ERRCODE='54000';
        END IF;
        IF EXISTS(
          SELECT 1 FROM club.academy_group_location_assignments a
          JOIN club.academy_groups g ON g.id=a.group_id
            AND g.organization_id=a.organization_id
          WHERE a.organization_id='lefteria_fc_academy'
            AND a.location_id=_location AND lower(g.name)=lower(_name)) THEN
          RAISE EXCEPTION 'academy_group_location_name_conflict'
            USING ERRCODE='23505';
        END IF;

        INSERT INTO club.academy_groups(id,organization_id,name)
          VALUES(_id,'lefteria_fc_academy',_name);
        INSERT INTO club.academy_group_location_assignments(
          organization_id,group_id,city_id,location_id,scope_id)
          VALUES('lefteria_fc_academy',_id,v_city,_location,v_scope);
        -- Existing validated profile writer participates in this SAME transaction.
        -- Any invalid capacity, banner URL or failed audit rolls everything back.
        v_profile:=club.write_admin_academy_group_profile(_digest,_id,0,_profile);
        SELECT s.id INTO v_session FROM identity.admin_sessions s
          WHERE s.token_digest=_digest;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.roster.academy_group.create','succeeded','lefteria_fc_academy',
          'verified_location');
        RETURN jsonb_build_object(
          'id',_id,'name',_name,'kind','academy_group','active_players',0,
          'profile',v_profile,'city_id',v_city,'location_id',_location,'replay',false);
      END $create_site$;
      REVOKE ALL ON FUNCTION club.create_admin_academy_location_group_with_profile(
        text,text,text,uuid,jsonb,uuid) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
      DROP FUNCTION club.create_admin_academy_location_group_with_profile(text,text,text,uuid,jsonb,uuid);

      CREATE OR REPLACE FUNCTION club.create_admin_group(
                    _digest text,_owner text,_id text,_name text
                  ) RETURNS jsonb
                  LANGUAGE plpgsql VOLATILE SECURITY DEFINER
                  SET search_path=pg_catalog,pg_temp
                  AS $admin_group_create$
                  DECLARE
                    v_actor uuid;
                    v_session uuid;
                    v_role text;
                    v_count integer;
                    v_kind text;
                    v_action text;
                  BEGIN
                    IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                      OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                      OR _id IS NULL OR _id !~ '^grp_[0-9a-f]{32}$'
                      OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 120
                      OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]' THEN
                      RAISE EXCEPTION 'admin_group_invalid' USING ERRCODE='22023';
                    END IF;

                    -- The auth lock holds session, admin account and exact owner
                    -- grant against concurrent revocation throughout this write.
                    PERFORM identity.lock_admin_club_authority(_digest,_owner);
                    SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
                    FROM identity.admin_sessions s
                    JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                    JOIN identity.admin_organization_grants g
                      ON g.admin_account_id=a.id AND g.organization_id=_owner
                    WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                      AND s.expires_at>clock_timestamp()
                      AND a.is_active AND NOT a.invite_pending
                      AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
                    IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
                      RAISE EXCEPTION 'admin_group_permission_denied' USING ERRCODE='42501';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
                      RAISE EXCEPTION 'admin_group_roster_not_ready' USING ERRCODE='55000';
                    END IF;

                    -- Scoped advisory lock ensures the 150-group selector budget
                    -- also holds under concurrent administrator creates.
                    PERFORM pg_advisory_xact_lock(
                      hashtextextended('lefteria:roster:groups:'||_owner,0)
                    );
                    IF _owner='lefteria_fc' THEN
                      SELECT count(*) INTO v_count FROM club.teams WHERE organization_id=_owner;
                      IF v_count>=150 THEN
                        RAISE EXCEPTION 'admin_group_quota' USING ERRCODE='54000';
                      END IF;
                      INSERT INTO club.teams(id,organization_id,name)
                        VALUES(_id,_owner,_name) ON CONFLICT(id) DO NOTHING;
                      GET DIAGNOSTICS v_count=ROW_COUNT;
                      v_kind := 'team';
                      v_action := 'admin.roster.team.create';
                    ELSE
                      SELECT count(*) INTO v_count FROM club.academy_groups WHERE organization_id=_owner;
                      IF v_count>=150 THEN
                        RAISE EXCEPTION 'admin_group_quota' USING ERRCODE='54000';
                      END IF;
                      INSERT INTO club.academy_groups(id,organization_id,name)
                        VALUES(_id,_owner,_name) ON CONFLICT(id) DO NOTHING;
                      GET DIAGNOSTICS v_count=ROW_COUNT;
                      v_kind := 'academy_group';
                      v_action := 'admin.roster.academy_group.create';
                    END IF;
                    IF v_count<>1 THEN
                      RAISE EXCEPTION 'admin_group_id_conflict' USING ERRCODE='23505';
                    END IF;

                    -- If the audit insert fails, the group creation is reverted in
                    -- the same PostgreSQL transaction. There is no unlogged group.
                    INSERT INTO audit.security_events(
                      id,actor_realm,occurred_at,actor_identity_id,
                      device_session_id,action,outcome,organization_id,reason_code
                    ) VALUES (
                      gen_random_uuid(),'admin',clock_timestamp(),v_actor,
                      v_session,v_action,'succeeded',_owner,'roster_groups'
                    );
                    RETURN jsonb_build_object(
                      'id',_id,'name',_name,'kind',v_kind,'active_players',0
                    );
                  END
                  $admin_group_create$;

      CREATE OR REPLACE FUNCTION club.create_admin_group_with_profile(
              _digest text,_owner text,_id text,_name text,
              _request uuid,_profile jsonb
            ) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER VOLATILE
            SET search_path=pg_catalog,pg_temp AS $create$
            DECLARE
              v_actor uuid; v_hash text; v_existing record;
              v_new integer; v_saved jsonb; v_profile jsonb;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _id IS NULL OR _id !~ '^grp_[0-9a-f]{32}$'
                OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 120
                OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
                OR _request IS NULL OR
                  _request='00000000-0000-0000-0000-000000000000'::uuid
                OR _profile IS NULL OR jsonb_typeof(_profile)<>'object'
              THEN RAISE EXCEPTION 'profiled_group_create_invalid'
                USING ERRCODE='22023'; END IF;

              -- Use the existing manager+organization grant lock before reading or
              -- locking a client-request row. Revocation is checked on every replay.
              IF _owner='lefteria_fc' THEN
                v_actor:=club.authorize_admin_first_team_profile(_digest);
              ELSE
                v_actor:=club.authorize_admin_academy_profile(_digest);
              END IF;

              v_hash:=encode(sha256(convert_to(
                jsonb_build_object('name',_name,'profile',_profile)::text,'UTF8')),'hex');
              INSERT INTO club.admin_group_create_requests(
                organization_id,actor_id,request_id,group_id,input_sha256
              ) VALUES(_owner,v_actor,_request,_id,v_hash)
              ON CONFLICT(organization_id,actor_id,request_id) DO NOTHING;
              GET DIAGNOSTICS v_new=ROW_COUNT;
              IF v_new=0 THEN
                SELECT * INTO v_existing FROM club.admin_group_create_requests
                  WHERE organization_id=_owner AND actor_id=v_actor
                    AND request_id=_request FOR UPDATE;
                IF NOT FOUND OR v_existing.input_sha256<>v_hash THEN
                  RAISE EXCEPTION 'profiled_group_replay_mismatch'
                    USING ERRCODE='23505'; END IF;
                -- Cross-check the authoritative original group and stored profile,
                -- never trust a replayed group ID supplied by the client.
                IF _owner='lefteria_fc' THEN
                  SELECT jsonb_build_object('id',t.id,'name',t.name,
                    'kind','team','active_players',0)
                    INTO v_saved FROM club.teams t
                    WHERE t.organization_id=_owner AND t.id=v_existing.group_id;
                  v_profile:=club.read_admin_first_team_profile(
                    _digest,v_existing.group_id);
                ELSE
                  SELECT jsonb_build_object('id',g.id,'name',g.name,
                    'kind','academy_group','active_players',0)
                    INTO v_saved FROM club.academy_groups g
                    WHERE g.organization_id=_owner AND g.id=v_existing.group_id;
                  v_profile:=club.read_admin_academy_group_profile(
                    _digest,v_existing.group_id);
                END IF;
                IF v_saved IS NULL OR v_saved->>'name'<>_name OR
                  (v_profile->>'revision')::bigint<1 THEN
                  RAISE EXCEPTION 'profiled_group_replay_inconsistent'
                    USING ERRCODE='23514'; END IF;
                RETURN v_saved || jsonb_build_object(
                  'profile',v_profile,'replay',true);
              END IF;

              -- All operations are one PostgreSQL transaction. If any profile
              -- validation, capacity, audit or canonical group write fails, no
              -- group, profile, or idempotency request row is committed.
              v_saved:=club.create_admin_group(_digest,_owner,_id,_name);
              IF _owner='lefteria_fc' THEN
                v_profile:=club.write_admin_first_team_profile(
                  _digest,_id,0,_profile);
              ELSE
                v_profile:=club.write_admin_academy_group_profile(
                  _digest,_id,0,_profile);
              END IF;
              RETURN v_saved || jsonb_build_object(
                'profile',v_profile,'replay',false);
            END $create$;
      """);
}
