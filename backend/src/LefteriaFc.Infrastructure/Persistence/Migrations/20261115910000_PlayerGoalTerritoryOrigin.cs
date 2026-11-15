using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Immutable, creation-time player-goal origin. Only unambiguous canonical
/// team/city/location memberships are captured; legacy goals and ambiguous
/// memberships remain null and must not be inferred later from a transfer.
/// No new runtime grants or delegated private-goal reads.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115910000_PlayerGoalTerritoryOrigin")]
public sealed class PlayerGoalTerritoryOrigin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE club.player_development_goals
        ADD COLUMN origin_scope_id uuid,
        ADD COLUMN origin_city_id uuid,
        ADD COLUMN origin_location_id uuid,
        ADD COLUMN origin_group_id varchar(128) COLLATE "C",
        ADD COLUMN origin_recorded_at timestamptz,
        ADD CONSTRAINT chk_goal_origin_complete CHECK (
          (origin_scope_id IS NULL AND origin_city_id IS NULL
            AND origin_location_id IS NULL AND origin_group_id IS NULL
            AND origin_recorded_at IS NULL)
          OR (origin_scope_id IS NOT NULL AND origin_recorded_at IS NOT NULL
            AND (
              (organization_id='lefteria_fc' AND origin_city_id IS NULL
                AND origin_location_id IS NULL AND origin_group_id IS NULL)
              OR
              (organization_id='lefteria_fc_academy'
                AND origin_city_id IS NOT NULL AND origin_location_id IS NOT NULL)
            ))
        ),
        ADD CONSTRAINT fk_goal_origin_operational_scope
          FOREIGN KEY(organization_id,origin_scope_id)
          REFERENCES club.football_operational_scopes(organization_id,id)
          ON DELETE RESTRICT,
        ADD CONSTRAINT fk_goal_origin_city_scope
          FOREIGN KEY(origin_city_id,origin_scope_id)
          REFERENCES club.football_operational_scopes(academy_city_id,id)
          ON DELETE RESTRICT,
        ADD CONSTRAINT fk_goal_origin_city_location
          FOREIGN KEY(origin_city_id,origin_location_id)
          REFERENCES club.football_locations(city_id,id)
          ON DELETE RESTRICT,
        ADD CONSTRAINT fk_goal_origin_exact_group
          FOREIGN KEY(origin_group_id,origin_scope_id,origin_location_id)
          REFERENCES club.academy_group_location_assignments(group_id,scope_id,location_id)
          ON DELETE RESTRICT;

      CREATE INDEX ix_goal_origin_site
        ON club.player_development_goals(
          organization_id,origin_scope_id,origin_location_id,player_id)
        WHERE origin_scope_id IS NOT NULL;

      -- BEFORE INSERT snapshots only source-verified roster membership.
      -- Never backfill old rows using a child's current membership. A player
      -- can belong to several groups: all must map to exactly ONE active site.
      CREATE FUNCTION club.capture_player_goal_origin()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $capture_goal_origin$
      DECLARE
        v_scope uuid; v_city uuid; v_location uuid; v_group text;
        v_total bigint; v_mapped bigint; v_scopes bigint;
        v_cities bigint; v_locations bigint; v_groups bigint;
      BEGIN
        IF TG_TABLE_SCHEMA<>'club'
          OR TG_TABLE_NAME<>'player_development_goals' THEN
          RAISE EXCEPTION 'goal_origin_invalid_trigger' USING ERRCODE='42501';
        END IF;

        IF TG_OP='UPDATE' THEN
          IF ROW(NEW.origin_scope_id,NEW.origin_city_id,NEW.origin_location_id,
                 NEW.origin_group_id,NEW.origin_recorded_at)
             IS DISTINCT FROM
             ROW(OLD.origin_scope_id,OLD.origin_city_id,OLD.origin_location_id,
                 OLD.origin_group_id,OLD.origin_recorded_at) THEN
            RAISE EXCEPTION 'goal_origin_is_immutable' USING ERRCODE='42501';
          END IF;
          RETURN NEW;
        END IF;

        IF TG_OP<>'INSERT' OR NEW.origin_scope_id IS NOT NULL
          OR NEW.origin_city_id IS NOT NULL OR NEW.origin_location_id IS NOT NULL
          OR NEW.origin_group_id IS NOT NULL OR NEW.origin_recorded_at IS NOT NULL
        THEN RAISE EXCEPTION 'goal_origin_is_server_set' USING ERRCODE='42501';
        END IF;

        IF NEW.organization_id='lefteria_fc' THEN
          -- A First Team team must have exactly its OWN operational scope.
          SELECT s.id INTO v_scope
          FROM club.players p
          JOIN club.football_operational_scopes s
            ON s.organization_id='lefteria_fc' AND s.team_id=p.team_id
          WHERE p.organization_id='lefteria_fc' AND p.id=NEW.player_id
            AND p.team_type='First Team'
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred;
          IF v_scope IS NOT NULL THEN
            NEW.origin_scope_id:=v_scope;
            NEW.origin_recorded_at:=clock_timestamp();
          END IF;
        ELSIF NEW.organization_id='lefteria_fc_academy' THEN
          -- Count all canonical group assignments, including the optional
          -- legacy primary group. UNION prevents double-counting that group.
          -- LEFT JOIN preserves missing/unmapped groups in the denominator.
          WITH memberships AS (
            SELECT pag.academy_group_id AS group_id
            FROM club.player_academy_groups pag
            WHERE pag.organization_id='lefteria_fc_academy'
              AND pag.player_id=NEW.player_id
            UNION
            SELECT p.primary_academy_group_id AS group_id
            FROM club.players p
            WHERE p.organization_id='lefteria_fc_academy'
              AND p.id=NEW.player_id AND p.primary_academy_group_id IS NOT NULL
          )
          SELECT count(*),
            count(*) FILTER(WHERE ag.id IS NOT NULL
              AND s.id IS NOT NULL AND l.is_active AND c.is_active),
            count(DISTINCT a.scope_id),count(DISTINCT a.city_id),
            count(DISTINCT a.location_id),count(DISTINCT m.group_id),
            min(a.scope_id::text)::uuid,min(a.city_id::text)::uuid,
            min(a.location_id::text)::uuid,min(m.group_id)
          INTO v_total,v_mapped,v_scopes,v_cities,v_locations,v_groups,
               v_scope,v_city,v_location,v_group
          FROM memberships m
          LEFT JOIN club.academy_groups ag
            ON ag.organization_id='lefteria_fc_academy' AND ag.id=m.group_id
          LEFT JOIN club.academy_group_location_assignments a
            ON a.organization_id='lefteria_fc_academy' AND a.group_id=m.group_id
          LEFT JOIN club.football_operational_scopes s
            ON s.id=a.scope_id
              AND s.organization_id='lefteria_fc_academy'
              AND s.academy_city_id=a.city_id
          LEFT JOIN club.football_locations l
            ON l.id=a.location_id AND l.city_id=a.city_id
          LEFT JOIN club.football_cities c ON c.id=a.city_id;
          IF v_total BETWEEN 1 AND 20
            AND v_total=v_mapped AND v_scopes=1 AND v_cities=1
            AND v_locations=1 AND EXISTS (
              SELECT 1 FROM club.players p
              WHERE p.organization_id=NEW.organization_id
                AND p.id=NEW.player_id AND p.team_type='Academy'
                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
            )
          THEN
            NEW.origin_scope_id:=v_scope;
            NEW.origin_city_id:=v_city;
            NEW.origin_location_id:=v_location;
            NEW.origin_group_id:=CASE WHEN v_groups=1 THEN v_group ELSE NULL END;
            NEW.origin_recorded_at:=clock_timestamp();
          END IF;
        END IF;
        -- Unscoped is intentional and inaccessible to future delegated roles.
        RETURN NEW;
      END $capture_goal_origin$;
      REVOKE ALL ON FUNCTION club.capture_player_goal_origin() FROM PUBLIC;

      CREATE TRIGGER tr_goal_origin_capture
        BEFORE INSERT ON club.player_development_goals
        FOR EACH ROW EXECUTE FUNCTION club.capture_player_goal_origin();
      CREATE TRIGGER tr_goal_origin_immutable
        BEFORE UPDATE ON club.player_development_goals
        FOR EACH ROW EXECUTE FUNCTION club.capture_player_goal_origin();
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP TRIGGER tr_goal_origin_immutable ON club.player_development_goals;
      DROP TRIGGER tr_goal_origin_capture ON club.player_development_goals;
      DROP FUNCTION club.capture_player_goal_origin();
      DROP INDEX club.ix_goal_origin_site;
      ALTER TABLE club.player_development_goals
        DROP CONSTRAINT fk_goal_origin_exact_group,
        DROP CONSTRAINT fk_goal_origin_city_location,
        DROP CONSTRAINT fk_goal_origin_city_scope,
        DROP CONSTRAINT fk_goal_origin_operational_scope,
        DROP CONSTRAINT chk_goal_origin_complete,
        DROP COLUMN origin_recorded_at,
        DROP COLUMN origin_group_id,
        DROP COLUMN origin_location_id,
        DROP COLUMN origin_city_id,
        DROP COLUMN origin_scope_id;
    """);
}
