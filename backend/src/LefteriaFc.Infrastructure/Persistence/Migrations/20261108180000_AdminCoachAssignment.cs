using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Admin-only, audit-atomic staff team/group membership replacement.
/// Existing designated Academy coach links must not be silently changed.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108180000_AdminCoachAssignment")]
public sealed class AdminCoachAssignment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION club.write_admin_coach_assignment(
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

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION club.write_admin_coach_assignment(text,text,text,jsonb,jsonb);
            """);
}
