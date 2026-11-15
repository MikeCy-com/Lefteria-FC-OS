using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Opt-in, read-only geography-bound manager goal reader. The original
/// Super Admin reader and all writers remain unchanged. No coach access.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115920000_DelegatedPlayerGoalRead")]
public sealed class DelegatedPlayerGoalRead : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.read_admin_player_development_goals_manager(
        _digest text,_owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $manager_goals$
      DECLARE
        v_actor uuid; v_role text;
        v_scope uuid; v_city uuid; v_location uuid;
        v_total bigint; v_mapped bigint; v_scope_count bigint;
        v_city_count bigint; v_location_count bigint;
        v_goal_count integer; v_rows jsonb;
      BEGIN
        IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'development_goal_invalid_scope'
          USING ERRCODE='22023'; END IF;

        -- Revalidates session, exact organization membership and revocation
        -- while holding the existing admin authority lock.
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF v_role<>'manager' THEN
          RAISE EXCEPTION 'development_goal_manager_only' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.players p
          WHERE p.id=_player AND p.organization_id=_owner
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
            AND p.team_type=CASE WHEN _owner='lefteria_fc'
              THEN 'First Team' ELSE 'Academy' END)
        THEN RAISE EXCEPTION 'development_goal_player_denied'
          USING ERRCODE='42501'; END IF;

        IF _owner='lefteria_fc' THEN
          -- The team must resolve to precisely one canonical First Team scope.
          SELECT count(*),min(s.id::text)::uuid INTO v_total,v_scope
          FROM club.players p
          JOIN club.football_operational_scopes s
            ON s.organization_id='lefteria_fc' AND s.team_id=p.team_id
          WHERE p.organization_id=_owner AND p.id=_player;
          IF v_total<>1 OR NOT EXISTS(
            SELECT 1 FROM club.football_team_admin_grants g
            WHERE g.admin_account_id=v_actor AND g.scope_id=v_scope
              AND g.role='team_manager' FOR SHARE
          ) THEN RAISE EXCEPTION 'development_goal_territory_denied'
            USING ERRCODE='42501'; END IF;
        ELSE
          -- Fail closed on empty, unmapped, cross-location and cross-city
          -- memberships. A legacy primary group cannot override other groups.
          WITH memberships AS (
            SELECT m.academy_group_id group_id
            FROM club.player_academy_groups m
            WHERE m.organization_id=_owner AND m.player_id=_player
            UNION
            SELECT p.primary_academy_group_id
            FROM club.players p
            WHERE p.organization_id=_owner AND p.id=_player
              AND p.primary_academy_group_id IS NOT NULL
          )
          SELECT count(*),count(*) FILTER (
            WHERE a.scope_id IS NOT NULL AND s.id IS NOT NULL
              AND ag.id IS NOT NULL AND c.is_active AND l.is_active),
            count(DISTINCT a.scope_id),count(DISTINCT a.city_id),
            count(DISTINCT a.location_id),min(a.scope_id::text)::uuid,
            min(a.city_id::text)::uuid,min(a.location_id::text)::uuid
          INTO v_total,v_mapped,v_scope_count,v_city_count,v_location_count,
               v_scope,v_city,v_location
          FROM memberships m
          LEFT JOIN club.academy_groups ag
            ON ag.id=m.group_id AND ag.organization_id=_owner
          LEFT JOIN club.academy_group_location_assignments a
            ON a.organization_id=_owner AND a.group_id=m.group_id
          LEFT JOIN club.football_operational_scopes s
            ON s.id=a.scope_id AND s.organization_id=_owner
              AND s.academy_city_id=a.city_id
          LEFT JOIN club.football_cities c ON c.id=a.city_id
          LEFT JOIN club.football_locations l
            ON l.id=a.location_id AND l.city_id=a.city_id;
          IF v_total NOT BETWEEN 1 AND 20 OR v_mapped<>v_total
            OR v_scope_count<>1 OR v_city_count<>1 OR v_location_count<>1
          THEN RAISE EXCEPTION 'development_goal_territory_ambiguous'
            USING ERRCODE='42501'; END IF;
          -- Exact city or location manager role, not a generic staff/coach grant.
          IF NOT (
            EXISTS (SELECT 1 FROM club.football_city_admin_grants g
              WHERE g.admin_account_id=v_actor AND g.city_id=v_city
                AND g.role='city_manager' FOR SHARE)
            OR EXISTS (SELECT 1 FROM club.football_location_admin_grants g
              WHERE g.admin_account_id=v_actor AND g.location_id=v_location
                AND g.role='location_manager' FOR SHARE)
          ) THEN RAISE EXCEPTION 'development_goal_territory_denied'
            USING ERRCODE='42501'; END IF;
        END IF;

        -- Both the *current* unambiguous site and the *creation-time* goal
        -- origin must match. A transferred child brings no previous site's
        -- private coaching notes to their new manager. Unknown legacy origin
        -- is never inferred from today's roster.
        SELECT count(*) INTO v_goal_count
        FROM club.player_development_goals g
        WHERE g.organization_id=_owner AND g.player_id=_player
          AND g.origin_scope_id=v_scope
          AND (_owner='lefteria_fc'
            OR (g.origin_city_id=v_city AND g.origin_location_id=v_location));
        IF v_goal_count>100 THEN RAISE EXCEPTION 'development_goal_list_too_large'
          USING ERRCODE='54000'; END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',g.id,'title',g.title,'description',g.description,
          'category',g.category,'target_date',g.target_date,
          'source_evaluation_id',NULL,
          'progress',g.progress,'status',g.status,'version',g.version,
          'created_at',g.created_at,'updated_at',g.updated_at,
          'history',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'version',e.version,'event',e.event_kind,
              'progress',e.progress,'status',e.status,'note',e.note,
              'recorded_at',e.recorded_at
            ) ORDER BY e.version DESC),'[]'::jsonb)
            FROM (
              SELECT version,event_kind,progress,status,note,recorded_at
              FROM club.player_development_goal_events
              WHERE organization_id=_owner AND goal_id=g.id
              ORDER BY version DESC LIMIT 50
            ) e
          ),
          'history_count',(
            SELECT count(*) FROM club.player_development_goal_events e
            WHERE e.organization_id=_owner AND e.goal_id=g.id)
        ) ORDER BY g.created_at DESC,g.id),'[]'::jsonb) INTO v_rows
        FROM club.player_development_goals g
        WHERE g.organization_id=_owner AND g.player_id=_player
          AND g.origin_scope_id=v_scope
          AND (_owner='lefteria_fc'
            OR (g.origin_city_id=v_city AND g.origin_location_id=v_location));
        RETURN jsonb_build_object('organization_id',_owner,'player_id',_player,
          'total',v_goal_count,'goals',v_rows);
      END $manager_goals$;
      REVOKE ALL ON FUNCTION
        club.read_admin_player_development_goals_manager(text,text,text)
        FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.read_admin_player_development_goals_manager(text,text,text);
    """);
}
