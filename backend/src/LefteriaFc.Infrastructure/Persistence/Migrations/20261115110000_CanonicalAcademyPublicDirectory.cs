using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Anonymous Academy directory is projected from the exact canonical groups
/// managed by /admin/groups, never from a disconnected public import alone.
/// Manager-authored public profile fields are safely allowlisted. No player,
/// guardian, staff, phone, attendance or private profile columns are returned.
/// Runtime gets EXECUTE on this fixed reader, never SELECT on either table.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115110000_CanonicalAcademyPublicDirectory")]
public sealed class CanonicalAcademyPublicDirectory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.read_public_academy_group_directory()
      RETURNS jsonb
      LANGUAGE sql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp
      AS $public_academy$
        SELECT CASE WHEN EXISTS(
          SELECT 1 FROM club.roster_import_state WHERE id='initial'
        ) THEN (
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
          LEFT JOIN club.academy_group_profiles p
            ON p.organization_id=g.organization_id AND p.group_id=g.id
          LEFT JOIN public_content.documents d ON d.kind='academy_group'
            AND d.organization_id=g.organization_id AND d.id=g.id
          WHERE g.organization_id='lefteria_fc_academy'
        ) ELSE NULL::jsonb END;
      $public_academy$;

      REVOKE ALL ON FUNCTION club.read_public_academy_group_directory()
        FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION club.read_public_academy_group_directory();
        """);
}
