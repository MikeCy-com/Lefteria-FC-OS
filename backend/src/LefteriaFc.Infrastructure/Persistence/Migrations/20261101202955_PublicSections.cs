using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public_sections");

            migrationBuilder.CreateTable(
                name: "snapshots",
                schema: "public_sections",
                columns: table => new
                {
                    version = table.Column<int>(type: "integer", nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    base_public_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_repository = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_commit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    exported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    documents_count = table.Column<int>(type: "integer", nullable: false),
                    sources_count = table.Column<int>(type: "integer", nullable: false),
                    issues_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_snapshots", x => x.version);
                    table.UniqueConstraint("AK_snapshots_digest", x => x.digest);
                    table.CheckConstraint("ck_public_sections_counts", "documents_count >= 0 AND sources_count >= 0 AND issues_count >= 0");
                    table.CheckConstraint("ck_public_sections_snapshot", "version BETWEEN 1 AND 100000 AND digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
                    table.ForeignKey(
                        name: "FK_snapshots_import_state_base_public_digest",
                        column: x => x.base_public_digest,
                        principalSchema: "public_content",
                        principalTable: "import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "current",
                schema: "public_sections",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    snapshot_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_current", x => x.id);
                    table.CheckConstraint("ck_public_sections_current", "id = 'current'");
                    table.ForeignKey(
                        name: "FK_current_snapshots_snapshot_version",
                        column: x => x.snapshot_version,
                        principalSchema: "public_sections",
                        principalTable: "snapshots",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "public_sections",
                columns: table => new
                {
                    snapshot_version = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents1", x => new { x.snapshot_version, x.kind, x.organization_id, x.id });
                    table.CheckConstraint("ck_public_sections_identity", "(jsonb_typeof(data_json) = 'object' AND data_json->>'id' = id AND data_json->>'organization_id' = organization_id) IS TRUE");
                    table.CheckConstraint("ck_public_sections_kind", "kind IN ('player','staff','academy_group','standing','venue','season','gallery','sponsor','partner','event','product','trials','fixture','ticket','archive','review','academy_stats','potm','branding','module')");
                    table.ForeignKey(
                        name: "FK_documents_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "club",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_documents_snapshots_snapshot_version",
                        column: x => x.snapshot_version,
                        principalSchema: "public_sections",
                        principalTable: "snapshots",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "public_sections_evidence",
                schema: "migration",
                columns: table => new
                {
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_bytes = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_sections_evidence", x => x.digest);
                    table.CheckConstraint("ck_public_sections_evidence", "octet_length(manifest_bytes) BETWEEN 1 AND 33554432 AND encode(sha256(manifest_bytes),'hex') = digest");
                    table.ForeignKey(
                        name: "FK_public_sections_evidence_snapshots_digest",
                        column: x => x.digest,
                        principalSchema: "public_sections",
                        principalTable: "snapshots",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "public_sections_source",
                schema: "migration",
                columns: table => new
                {
                    snapshot_version = table.Column<int>(type: "integer", nullable: false),
                    collection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    source_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_sections_source", x => new { x.snapshot_version, x.collection, x.ordinal });
                    table.CheckConstraint("ck_public_sections_source", "ordinal >= 0 AND json_typeof(source_json::json) = 'object' AND collection IN ('players','staff','standings','venues','seasons','gallery','sponsors','partners','marketing_events','products','trials_settings','fixtures','teams','academy_groups','tickets','archived_seasons','reviews','training_sessions','attendance','potm_votes','club_profile')");
                    table.ForeignKey(
                        name: "FK_public_sections_source_snapshots_snapshot_version",
                        column: x => x.snapshot_version,
                        principalSchema: "public_sections",
                        principalTable: "snapshots",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "public_sections_issues",
                schema: "migration",
                columns: table => new
                {
                    snapshot_version = table.Column<int>(type: "integer", nullable: false),
                    collection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_sections_issues", x => new { x.snapshot_version, x.collection, x.ordinal, x.code });
                    table.ForeignKey(
                        name: "FK_public_sections_issues_public_sections_source_snapshot_vers~",
                        columns: x => new { x.snapshot_version, x.collection, x.ordinal },
                        principalSchema: "migration",
                        principalTable: "public_sections_source",
                        principalColumns: new[] { "snapshot_version", "collection", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_current_snapshot_version",
                schema: "public_sections",
                table: "current",
                column: "snapshot_version");

            migrationBuilder.CreateIndex(
                name: "IX_documents_organization_id",
                schema: "public_sections",
                table: "documents",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_snapshots_base_public_digest",
                schema: "public_sections",
                table: "snapshots",
                column: "base_public_digest");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $public_sections_recovery$
                BEGIN
                  IF EXISTS(SELECT 1 FROM public_sections.snapshots)
                    OR EXISTS(SELECT 1 FROM public_sections.current)
                    OR EXISTS(SELECT 1 FROM public_sections.documents)
                    OR EXISTS(SELECT 1 FROM migration.public_sections_source)
                    OR EXISTS(SELECT 1 FROM migration.public_sections_evidence)
                    OR EXISTS(SELECT 1 FROM migration.public_sections_issues)
                    OR EXISTS(SELECT 1 FROM audit.security_events WHERE action='public_sections.import')
                  THEN RAISE EXCEPTION 'public_sections_data_requires_forward_recovery'; END IF;
                END
                $public_sections_recovery$;
                """);
            migrationBuilder.DropTable(
                name: "current",
                schema: "public_sections");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "public_sections");

            migrationBuilder.DropTable(
                name: "public_sections_evidence",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "public_sections_issues",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "public_sections_source",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "snapshots",
                schema: "public_sections");
        }
    }
}
