using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds native, initially-private sponsor and partner entries. The existing
/// imported editorial rows, versions, and approved copy remain unchanged.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261121900000_NativeSponsorshipDrafts")]
public sealed class NativeSponsorshipDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE public_content.sponsorship_editorial
        ADD COLUMN is_native boolean NOT NULL DEFAULT false;
      ALTER TABLE public_content.sponsorship_editorial
        ADD CONSTRAINT ck_sponsorship_native_identity
        CHECK(NOT is_native OR item_id ~ '^cms-[a-f0-9]{32}$');

      -- Reuse the previously reviewed, narrowly granted public projection.
      -- Only an approved snapshot is visible: never drafts, versions, or actor data.
      CREATE OR REPLACE FUNCTION public_content.read_published_sponsorship(_org text)
      RETURNS jsonb LANGUAGE sql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $approved_sponsorship$
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'kind',p.kind,'item_id',p.item_id,
          'content',p.published_json,'is_native',p.is_native
        ) ORDER BY p.kind,p.item_id),'[]'::jsonb)
        FROM (
          SELECT kind,item_id,published_json,is_native
            FROM public_content.sponsorship_editorial
           WHERE organization_id=_org AND published_json IS NOT NULL
             AND _org IN ('lefteria_fc','lefteria_fc_academy')
           ORDER BY kind,item_id LIMIT 400
        ) p;
      $approved_sponsorship$;
      REVOKE ALL ON FUNCTION public_content.read_published_sponsorship(text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $native_sponsor_recovery$
      BEGIN
        IF EXISTS(SELECT 1 FROM public_content.sponsorship_editorial WHERE is_native)
          OR EXISTS(SELECT 1 FROM audit.security_events
                    WHERE action='admin.sponsorship.create')
        THEN RAISE EXCEPTION 'native_sponsorship_requires_forward_recovery'; END IF;
      END $native_sponsor_recovery$;

      -- The former allowlisted projection is restored without touching imported edits.
      CREATE OR REPLACE FUNCTION public_content.read_published_sponsorship(_org text)
      RETURNS jsonb LANGUAGE sql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $approved_sponsorship$
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'kind',p.kind,'item_id',p.item_id,'content',p.published_json
        ) ORDER BY p.kind,p.item_id),'[]'::jsonb)
        FROM (
          SELECT kind,item_id,published_json
            FROM public_content.sponsorship_editorial
           WHERE organization_id=_org AND published_json IS NOT NULL
             AND _org IN ('lefteria_fc','lefteria_fc_academy')
           ORDER BY kind,item_id LIMIT 400
        ) p;
      $approved_sponsorship$;
      REVOKE ALL ON FUNCTION public_content.read_published_sponsorship(text) FROM PUBLIC;
      ALTER TABLE public_content.sponsorship_editorial
        DROP CONSTRAINT ck_sponsorship_native_identity;
      ALTER TABLE public_content.sponsorship_editorial DROP COLUMN is_native;
      """);
}
