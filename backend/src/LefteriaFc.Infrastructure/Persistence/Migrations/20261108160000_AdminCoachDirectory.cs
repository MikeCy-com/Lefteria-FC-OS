using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108160000_AdminCoachDirectory")]
public sealed class AdminCoachDirectory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION club.read_admin_coaches(
            _digest text,_owner text,_offset integer,_limit integer
        ) RETURNS jsonb
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $admin_coaches$
        DECLARE
            v_role text;
            v_total integer;
            v_coaches jsonb;
        BEGIN
            IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
               OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
               OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 20000
               OR _limit IS NULL OR _limit NOT BETWEEN 1 AND 50 THEN
                RAISE EXCEPTION 'invalid_coach_filters' USING ERRCODE='22023';
            END IF;
            PERFORM identity.lock_admin_club_authority(_digest,_owner);
            SELECT a.role INTO v_role FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
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

            -- An active coach remains visible without an assignment so that a
            -- manager can distinguish unassigned staff from a missing import.
            -- Staff is a shared identity: each owner's membership is projected
            -- independently, never based on staff.team_type alone.
            SELECT count(*) INTO v_total FROM club.staff s
              WHERE s.is_active AND NOT s.is_archived
              AND s.job_role IN ('Head Coach','Assistant Coach',
                  'Goalkeeper Coach','Fitness Coach','Youth Coach')
              AND (s.team_type=CASE WHEN _owner='lefteria_fc'
                  THEN 'First Team' ELSE 'Academy' END
                OR EXISTS(SELECT 1 FROM club.staff_teams st
                    WHERE _owner='lefteria_fc' AND st.staff_id=s.id
                      AND st.organization_id=_owner)
                OR EXISTS(SELECT 1 FROM club.staff_academy_groups sg
                    WHERE _owner='lefteria_fc_academy' AND sg.staff_id=s.id
                      AND sg.organization_id=_owner)
                OR EXISTS(SELECT 1 FROM club.academy_group_coaches gc
                    WHERE _owner='lefteria_fc_academy' AND gc.staff_id=s.id
                      AND gc.organization_id=_owner));

            SELECT coalesce(jsonb_agg(
                jsonb_build_object('id',c.id,'name',c.name,'job_role',c.job_role,
                  'group_ids',CASE WHEN _owner='lefteria_fc' THEN
                      (SELECT coalesce(jsonb_agg(st.team_id ORDER BY st.team_id),'[]'::jsonb)
                       FROM club.staff_teams st WHERE st.staff_id=c.id AND st.organization_id=_owner)
                    ELSE
                      (SELECT coalesce(jsonb_agg(g.group_id ORDER BY g.group_id),'[]'::jsonb)
                       FROM (SELECT sg.academy_group_id AS group_id
                             FROM club.staff_academy_groups sg
                             WHERE sg.staff_id=c.id AND sg.organization_id=_owner
                             UNION
                             SELECT gc.academy_group_id FROM club.academy_group_coaches gc
                             WHERE gc.staff_id=c.id AND gc.organization_id=_owner) g)
                    END) ORDER BY c.name,c.id),'[]'::jsonb) INTO v_coaches
            FROM (
              SELECT s.id,s.name,s.job_role FROM club.staff s
              WHERE s.is_active AND NOT s.is_archived
                AND s.job_role IN ('Head Coach','Assistant Coach',
                    'Goalkeeper Coach','Fitness Coach','Youth Coach')
                AND (s.team_type=CASE WHEN _owner='lefteria_fc'
                    THEN 'First Team' ELSE 'Academy' END
                  OR EXISTS(SELECT 1 FROM club.staff_teams st
                      WHERE _owner='lefteria_fc' AND st.staff_id=s.id
                        AND st.organization_id=_owner)
                  OR EXISTS(SELECT 1 FROM club.staff_academy_groups sg
                      WHERE _owner='lefteria_fc_academy' AND sg.staff_id=s.id
                        AND sg.organization_id=_owner)
                  OR EXISTS(SELECT 1 FROM club.academy_group_coaches gc
                      WHERE _owner='lefteria_fc_academy' AND gc.staff_id=s.id
                        AND gc.organization_id=_owner))
              ORDER BY s.name,s.id LIMIT _limit OFFSET _offset
            ) c;
            IF v_total>25000 THEN
                RAISE EXCEPTION 'coach_directory_overflow' USING ERRCODE='54000';
            END IF;
            RETURN jsonb_build_object('organization_id',_owner,'ready',true,
                'offset',_offset,'limit',_limit,'total',v_total,'coaches',v_coaches);
        END
        $admin_coaches$;
        REVOKE ALL ON FUNCTION club.read_admin_coaches(text,text,integer,integer) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION club.read_admin_coaches(text,text,integer,integer);
            """);
}
