using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// A private CMS workspace, intentionally isolated from imported published
/// document projections. Only the explicitly restricted admin-auth role
/// receives application access through the separate grants script.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115090000_PrivateNewsDraftWorkspace")]
public sealed class PrivateNewsDraftWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE public_content.news_drafts (
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT,
        id varchar(40) COLLATE "C" NOT NULL,
        data_json jsonb NOT NULL,
        version bigint NOT NULL DEFAULT 1,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CONSTRAINT pk_private_news_drafts PRIMARY KEY (organization_id,id),
        CONSTRAINT ck_private_news_draft_id
          CHECK (id ~ '^draft_[0-9a-f]{32}$'),
        CONSTRAINT ck_private_news_draft_version
          CHECK (version > 0),
        CONSTRAINT ck_private_news_draft_payload
          CHECK (jsonb_typeof(data_json) = 'object')
      );
      CREATE INDEX ix_private_news_drafts_updated
        ON public_content.news_drafts (organization_id, updated_at DESC, id);
      REVOKE ALL ON public_content.news_drafts FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Removing unpublished editorial work would silently destroy club data.
      DO $news_draft_recovery$
      BEGIN
        IF EXISTS (SELECT 1 FROM public_content.news_drafts)
          OR EXISTS (SELECT 1 FROM audit.security_events
              WHERE action IN ('admin.news_draft.create','admin.news_draft.update'))
        THEN RAISE EXCEPTION 'news_drafts_require_forward_recovery'; END IF;
      END $news_draft_recovery$;
      DROP TABLE public_content.news_drafts;
      """);
}
