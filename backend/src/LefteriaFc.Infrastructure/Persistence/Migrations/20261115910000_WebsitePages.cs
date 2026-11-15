using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>CMS drafts stay private; only approved snapshots can be projected to the anonymous API.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115910000_WebsitePages")]
public sealed class WebsitePages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE public_content.website_pages (
          organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
          slug varchar(80) COLLATE "C" NOT NULL CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
          version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
          draft_json jsonb NOT NULL CHECK (jsonb_typeof(draft_json) = 'object'),
          published_json jsonb NULL CHECK (published_json IS NULL OR jsonb_typeof(published_json) = 'object'),
          published_at timestamptz NULL,
          updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          CONSTRAINT pk_website_pages PRIMARY KEY (organization_id,slug),
          CONSTRAINT ck_website_publication CHECK ((published_json IS NULL) = (published_at IS NULL))
        );
        REVOKE ALL ON public_content.website_pages FROM PUBLIC;

        -- A fixed, allowlisted security-definer projection is the ONLY way the
        -- public API sees approved pages. Drafts and private versions are excluded.
        CREATE FUNCTION public_content.read_published_website_pages(_org text, _slug text DEFAULT NULL)
        RETURNS jsonb LANGUAGE sql STABLE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp
        AS $website_pages_public$
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'organization_id',p.organization_id,'slug',p.slug,
            'page',p.published_json,'published_at',p.published_at
          ) ORDER BY p.slug),'[]'::jsonb)
          FROM (
            SELECT organization_id,slug,published_json,published_at
              FROM public_content.website_pages
             WHERE organization_id=_org AND published_json IS NOT NULL
               AND (_slug IS NULL OR slug=_slug)
               AND _org IN ('lefteria_fc','lefteria_fc_academy')
             ORDER BY slug LIMIT 100
          ) p;
        $website_pages_public$;
        REVOKE ALL ON FUNCTION public_content.read_published_website_pages(text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $website_pages_recovery$
        BEGIN
          IF EXISTS(SELECT 1 FROM public_content.website_pages LIMIT 1)
             OR EXISTS(SELECT 1 FROM audit.security_events
                  WHERE action LIKE 'admin.website_page.%')
          THEN RAISE EXCEPTION 'website_pages_require_forward_recovery'; END IF;
        END $website_pages_recovery$;
        DROP FUNCTION public_content.read_published_website_pages(text,text);
        DROP TABLE public_content.website_pages;
        """);
}
