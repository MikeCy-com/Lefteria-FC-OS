using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Reuse the existing versioned public edit table for already-imported news.
/// Immutable imported records are never updated or deleted.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115100000_PublicNewsEditor")]
public sealed class PublicNewsEditor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE public_content.edits
        DROP CONSTRAINT ck_public_club_edit;
      ALTER TABLE public_content.edits
        ADD CONSTRAINT ck_public_club_edit
        CHECK(kind IN ('club','news') AND version > 0);
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- A rollback that hides saved published changes is not safe. Require
      -- forward recovery if any article was edited (or an audit was created).
      DO $news_editor_recovery$
      BEGIN
        IF EXISTS(SELECT 1 FROM public_content.edits WHERE kind='news')
           OR EXISTS(SELECT 1 FROM audit.security_events WHERE action='admin.news.update')
        THEN RAISE EXCEPTION 'news_edits_require_forward_recovery'; END IF;
      END $news_editor_recovery$;

      ALTER TABLE public_content.edits
        DROP CONSTRAINT ck_public_club_edit;
      ALTER TABLE public_content.edits
        ADD CONSTRAINT ck_public_club_edit
        CHECK(kind='club' AND version > 0);
      """);
}
