using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Public Academy geography comes ONLY from verified canonical city/location
/// assignments; historic import labels are retained solely for unassigned
/// groups and cannot create an inferred location. Inactive locations/cities
/// are not published. No private players, payments or grants are exposed.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115220000_PublicAcademyVerifiedTerritories")]
public sealed class PublicAcademyVerifiedTerritories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION club.read_public_academy_group_directory()
      RETURNS jsonb
      LANGUAGE sql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp
      AS $public_academy$
        SELECT coalesce(jsonb_agg(
            jsonb_build_object(
              'id',g.id,
              'organization_id','lefteria_fc_academy',
              'name',g.name,
              'age_range',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.age_range,'') ELSE d.data_json->>'age_range' END,
              'city',CASE WHEN a.group_id IS NOT NULL THEN c.name ELSE d.data_json->>'city' END,
              'location_name',CASE WHEN a.group_id IS NOT NULL THEN l.name ELSE d.data_json->>'location_name' END,
              'city_id',a.city_id,
              'location_id',a.location_id,
              'city_slug',c.slug,
              'location_slug',l.slug,
              'training_schedule',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.training_schedule,'') ELSE d.data_json->>'training_schedule' END,
              'description',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.description,'') ELSE d.data_json->>'description' END,
              'banner_url',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.banner_url,'') ELSE d.data_json->>'banner_url' END,
              'season',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.season,'') ELSE d.data_json->>'season' END,
              'display_order',coalesce(p.display_order,(d.data_json->>'display_order')::integer,0)
            ) ORDER BY coalesce(p.display_order,(d.data_json->>'display_order')::integer,0),g.name,g.id
          ),'[]'::jsonb)
          FROM club.academy_groups g
          JOIN club.academy_group_profiles p
            ON p.organization_id=g.organization_id AND p.group_id=g.id
          LEFT JOIN club.academy_group_location_assignments a
            ON a.organization_id=g.organization_id AND a.group_id=g.id
          LEFT JOIN club.football_locations l
            ON l.id=a.location_id AND l.city_id=a.city_id
          LEFT JOIN club.football_cities c ON c.id=a.city_id
          LEFT JOIN public_content.documents d ON d.kind='academy_group'
            AND d.organization_id=g.organization_id AND d.id=g.id
          WHERE g.organization_id='lefteria_fc_academy' AND p.is_public
            AND (a.group_id IS NULL OR (l.is_active AND c.is_active))
        ;
      $public_academy$;

      REVOKE ALL ON FUNCTION club.read_public_academy_group_directory()
        FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION club.read_public_academy_group_directory()
      RETURNS jsonb
      LANGUAGE sql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp
      AS $public_academy$
        SELECT coalesce(jsonb_agg(
            jsonb_build_object(
              'id',g.id,
              'organization_id','lefteria_fc_academy',
              'name',g.name,
              'age_range',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.age_range,'') ELSE d.data_json->>'age_range' END,
              'city',d.data_json->>'city',
              'location_name',d.data_json->>'location_name',
              'training_schedule',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.training_schedule,'') ELSE d.data_json->>'training_schedule' END,
              'description',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.description,'') ELSE d.data_json->>'description' END,
              'banner_url',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.banner_url,'') ELSE d.data_json->>'banner_url' END,
              'season',CASE WHEN p.group_id IS NOT NULL THEN nullif(p.season,'') ELSE d.data_json->>'season' END,
              'display_order',coalesce(p.display_order,(d.data_json->>'display_order')::integer,0)
            ) ORDER BY coalesce(p.display_order,(d.data_json->>'display_order')::integer,0),g.name,g.id
          ),'[]'::jsonb)
          FROM club.academy_groups g
          JOIN club.academy_group_profiles p
            ON p.organization_id=g.organization_id AND p.group_id=g.id
          LEFT JOIN public_content.documents d ON d.kind='academy_group'
            AND d.organization_id=g.organization_id AND d.id=g.id
          WHERE g.organization_id='lefteria_fc_academy' AND p.is_public
        ;
      $public_academy$;

      REVOKE ALL ON FUNCTION club.read_public_academy_group_directory()
        FROM PUBLIC;
    """);
}
