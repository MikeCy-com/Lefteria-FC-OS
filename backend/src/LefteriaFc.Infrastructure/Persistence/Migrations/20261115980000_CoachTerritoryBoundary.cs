using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Exact football territory boundaries for coach directories and assignment
/// changes. Migration preserves the Super Admin legacy workflow while
/// denying cross-location/cross-squad manager visibility and writes.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115980000_CoachTerritoryBoundary")]
public sealed class CoachTerritoryBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

      CREATE OR REPLACE FUNCTION club.read_admin_coaches(
        _digest text,_owner text,_offset integer,_limit integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $coach_read$
      DECLARE v_actor uuid;v_role text;v_ready boolean;v_result jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 20000
          OR _limit IS NULL OR _limit NOT BETWEEN 1 AND 50
        THEN RAISE EXCEPTION 'invalid_coach_filters' USING ERRCODE='22023'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s JOIN identity.admin_accounts a
            ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'coach_directory_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
          OR NOT EXISTS(SELECT 1 FROM migration.staff_relationship_import_state
                        WHERE id='initial') THEN
          RAISE EXCEPTION 'coach_directory_unready' USING ERRCODE='55000';
        END IF;

        -- Unplaced historical groups remain visible to an authorized Super
        -- Admin, but are never guessed into a manager's territory.
        -- UNION removes duplicate historic designated-coach relationships.
        WITH all_memberships AS (
          SELECT st.staff_id,st.team_id AS group_id,fs.id AS scope_id,
                 NULL::uuid AS location_id
          FROM club.staff_teams st
          LEFT JOIN club.football_operational_scopes fs
            ON fs.organization_id=st.organization_id AND fs.team_id=st.team_id
          WHERE _owner='lefteria_fc' AND st.organization_id=_owner
          UNION
          SELECT sg.staff_id,sg.academy_group_id,
                 ga.scope_id,ga.location_id
          FROM club.staff_academy_groups sg
          LEFT JOIN club.academy_group_location_assignments ga
            ON ga.organization_id=sg.organization_id
           AND ga.group_id=sg.academy_group_id
          WHERE _owner='lefteria_fc_academy' AND sg.organization_id=_owner
          UNION
          SELECT gc.staff_id,gc.academy_group_id,
                 ga.scope_id,ga.location_id
          FROM club.academy_group_coaches gc
          LEFT JOIN club.academy_group_location_assignments ga
            ON ga.organization_id=gc.organization_id
           AND ga.group_id=gc.academy_group_id
          WHERE _owner='lefteria_fc_academy' AND gc.organization_id=_owner
        ), permitted_groups AS (
          SELECT DISTINCT m.staff_id,m.group_id FROM all_memberships m
          WHERE v_role='super_admin'
             OR (m.scope_id IS NOT NULL
               AND club.admin_football_allowed(
                 v_actor,v_role,_owner,m.scope_id,m.location_id,false))
        ), eligible_staff AS (
          SELECT s.id,s.name,s.job_role FROM club.staff s
          WHERE s.is_active AND NOT s.is_archived
            AND s.job_role IN ('Head Coach','Assistant Coach',
              'Goalkeeper Coach','Fitness Coach','Youth Coach')
            AND (
              (v_role='super_admin' AND (
                 s.team_type=CASE WHEN _owner='lefteria_fc'
                   THEN 'First Team' ELSE 'Academy' END
                 OR EXISTS(SELECT 1 FROM all_memberships m WHERE m.staff_id=s.id)))
              OR (v_role='manager' AND EXISTS(
                 SELECT 1 FROM permitted_groups p WHERE p.staff_id=s.id))
            )
        ), page_rows AS (
          SELECT e.id,e.name,e.job_role FROM eligible_staff e
          ORDER BY e.name,e.id LIMIT _limit OFFSET _offset
        )
        SELECT jsonb_build_object(
          'organization_id',_owner,'ready',true,'offset',_offset,'limit',_limit,
          'total',(SELECT count(*)::integer FROM eligible_staff),
          'coaches',COALESCE((
            SELECT jsonb_agg(jsonb_build_object(
              'id',p.id,'name',p.name,'job_role',p.job_role,
              'group_ids',COALESCE((
                SELECT jsonb_agg(g.group_id ORDER BY g.group_id)
                  FROM permitted_groups g WHERE g.staff_id=p.id
              ),'[]'::jsonb)) ORDER BY p.name,p.id)
            FROM page_rows p),'[]'::jsonb)
        ) INTO v_result;

        IF (v_result->>'total')::integer>25000 THEN
          RAISE EXCEPTION 'coach_directory_overflow' USING ERRCODE='54000'; END IF;
        RETURN v_result;
      END $coach_read$;

        CREATE OR REPLACE FUNCTION club.write_admin_coach_assignment(
          _digest text,_owner text,_staff text,_expected jsonb,_desired jsonb
        ) RETURNS jsonb
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $coach_assignment$
        DECLARE
          v_actor uuid;
          v_session uuid;
          v_role text;
          v_current jsonb;
          v_expected jsonb;
          v_desired jsonb;
          v_id text;
          v_count integer;
          v_team_type text;
          v_scope uuid;
          v_location uuid;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
             OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
             OR _staff IS NULL OR length(_staff) NOT BETWEEN 1 AND 128
             OR _staff !~ '^[A-Za-z0-9_-]+$'
             OR jsonb_typeof(_expected) IS DISTINCT FROM 'array'
             OR jsonb_typeof(_desired) IS DISTINCT FROM 'array'
             OR jsonb_array_length(_expected)>20 OR jsonb_array_length(_desired)>20 THEN
            RAISE EXCEPTION 'invalid_coach_assignment' USING ERRCODE='22023';
          END IF;
          FOR v_id IN SELECT value FROM jsonb_array_elements_text(_expected)
            UNION ALL SELECT value FROM jsonb_array_elements_text(_desired)
          LOOP
            IF v_id IS NULL OR length(v_id) NOT BETWEEN 1 AND 128
               OR v_id !~ '^[A-Za-z0-9_-]+$' THEN
              RAISE EXCEPTION 'invalid_coach_assignment_group' USING ERRCODE='22023';
            END IF;
          END LOOP;
          SELECT count(DISTINCT value) INTO v_count FROM jsonb_array_elements_text(_expected);
          IF v_count<>jsonb_array_length(_expected) THEN
            RAISE EXCEPTION 'duplicate_expected_group' USING ERRCODE='22023';
          END IF;
          SELECT count(DISTINCT value) INTO v_count FROM jsonb_array_elements_text(_desired);
          IF v_count<>jsonb_array_length(_desired) THEN
            RAISE EXCEPTION 'duplicate_desired_group' USING ERRCODE='22023';
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
            RAISE EXCEPTION 'coach_assignment_denied' USING ERRCODE='42501';
          END IF;
          IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
            OR NOT EXISTS(SELECT 1 FROM migration.staff_relationship_import_state
                WHERE id='initial') THEN
            RAISE EXCEPTION 'coach_assignment_unready' USING ERRCODE='55000';
          END IF;

          -- This staff-row lock serializes edits to the same coach across
          -- organizations while the authorization lock guards revocations.
          SELECT st.team_type INTO v_team_type FROM club.staff st
          WHERE st.id=_staff AND st.is_active AND NOT st.is_archived
            AND st.job_role IN ('Head Coach','Assistant Coach','Goalkeeper Coach',
              'Fitness Coach','Youth Coach')
          FOR UPDATE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'coach_not_eligible' USING ERRCODE='22023';
          END IF;
          -- A shared staff identity can have assignments in two owners.
          -- Do not permit owner-wide enumeration of staff with no association.
          IF (v_team_type<>(CASE WHEN _owner='lefteria_fc'
              THEN 'First Team' ELSE 'Academy' END))
            AND NOT EXISTS(SELECT 1 FROM club.staff_teams t
                WHERE _owner='lefteria_fc' AND t.staff_id=_staff
                  AND t.organization_id=_owner)
            AND NOT EXISTS(SELECT 1 FROM club.staff_academy_groups g
                WHERE _owner='lefteria_fc_academy' AND g.staff_id=_staff
                  AND g.organization_id=_owner)
            AND NOT EXISTS(SELECT 1 FROM club.academy_group_coaches c
                WHERE _owner='lefteria_fc_academy' AND c.staff_id=_staff
                  AND c.organization_id=_owner) THEN
            RAISE EXCEPTION 'coach_not_in_owner' USING ERRCODE='22023';
          END IF;

          IF _owner='lefteria_fc' THEN
            SELECT coalesce(jsonb_agg(t.team_id ORDER BY t.team_id),'[]'::jsonb)
              INTO v_current FROM club.staff_teams t
              WHERE t.staff_id=_staff AND t.organization_id=_owner;
            SELECT count(*) INTO v_count FROM club.teams t
              WHERE t.organization_id=_owner
                AND t.id IN (SELECT value FROM jsonb_array_elements_text(_desired));
          ELSE
            SELECT coalesce(jsonb_agg(g.group_id ORDER BY g.group_id),'[]'::jsonb)
              INTO v_current
            FROM (
              SELECT sg.academy_group_id AS group_id FROM club.staff_academy_groups sg
              WHERE sg.staff_id=_staff AND sg.organization_id=_owner
              UNION
              SELECT gc.academy_group_id FROM club.academy_group_coaches gc
              WHERE gc.staff_id=_staff AND gc.organization_id=_owner
            ) g;
            SELECT count(*) INTO v_count FROM club.academy_groups g
              WHERE g.organization_id=_owner
                AND g.id IN (SELECT value FROM jsonb_array_elements_text(_desired));
          END IF;
          IF v_count<>jsonb_array_length(_desired) THEN
            RAISE EXCEPTION 'invalid_coach_group' USING ERRCODE='22023';
          END IF;
          SELECT coalesce(jsonb_agg(value ORDER BY value),'[]'::jsonb)
            INTO v_expected FROM jsonb_array_elements_text(_expected);
          SELECT coalesce(jsonb_agg(value ORDER BY value),'[]'::jsonb)
            INTO v_desired FROM jsonb_array_elements_text(_desired);
          -- Manager rights are on exact First Team squads and Academy sites.
          -- The full existing membership is checked, not merely requested
          -- additions: a shared coach cannot be moved from another site.
          -- Super Admin alone may manage orphaned legacy staff records.
          IF v_role='manager' THEN
            IF jsonb_array_length(v_current)=0 THEN
              RAISE EXCEPTION 'coach_assignment_territory_denied' USING ERRCODE='42501';
            END IF;
            FOR v_id IN
              SELECT DISTINCT grp FROM (
                SELECT jsonb_array_elements_text(v_current) AS grp
                UNION ALL SELECT jsonb_array_elements_text(v_desired) AS grp
              ) changes
            LOOP
              v_scope:=NULL;
              v_location:=NULL;
              IF _owner='lefteria_fc' THEN
                SELECT scope.id INTO v_scope FROM club.football_operational_scopes scope
                WHERE scope.organization_id=_owner AND scope.team_id=v_id;
              ELSE
                SELECT mapping.scope_id,mapping.location_id
                  INTO v_scope,v_location
                FROM club.academy_group_location_assignments mapping
                WHERE mapping.organization_id=_owner AND mapping.group_id=v_id;
              END IF;
              IF v_scope IS NULL OR NOT club.admin_football_allowed(
                   v_actor,v_role,_owner,v_scope,v_location,true) THEN
                RAISE EXCEPTION 'coach_assignment_territory_denied' USING ERRCODE='42501';
              END IF;
            END LOOP;
          END IF;
          IF v_current<>v_expected THEN
            RAISE EXCEPTION 'coach_assignment_conflict' USING ERRCODE='40001';
          END IF;
          IF _owner='lefteria_fc' THEN
            DELETE FROM club.staff_teams t WHERE t.staff_id=_staff
              AND t.organization_id=_owner
              AND NOT (v_desired ? t.team_id);
            INSERT INTO club.staff_teams(staff_id,team_id,organization_id,
                from_plural,from_singular)
            SELECT _staff,value,_owner,true,false
              FROM jsonb_array_elements_text(v_desired)
            ON CONFLICT(staff_id,team_id) DO NOTHING;
          ELSE
            -- The designated Academy coach is an independent historical role.
            -- Removing that group's membership requires a separate controlled
            -- primary-coach reassignment; never silently unlink or orphan it.
            IF EXISTS(SELECT 1 FROM club.academy_group_coaches gc
                WHERE gc.staff_id=_staff AND gc.organization_id=_owner
                  AND NOT (v_desired ? gc.academy_group_id)) THEN
              RAISE EXCEPTION 'designated_coach_link_conflict' USING ERRCODE='40001';
            END IF;
            DELETE FROM club.staff_academy_groups g
              WHERE g.staff_id=_staff AND g.organization_id=_owner
                AND NOT (v_desired ? g.academy_group_id);
            INSERT INTO club.staff_academy_groups(
              staff_id,academy_group_id,organization_id,
              from_plural,from_singular,from_group_coach)
            SELECT _staff,value,_owner,true,false,false
              FROM jsonb_array_elements_text(v_desired)
            ON CONFLICT(staff_id,academy_group_id) DO NOTHING;
          END IF;
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,
            device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
            'admin.coach.groups.replace','succeeded',_owner,'staff_group_membership');
          RETURN jsonb_build_object('organization_id',_owner,'staff_id',_staff,
            'group_ids',v_desired);
        END;
        $coach_assignment$;
        REVOKE ALL ON FUNCTION club.write_admin_coach_assignment(
          text,text,text,jsonb,jsonb) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- A rollback must not re-enable the previously permissive manager
      -- directory/writer. Keep the same function signatures but fail closed
      -- for all roles until this migration or an audited successor is applied.
      CREATE OR REPLACE FUNCTION club.read_admin_coaches(
        _digest text,_owner text,_offset integer,_limit integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $rollback_coaches$
      BEGIN
        RAISE EXCEPTION 'coach_directory_upgrade_required' USING ERRCODE='42501';
      END $rollback_coaches$;
      CREATE OR REPLACE FUNCTION club.write_admin_coach_assignment(
        _digest text,_owner text,_staff text,_expected jsonb,_desired jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $rollback_assignment$
      BEGIN
        RAISE EXCEPTION 'coach_assignment_upgrade_required' USING ERRCODE='42501';
      END $rollback_assignment$;
      """);
}
