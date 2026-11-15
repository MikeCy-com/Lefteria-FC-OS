using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Normalized geography and separate Football/Academy catalogs. No runtime
/// permission is granted here: follow-up admin readers/writers must verify
/// session, role AND authorized city/location in the database transaction.
/// Existing imported fixtures and financial records are deliberately untouched.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115210000_FootballTerritoryCatalogFoundation")]
public sealed class FootballTerritoryCatalogFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.football_cities (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        name varchar(120) NOT NULL,
        slug varchar(120) NOT NULL UNIQUE,
        is_active boolean NOT NULL DEFAULT true,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CHECK(name=btrim(name) AND length(name)>0 AND name !~ '[[:cntrl:]]'),
        CHECK(slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
      );

      CREATE TABLE club.football_locations (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        city_id uuid NOT NULL REFERENCES club.football_cities(id) ON DELETE RESTRICT,
        name varchar(120) NOT NULL,
        slug varchar(120) NOT NULL,
        address varchar(300),
        google_maps_url varchar(2048),
        latitude numeric(9,6), longitude numeric(9,6),
        is_active boolean NOT NULL DEFAULT true,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(city_id,id),
        UNIQUE(city_id,slug),
        CHECK(name=btrim(name) AND length(name)>0 AND name !~ '[[:cntrl:]]'),
        CHECK(slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
        CHECK(address IS NULL OR address !~ '[[:cntrl:]]'),
        CHECK(google_maps_url IS NULL OR (
          (google_maps_url ~ '^https://(www[.])?google[.][a-z.]+/maps(/|[?]|$)'
            OR google_maps_url ~ '^https://maps[.]app[.]goo[.]gl/')
          AND google_maps_url !~ '[[:cntrl:][:space:]]')),
        CHECK(latitude IS NULL OR latitude BETWEEN -90 AND 90),
        CHECK(longitude IS NULL OR longitude BETWEEN -180 AND 180),
        CHECK((latitude IS NULL)=(longitude IS NULL))
      );

      -- Catalog scopes belong to ONE first-team squad or ONE Academy CITY.
      -- All Academy locations inside that city reuse its opponents and fields.
      -- Training bases, groups, staffing, payments and reporting remain tied
      -- to individual locations. No fallback to other cities or Club teams.
      CREATE TABLE club.football_operational_scopes (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        organization_id varchar(32) NOT NULL,
        team_id varchar(128) COLLATE "C",
        academy_city_id uuid REFERENCES club.football_cities(id) ON DELETE RESTRICT,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(organization_id,id),
        UNIQUE(team_id,id),
        UNIQUE(team_id),
        UNIQUE(academy_city_id),
        UNIQUE(academy_city_id,id),
        FOREIGN KEY(organization_id,team_id)
          REFERENCES club.teams(organization_id,id) ON DELETE RESTRICT,
        CHECK(
          (organization_id='lefteria_fc' AND team_id IS NOT NULL
            AND academy_city_id IS NULL)
          OR (organization_id='lefteria_fc_academy'
            AND team_id IS NULL AND academy_city_id IS NOT NULL)
        )
      );

      -- A canonical Academy group belongs to at most one location. A U12
      -- cohort at another site must have its own canonical group identity.
      -- No inferred binding from legacy address strings.
      CREATE TABLE club.academy_group_location_assignments (
        organization_id varchar(32) NOT NULL DEFAULT 'lefteria_fc_academy'
          CHECK(organization_id='lefteria_fc_academy'),
        group_id varchar(128) COLLATE "C" NOT NULL,
        city_id uuid NOT NULL,
        location_id uuid NOT NULL,
        scope_id uuid NOT NULL,
        assigned_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(organization_id,group_id),
        UNIQUE(group_id,scope_id,location_id),
        FOREIGN KEY(organization_id,group_id)
          REFERENCES club.academy_groups(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,scope_id)
          REFERENCES club.football_operational_scopes(organization_id,id)
          ON DELETE RESTRICT,
        FOREIGN KEY(city_id,scope_id)
          REFERENCES club.football_operational_scopes(academy_city_id,id)
          ON DELETE RESTRICT,
        FOREIGN KEY(city_id,location_id)
          REFERENCES club.football_locations(city_id,id)
          ON DELETE RESTRICT
      );
      CREATE INDEX ix_academy_group_location_by_scope
        ON club.academy_group_location_assignments(scope_id,location_id,group_id);

      CREATE TABLE club.football_opponents (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        organization_id varchar(32) NOT NULL,
        scope_id uuid NOT NULL,
        name varchar(160) NOT NULL,
        crest_url varchar(2048),
        is_active boolean NOT NULL DEFAULT true,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(scope_id,id),
        FOREIGN KEY(organization_id,scope_id)
          REFERENCES club.football_operational_scopes(organization_id,id)
          ON DELETE RESTRICT,
        CHECK(name=btrim(name) AND length(name)>0 AND name !~ '[[:cntrl:]]'),
        CHECK(crest_url IS NULL OR (
          crest_url ~ '^https://[^[:space:][:cntrl:]]+$'
          AND crest_url !~ '[[:cntrl:]]'))
      );
      CREATE UNIQUE INDEX ux_football_opponent_name_per_scope
        ON club.football_opponents(scope_id,lower(name));

      CREATE TABLE club.football_playing_fields (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        organization_id varchar(32) NOT NULL,
        scope_id uuid NOT NULL,
        name varchar(160) NOT NULL,
        address varchar(300),
        google_maps_url varchar(2048),
        latitude numeric(9,6), longitude numeric(9,6),
        is_active boolean NOT NULL DEFAULT true,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(scope_id,id),
        FOREIGN KEY(organization_id,scope_id)
          REFERENCES club.football_operational_scopes(organization_id,id)
          ON DELETE RESTRICT,
        CHECK(name=btrim(name) AND length(name)>0 AND name !~ '[[:cntrl:]]'),
        CHECK(google_maps_url IS NULL OR (
          (google_maps_url ~ '^https://(www[.])?google[.][a-z.]+/maps(/|[?]|$)'
            OR google_maps_url ~ '^https://maps[.]app[.]goo[.]gl/')
          AND google_maps_url !~ '[[:cntrl:][:space:]]')),
        CHECK(latitude IS NULL OR latitude BETWEEN -90 AND 90),
        CHECK(longitude IS NULL OR longitude BETWEEN -180 AND 180),
        CHECK((latitude IS NULL)=(longitude IS NULL))
      );
      CREATE UNIQUE INDEX ux_football_field_name_per_scope
        ON club.football_playing_fields(scope_id,lower(name));

      -- Academy training bases are specific to the LOCATION, unlike the
      -- opponent and playing-field catalogs, which are shared across its city.
      CREATE TABLE club.football_training_bases (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        organization_id varchar(32) NOT NULL,
        scope_id uuid NOT NULL,
        academy_city_id uuid,
        academy_location_id uuid,
        name varchar(160) NOT NULL,
        playing_field_id uuid,
        address varchar(300),
        google_maps_url varchar(2048),
        is_active boolean NOT NULL DEFAULT true,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(scope_id,id),
        FOREIGN KEY(organization_id,scope_id)
          REFERENCES club.football_operational_scopes(organization_id,id)
          ON DELETE RESTRICT,
        FOREIGN KEY(scope_id,playing_field_id)
          REFERENCES club.football_playing_fields(scope_id,id)
          ON DELETE RESTRICT,
        FOREIGN KEY(academy_city_id,scope_id)
          REFERENCES club.football_operational_scopes(academy_city_id,id)
          ON DELETE RESTRICT,
        FOREIGN KEY(academy_city_id,academy_location_id)
          REFERENCES club.football_locations(city_id,id)
          ON DELETE RESTRICT,
        CHECK(
          (organization_id='lefteria_fc'
            AND academy_city_id IS NULL AND academy_location_id IS NULL)
          OR (organization_id='lefteria_fc_academy'
            AND academy_city_id IS NOT NULL AND academy_location_id IS NOT NULL)
        ),
        CHECK(name=btrim(name) AND length(name)>0 AND name !~ '[[:cntrl:]]'),
        CHECK(google_maps_url IS NULL OR (
          (google_maps_url ~ '^https://(www[.])?google[.][a-z.]+/maps(/|[?]|$)'
            OR google_maps_url ~ '^https://maps[.]app[.]goo[.]gl/')
          AND google_maps_url !~ '[[:cntrl:][:space:]]'))
      );
      CREATE UNIQUE INDEX ux_football_base_name_per_team
        ON club.football_training_bases(scope_id,lower(name))
        WHERE organization_id='lefteria_fc';
      CREATE UNIQUE INDEX ux_football_base_name_per_academy_location
        ON club.football_training_bases(academy_location_id,lower(name))
        WHERE organization_id='lefteria_fc_academy';

      -- Draft fixtures are intentionally NOT published to calendars or the
      -- public website. Every opponent/field and group/team ID is constrained
      -- against the team (Club) or city (Academy) catalog at the DB boundary.
      -- Academy group and site must match the same exact city AND location.
      CREATE TABLE club.football_match_drafts (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        organization_id varchar(32) NOT NULL,
        scope_id uuid NOT NULL,
        club_team_id varchar(128) COLLATE "C",
        academy_group_id varchar(128) COLLATE "C",
        academy_location_id uuid,
        opponent_id uuid NOT NULL,
        playing_field_id uuid NOT NULL,
        match_date date NOT NULL,
        kickoff_local time without time zone NOT NULL,
        home_game boolean NOT NULL DEFAULT true,
        competition varchar(160) NOT NULL DEFAULT '',
        status varchar(16) NOT NULL DEFAULT 'draft' CHECK(status='draft'),
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY(organization_id,scope_id)
          REFERENCES club.football_operational_scopes(organization_id,id)
          ON DELETE RESTRICT,
        FOREIGN KEY(club_team_id,scope_id)
          REFERENCES club.football_operational_scopes(team_id,id)
          ON DELETE RESTRICT,
        FOREIGN KEY(academy_group_id,scope_id,academy_location_id)
          REFERENCES club.academy_group_location_assignments(group_id,scope_id,location_id)
          ON DELETE RESTRICT,
        FOREIGN KEY(scope_id,opponent_id)
          REFERENCES club.football_opponents(scope_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(scope_id,playing_field_id)
          REFERENCES club.football_playing_fields(scope_id,id)
          ON DELETE RESTRICT,
        CHECK (
          (organization_id='lefteria_fc' AND club_team_id IS NOT NULL
            AND academy_group_id IS NULL AND academy_location_id IS NULL)
          OR (organization_id='lefteria_fc_academy'
            AND club_team_id IS NULL AND academy_group_id IS NOT NULL
            AND academy_location_id IS NOT NULL)
        )
      );
      CREATE INDEX ix_football_match_draft_schedule
        ON club.football_match_drafts(scope_id,match_date,kickoff_local);

      -- Delegated geography is explicit. Possessing an organization manager
      -- role never implicitly grants access to every Academy city or location.
      CREATE TABLE club.football_city_admin_grants (
        admin_account_id uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        city_id uuid NOT NULL REFERENCES club.football_cities(id) ON DELETE RESTRICT,
        role varchar(24) NOT NULL CHECK(role='city_manager'),
        PRIMARY KEY(admin_account_id,city_id,role)
      );
      CREATE TABLE club.football_location_admin_grants (
        admin_account_id uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        location_id uuid NOT NULL
          REFERENCES club.football_locations(id) ON DELETE RESTRICT,
        role varchar(24) NOT NULL
          CHECK(role IN ('location_manager','coach','staff')),
        PRIMARY KEY(admin_account_id,location_id,role)
      );
      CREATE TABLE club.football_team_admin_grants (
        admin_account_id uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        scope_id uuid NOT NULL
          REFERENCES club.football_operational_scopes(id) ON DELETE RESTRICT,
        organization_id varchar(32) NOT NULL DEFAULT 'lefteria_fc'
          CHECK(organization_id='lefteria_fc'),
        role varchar(24) NOT NULL
          CHECK(role IN ('team_manager','coach','staff')),
        PRIMARY KEY(admin_account_id,scope_id,role),
        FOREIGN KEY(organization_id,scope_id)
          REFERENCES club.football_operational_scopes(organization_id,id)
          ON DELETE RESTRICT
      );

      -- Private student counts by location; not an unrestricted public KPI.
      CREATE VIEW club.private_academy_location_roster_counts
        WITH (security_invoker=true)
      AS SELECT l.city_id,l.id AS location_id,
          count(DISTINCT p.id) FILTER (
            WHERE p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          ) AS active_players,
          count(DISTINCT ga.group_id) AS groups
        FROM club.football_locations l
        JOIN club.football_operational_scopes s
          ON s.academy_city_id=l.city_id
          AND s.organization_id='lefteria_fc_academy'
        LEFT JOIN club.academy_group_location_assignments ga
          ON ga.scope_id=s.id AND ga.location_id=l.id
        LEFT JOIN club.player_academy_groups pag
          ON pag.organization_id='lefteria_fc_academy'
          AND pag.academy_group_id=ga.group_id
        LEFT JOIN club.players p
          ON p.organization_id=pag.organization_id AND p.id=pag.player_id
        GROUP BY l.city_id,l.id;

      -- No public, member or general admin runtime gets table access.
      -- Dedicated session-bound functions and explicit grants are required
      -- before enabling CRUD, invoice allocation or location dashboards.
      ALTER TABLE club.football_cities ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.football_locations ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.football_operational_scopes ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.academy_group_location_assignments ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.football_opponents ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.football_playing_fields ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.football_training_bases ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.football_match_drafts ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.football_city_admin_grants ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.football_location_admin_grants ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.football_team_admin_grants ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON TABLE
        club.football_cities,club.football_locations,
        club.football_operational_scopes,
        club.academy_group_location_assignments,
        club.football_opponents,club.football_playing_fields,
        club.football_training_bases,club.football_match_drafts,
        club.football_city_admin_grants,club.football_location_admin_grants,
        club.football_team_admin_grants,
        club.private_academy_location_roster_counts FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Delete only in reverse FK order. No data is automatically transferred
      -- to the old flat model; live production is not a migration target.
      DROP VIEW club.private_academy_location_roster_counts;
      DROP TABLE club.football_team_admin_grants;
      DROP TABLE club.football_location_admin_grants;
      DROP TABLE club.football_city_admin_grants;
      DROP TABLE club.football_match_drafts;
      DROP TABLE club.football_training_bases;
      DROP TABLE club.football_playing_fields;
      DROP TABLE club.football_opponents;
      DROP TABLE club.academy_group_location_assignments;
      DROP TABLE club.football_operational_scopes;
      DROP TABLE club.football_locations;
      DROP TABLE club.football_cities;
    """);
}
