using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Super Admin-only native coach record, exact team/site assignment
/// and optional existing Coach account link; no new identity or application grant.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261121153000_AdminNativeCoachRegistration")]
public sealed class AdminNativeCoachRegistration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Native entries are not legacy imported singular/plural relationships.
      -- Preserve import provenance, rather than mislabeling new records.
      ALTER TABLE club.staff_teams
        ADD COLUMN from_admin boolean NOT NULL DEFAULT false;
      ALTER TABLE club.staff_teams DROP CONSTRAINT ck_staff_team_origin;
      ALTER TABLE club.staff_teams ADD CONSTRAINT ck_staff_team_origin
        CHECK (from_plural OR from_singular OR from_admin);
      ALTER TABLE club.staff_academy_groups
        ADD COLUMN from_admin boolean NOT NULL DEFAULT false;
      ALTER TABLE club.staff_academy_groups DROP CONSTRAINT ck_staff_group_origin;
      ALTER TABLE club.staff_academy_groups ADD CONSTRAINT ck_staff_group_origin
        CHECK (from_plural OR from_singular OR from_group_coach OR from_admin);

      CREATE TABLE club.admin_native_coach_requests (
        request_id uuid PRIMARY KEY,
        staff_id varchar(128) COLLATE "C" NOT NULL UNIQUE
          REFERENCES club.staff(id) ON DELETE RESTRICT,
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        organization_id varchar(32) NOT NULL
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        group_id varchar(128) COLLATE "C" NOT NULL,
        account_id uuid REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        name varchar(200) NOT NULL,
        job_role varchar(64) NOT NULL,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );
      REVOKE ALL ON TABLE club.admin_native_coach_requests FROM PUBLIC;

      CREATE FUNCTION club.create_admin_native_coach(
        _digest text,_owner text,_request uuid,_name text,
        _job_role text,_group text,_account uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $native_coach$
      DECLARE
        v_actor uuid;v_session uuid;v_role text;v_id text;
        v_scope uuid;v_location uuid;v_subject_role text;
        v_old club.admin_native_coach_requests%ROWTYPE;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _name IS NULL OR _name<>btrim(_name)
          OR length(_name) NOT BETWEEN 2 AND 200
          OR _name ~ '[[:cntrl:]]'
          OR _job_role IS NULL
          OR _job_role NOT IN ('Head Coach','Assistant Coach','Goalkeeper Coach',
            'Fitness Coach','Youth Coach')
          OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
          OR _group !~ '^[A-Za-z0-9_-]+$'
        THEN RAISE EXCEPTION 'native_coach_invalid' USING ERRCODE='22023'; END IF;

        -- Locks actor, session and organization grant before new identity
        -- linking. On revocation/deactivation no stale session may write.
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
        IF v_actor IS NULL OR v_role IS DISTINCT FROM 'super_admin'
        THEN RAISE EXCEPTION 'native_coach_super_admin_required' USING ERRCODE='42501'; END IF;
        IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
          OR NOT EXISTS(SELECT 1 FROM migration.staff_relationship_import_state
                        WHERE id='initial')
        THEN RAISE EXCEPTION 'native_coach_registry_not_ready' USING ERRCODE='55000'; END IF;

        -- Serialize identical requests without ever re-creating an account
        -- or duplicating the same coach on a browser/network retry.
        PERFORM pg_advisory_xact_lock(hashtextextended(_request::text,0));
        SELECT * INTO v_old FROM club.admin_native_coach_requests
          WHERE request_id=_request FOR SHARE;
        IF FOUND THEN
          IF v_old.actor_id<>v_actor OR v_old.organization_id<>_owner
            OR v_old.group_id<>_group OR v_old.name<>_name
            OR v_old.job_role<>_job_role
            OR v_old.account_id IS DISTINCT FROM _account
          THEN RAISE EXCEPTION 'native_coach_request_conflict' USING ERRCODE='23505'; END IF;
          RETURN jsonb_build_object('organization_id',v_old.organization_id,
            'request_id',v_old.request_id,'staff_id',v_old.staff_id,
            'name',v_old.name,'job_role',v_old.job_role,
            'group_id',v_old.group_id,'account_id',v_old.account_id,
            'replayed',true);
        END IF;

        -- A selected group must have an exact canonical territory.
        -- A city-wide catalogue alone does not establish staff assignment.
        IF _owner='lefteria_fc' THEN
          SELECT scope.id INTO v_scope
            FROM club.teams t
            JOIN club.football_operational_scopes scope
              ON scope.organization_id=t.organization_id AND scope.team_id=t.id
            WHERE t.organization_id=_owner AND t.id=_group
            FOR SHARE OF t,scope;
        ELSE
          SELECT mapping.scope_id,mapping.location_id
            INTO v_scope,v_location
            FROM club.academy_group_location_assignments mapping
            JOIN club.football_operational_scopes scope ON scope.id=mapping.scope_id
              AND scope.organization_id=mapping.organization_id
            JOIN club.football_locations site ON site.id=mapping.location_id
              AND site.city_id=scope.academy_city_id
            JOIN club.academy_groups g ON g.id=mapping.group_id
              AND g.organization_id=mapping.organization_id
            WHERE mapping.organization_id=_owner AND mapping.group_id=_group
            FOR SHARE OF mapping,scope,site,g;
        END IF;
        IF v_scope IS NULL
        THEN RAISE EXCEPTION 'native_coach_unassigned_territory' USING ERRCODE='23514'; END IF;

        -- Optional link is an existing, enabled, verified Coach account.
        -- No signup, role grant, mobile login or phone matching is implied.
        IF _account IS NOT NULL THEN
          SELECT a.role INTO v_subject_role
            FROM identity.admin_accounts a WHERE a.id=_account
              AND a.is_active AND NOT a.invite_pending
              AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL
            FOR UPDATE;
          IF v_subject_role IS DISTINCT FROM 'coach'
            OR _account=v_actor
          THEN RAISE EXCEPTION 'native_coach_account_denied' USING ERRCODE='42501'; END IF;
          PERFORM 1 FROM identity.admin_organization_grants g
            WHERE g.admin_account_id=_account AND g.organization_id=_owner FOR SHARE;
          IF NOT FOUND OR EXISTS(
            SELECT 1 FROM identity.admin_staff_links
              WHERE admin_account_id=_account)
          THEN RAISE EXCEPTION 'native_coach_account_denied' USING ERRCODE='42501'; END IF;
        END IF;

        v_id='stf_admin_'||replace(gen_random_uuid()::text,'-','');
        INSERT INTO club.staff(id,name,job_role,team_type,is_active,is_archived,
          has_explicit_active,has_explicit_archived)
        VALUES(v_id,_name,_job_role,
          CASE WHEN _owner='lefteria_fc' THEN 'First Team' ELSE 'Academy' END,
          true,false,true,true);
        IF _owner='lefteria_fc' THEN
          INSERT INTO club.staff_teams(staff_id,team_id,organization_id,
            from_plural,from_singular,from_admin)
          VALUES(v_id,_group,_owner,false,false,true);
        ELSE
          INSERT INTO club.staff_academy_groups(
            staff_id,academy_group_id,organization_id,
            from_plural,from_singular,from_group_coach,from_admin)
          VALUES(v_id,_group,_owner,false,false,false,true);
        END IF;
        IF _account IS NOT NULL THEN
          INSERT INTO identity.admin_staff_links(admin_account_id,staff_id)
          VALUES(_account,v_id);
        END IF;
        INSERT INTO club.admin_native_coach_requests(
          request_id,staff_id,actor_id,organization_id,
          group_id,account_id,name,job_role)
        VALUES(_request,v_id,v_actor,_owner,_group,_account,_name,_job_role);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,subject_digest,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.coach.provision','succeeded',_owner,
          md5(v_id),CASE WHEN _account IS NULL
            THEN 'native_coach_created' ELSE 'native_coach_linked' END);
        RETURN jsonb_build_object('organization_id',_owner,
          'request_id',_request,'staff_id',v_id,'name',_name,
          'job_role',_job_role,'group_id',_group,
          'account_id',_account,'replayed',false);
      END $native_coach$;
      REVOKE ALL ON FUNCTION club.create_admin_native_coach(
        text,text,uuid,text,text,text,uuid) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.create_admin_native_coach(text,text,uuid,text,text,text,uuid);
      DROP TABLE club.admin_native_coach_requests;
      ALTER TABLE club.staff_teams DROP CONSTRAINT ck_staff_team_origin;
      ALTER TABLE club.staff_teams ADD CONSTRAINT ck_staff_team_origin
        CHECK (from_plural OR from_singular);
      ALTER TABLE club.staff_academy_groups DROP CONSTRAINT ck_staff_group_origin;
      ALTER TABLE club.staff_academy_groups ADD CONSTRAINT ck_staff_group_origin
        CHECK (from_plural OR from_singular OR from_group_coach);
      ALTER TABLE club.staff_teams DROP COLUMN from_admin;
      ALTER TABLE club.staff_academy_groups DROP COLUMN from_admin;
      """);
}
