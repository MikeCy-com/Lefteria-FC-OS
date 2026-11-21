using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private, read-only coach goal progress. Coach identity and exact assigned
/// group are reverified for every request. No evaluations, goal-event notes,
/// full descriptions, documents, guardian details or direct table grants.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261121160000_MobileCoachPlayerGoalSummary")]
public sealed class MobileCoachPlayerGoalSummary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.read_mobile_coach_player_goals(
        _identity uuid,_phone text,_role text,_expires timestamptz,
        _owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $coach_goal_read$
      DECLARE
        v_staff text;v_scope uuid;v_city uuid;v_location uuid;
        v_count integer;v_rows jsonb;
      BEGIN
        IF _identity IS NULL OR _phone IS NULL OR length(_phone)>32
          OR _role IS DISTINCT FROM 'coach' OR _expires IS NULL
          OR _expires<=clock_timestamp()
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player !~ '^[A-Za-z0-9_-]+$'
        THEN RAISE EXCEPTION 'coach_goals_scope_denied' USING ERRCODE='42501';
        END IF;
        -- Do not accept a self-declared coach name. A single active, bound
        -- coach identity must resolve to a single canonical staff record.
        SELECT s.id INTO STRICT v_staff
        FROM identity.mobile_identities i
        JOIN identity.mobile_record_links l
          ON l.identity_id=i.id AND l.role='coach'
            AND l.normalized_phone=i.normalized_phone
            AND l.collection='staff' AND l.kind='staff_own'
        JOIN identity.canonical_contact_bindings b
          ON b.collection=l.collection AND b.kind=l.kind
            AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
        JOIN club.staff s
          ON s.id=l.record_id AND s.is_active AND NOT s.is_archived
        WHERE i.id=_identity AND i.normalized_phone=_phone
          AND i.role='coach' AND i.is_active
        FOR SHARE OF i,l,b,s;

        -- Identity validation precedes roster readiness; unknown callers
        -- must not learn even whether the private roster is initialized.
        IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
          OR NOT EXISTS(SELECT 1 FROM migration.staff_relationship_import_state WHERE id='initial')
        THEN RAISE EXCEPTION 'coach_goals_roster_unready' USING ERRCODE='55000';
        END IF;

        -- Every current Academy membership (including primary group) must
        -- resolve to exactly one ACTIVE city/location and operational scope.
        -- Legacy, unplaced and multi-site player records fail closed.
        SELECT t.scope_id,t.city_id,t.location_id
          INTO v_scope,v_city,v_location
        FROM academy_private.resolve_private_player_territory(_owner,_player) t;
        IF v_scope IS NULL THEN
          RAISE EXCEPTION 'coach_goals_territory_unverified' USING ERRCODE='42501';
        END IF;
        IF NOT club.current_feedback_coach_assignment(
          _identity,_phone,_expires,_owner,v_staff,_player)
        THEN RAISE EXCEPTION 'coach_goals_assignment_denied' USING ERRCODE='42501';
        END IF;

        IF NOT (
          (_owner='lefteria_fc' AND EXISTS(
            SELECT 1 FROM club.players p
            JOIN club.staff_teams st ON st.team_id=p.team_id
              AND st.organization_id=_owner AND st.staff_id=v_staff
            JOIN club.football_operational_scopes fs
              ON fs.organization_id=_owner AND fs.team_id=p.team_id
                AND fs.id=v_scope
            WHERE p.organization_id=_owner AND p.id=_player
              AND p.team_type='First Team' AND p.is_active
              AND NOT p.is_archived AND NOT p.is_transferred
          ))
          OR (_owner='lefteria_fc_academy' AND EXISTS(
            SELECT 1 FROM (
              SELECT sg.academy_group_id AS group_id
                FROM club.staff_academy_groups sg
                WHERE sg.organization_id=_owner AND sg.staff_id=v_staff
                  AND (sg.from_plural OR sg.from_singular OR
                    (sg.from_group_coach AND EXISTS(
                      SELECT 1 FROM club.academy_group_coaches gc
                      WHERE gc.staff_id=v_staff AND gc.organization_id=_owner
                        AND gc.academy_group_id=sg.academy_group_id)))
              UNION
              SELECT gc.academy_group_id FROM club.academy_group_coaches gc
              WHERE gc.organization_id=_owner AND gc.staff_id=v_staff
            ) assigned
            JOIN club.academy_group_location_assignments loc
              ON loc.organization_id=_owner AND loc.group_id=assigned.group_id
              AND loc.scope_id=v_scope AND loc.city_id=v_city
              AND loc.location_id=v_location
            JOIN club.players p ON p.organization_id=_owner AND p.id=_player
              AND p.team_type='Academy' AND p.is_active
              AND NOT p.is_archived AND NOT p.is_transferred
            WHERE p.primary_academy_group_id=assigned.group_id OR EXISTS(
              SELECT 1 FROM club.player_academy_groups pm
              WHERE pm.organization_id=_owner AND pm.player_id=_player
                AND pm.academy_group_id=assigned.group_id)
          ))
        ) THEN
          RAISE EXCEPTION 'coach_goals_exact_group_denied' USING ERRCODE='42501';
        END IF;

        -- Historical origin and live territory must BOTH match. Keep the
        -- response deliberately narrower than manager/private goal readers.
        SELECT count(*)::integer INTO v_count FROM club.player_development_goals g
        WHERE g.organization_id=_owner AND g.player_id=_player
          AND g.status IN ('active','completed')
          AND g.origin_scope_id=v_scope
          AND (_owner='lefteria_fc' OR
            (g.origin_city_id=v_city AND g.origin_location_id=v_location));
        IF v_count>50 THEN
          RAISE EXCEPTION 'coach_goals_list_too_large' USING ERRCODE='54000';
        END IF;
        SELECT COALESCE(jsonb_agg(jsonb_build_object(
          'id',g.id,'title',g.title,'category',g.category,
          'progress',g.progress,'status',g.status,'target_date',g.target_date)
          ORDER BY g.created_at DESC,g.id),'[]'::jsonb)
        INTO v_rows FROM club.player_development_goals g
        WHERE g.organization_id=_owner AND g.player_id=_player
          AND g.status IN ('active','completed')
          AND g.origin_scope_id=v_scope
          AND (_owner='lefteria_fc' OR
            (g.origin_city_id=v_city AND g.origin_location_id=v_location));

        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,action,outcome,
          organization_id,reason_code
        ) VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_identity,
          'mobile.coach.player_goals.read','succeeded',_owner,'exact_group_summary');
        RETURN jsonb_build_object(
          'organization_id',_owner,'player_id',_player,
          'total',v_count,'goals',v_rows);
      EXCEPTION WHEN no_data_found OR too_many_rows THEN
        RAISE EXCEPTION 'coach_goals_identity_denied' USING ERRCODE='42501';
      END $coach_goal_read$;
      REVOKE ALL ON FUNCTION club.read_mobile_coach_player_goals(
        uuid,text,text,timestamptz,text,text) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.read_mobile_coach_player_goals(
        uuid,text,text,timestamptz,text,text);
    """);
}
