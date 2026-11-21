using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Audited private draft and manager-approved public projection for imported sponsor/partner copy.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115980000_SponsorshipEditorial")]
public sealed class SponsorshipEditorial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE public_content.sponsorship_editorial (
        organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
        kind varchar(12) NOT NULL CHECK(kind IN ('sponsor','partner')),
        item_id varchar(128) COLLATE "C" NOT NULL CHECK(length(item_id) BETWEEN 1 AND 128),
        version bigint NOT NULL DEFAULT 1 CHECK(version > 0),
        draft_json jsonb NOT NULL CHECK(jsonb_typeof(draft_json) = 'object'),
        published_json jsonb NULL CHECK(published_json IS NULL OR jsonb_typeof(published_json) = 'object'),
        published_at timestamptz NULL,
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CONSTRAINT pk_sponsorship_editorial PRIMARY KEY(organization_id,kind,item_id),
        CONSTRAINT ck_sponsorship_publication CHECK((published_json IS NULL) = (published_at IS NULL))
      );
      REVOKE ALL ON public_content.sponsorship_editorial FROM PUBLIC;

      -- Public runtime gets ONLY approved content through a fixed projection.
      -- Drafts, unpublished changes, version and audit evidence stay private.
      CREATE FUNCTION public_content.read_published_sponsorship(_org text)
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

      -- Admin runtime can only verify that an imported, currently active
      -- source identity exists. It cannot read other public-sections records.
      CREATE FUNCTION public_content.current_sponsorship_exists(_org text,_kind text,_id text)
      RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $sponsorship_identity$
        SELECT _org IN ('lefteria_fc','lefteria_fc_academy')
          AND _kind IN ('sponsor','partner')
          AND EXISTS(
            SELECT 1 FROM public_sections.documents d
              JOIN public_sections.current c ON c.snapshot_version=d.snapshot_version
            WHERE c.id='current' AND d.kind=_kind
              AND d.organization_id=_org AND d.id=_id
          );
      $sponsorship_identity$;
      REVOKE ALL ON FUNCTION public_content.current_sponsorship_exists(text,text,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $sponsorship_recovery$
      BEGIN
        IF EXISTS(SELECT 1 FROM public_content.sponsorship_editorial LIMIT 1)
          OR EXISTS(SELECT 1 FROM audit.security_events
            WHERE action LIKE 'admin.sponsorship.%')
        THEN RAISE EXCEPTION 'sponsorship_editorial_requires_forward_recovery'; END IF;
      END $sponsorship_recovery$;
      DROP FUNCTION public_content.current_sponsorship_exists(text,text,text);
      DROP FUNCTION public_content.read_published_sponsorship(text);
      DROP TABLE public_content.sponsorship_editorial;
      """);
}
