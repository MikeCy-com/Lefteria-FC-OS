using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Read-only, exact-coach group name directory. Web-admin Coach has no
/// organization-wide roster read and obtains no private player details.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261124121500_CoachAssignedGroupDirectory")]
public sealed class CoachAssignedGroupDirectory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.read_admin_coach_assigned_groups(
        _digest text,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $coach_assigned$
      DECLARE v_actor uuid;v_role text;v_staff text;v_groups jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[a-f0-9]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy') THEN
          RAISE EXCEPTION 'invalid_assigned_group_scope' USING ERRCODE='22023';
        END IF;

        -- Lock the live session/account/owner grant before resolving the
        -- explicit staff relationship. Never authorize on client-supplied IDs.
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.id,a.role INTO v_actor,v_role
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g
          ON g.admin_account_id=a.id AND g.organization_id=_owner
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL
          AND s.expires_at>clock_timestamp() AND a.is_active
          AND NOT a.invite_pending AND NOT a.otp2fa_enabled
          AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role IS DISTINCT FROM 'coach' THEN
          RAISE EXCEPTION 'assigned_groups_coach_only' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
          OR NOT EXISTS(SELECT 1 FROM migration.staff_relationship_import_state WHERE id='initial') THEN
          RAISE EXCEPTION 'assigned_groups_not_ready' USING ERRCODE='55000';
        END IF;

        -- An unlinked, archived or inactive staff identity has no groups.
        -- No phone/name matching, cached roles or inferred city membership.
        SELECT l.staff_id INTO v_staff
        FROM identity.admin_staff_links l
        JOIN club.staff st ON st.id=l.staff_id AND st.is_active AND NOT st.is_archived
        WHERE l.admin_account_id=v_actor
        FOR SHARE OF l,st;
        IF v_staff IS NULL THEN
          RETURN jsonb_build_object('organization_id',_owner,'ready',true,'groups','[]'::jsonb);
        END IF;

        -- The two organizations use distinct relationship tables and exact
        -- canonical group IDs. UNION removes duplicate legacy coach links.
        WITH assigned AS (
          SELECT t.id,t.name,'team'::text AS kind
          FROM club.staff_teams st
          JOIN club.teams t
            ON t.organization_id=st.organization_id AND t.id=st.team_id
          WHERE _owner='lefteria_fc' AND st.organization_id=_owner
            AND st.staff_id=v_staff
          UNION
          SELECT g.id,g.name,'academy_group'::text AS kind
          FROM club.academy_groups g
          JOIN club.staff_academy_groups sg
            ON sg.academy_group_id=g.id AND sg.organization_id=g.organization_id
          WHERE _owner='lefteria_fc_academy' AND sg.organization_id=_owner
            AND sg.staff_id=v_staff
          UNION
          SELECT g.id,g.name,'academy_group'::text AS kind
          FROM club.academy_groups g
          JOIN club.academy_group_coaches gc
            ON gc.academy_group_id=g.id AND gc.organization_id=g.organization_id
          WHERE _owner='lefteria_fc_academy' AND gc.organization_id=_owner
            AND gc.staff_id=v_staff
        )
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',id,'name',name,'kind',kind)
            ORDER BY name COLLATE "C",id COLLATE "C"),'[]'::jsonb)
        INTO v_groups FROM assigned;
        IF jsonb_array_length(v_groups)>150 THEN
          RAISE EXCEPTION 'assigned_groups_overflow' USING ERRCODE='54000';
        END IF;
        RETURN jsonb_build_object('organization_id',_owner,'ready',true,'groups',v_groups);
      END $coach_assigned$;
      REVOKE ALL ON FUNCTION club.read_admin_coach_assigned_groups(text,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.read_admin_coach_assigned_groups(text,text);
      """);
}
