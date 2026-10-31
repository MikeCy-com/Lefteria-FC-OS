using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicWebsiteAndWebAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public_content");

            migrationBuilder.AddColumn<string>(
                name: "actor_realm",
                schema: "audit",
                table: "security_events",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "mobile");

            migrationBuilder.CreateTable(
                name: "admin_auth_rate_buckets",
                schema: "identity",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tokens = table.Column<double>(type: "double precision", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_auth_rate_buckets", x => x.key);
                    table.CheckConstraint("ck_admin_auth_rate_tokens", "tokens >= 0 AND tokens <= 10");
                });

            migrationBuilder.CreateTable(
                name: "admin_sessions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    admin_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_sessions", x => x.id);
                    table.CheckConstraint("ck_admin_session_digests", "token_digest ~ '^[0-9a-f]{64}$' AND credential_digest ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_admin_session_expiry", "expires_at > created_at AND expires_at <= created_at + interval '90 days'");
                    table.CheckConstraint("ck_admin_session_revocation", "revoked_at IS NULL OR revoked_at >= created_at");
                    table.ForeignKey(
                        name: "FK_admin_sessions_admin_accounts_admin_account_id",
                        column: x => x.admin_account_id,
                        principalSchema: "identity",
                        principalTable: "admin_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "public_content",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => new { x.kind, x.organization_id, x.id });
                    table.CheckConstraint("ck_public_content_kind", "kind IN ('club','team','academy_group','news','fixture')");
                    table.CheckConstraint("ck_public_content_projection", "(jsonb_typeof(data_json) = 'object' AND data_json->>'organization_id' = organization_id AND data_json->>'id' = id) IS TRUE");
                    table.ForeignKey(
                        name: "FK_documents_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "club",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_state",
                schema: "public_content",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_repository = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_commit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    documents_count = table.Column<int>(type: "integer", nullable: false),
                    sources_count = table.Column<int>(type: "integer", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_state", x => x.id);
                    table.UniqueConstraint("AK_import_state_digest", x => x.digest);
                    table.CheckConstraint("ck_public_content_counts", "documents_count >= 0 AND sources_count >= 0");
                    table.CheckConstraint("ck_public_content_state", "id = 'initial' AND digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
                });

            migrationBuilder.CreateTable(
                name: "public_content_import_evidence",
                schema: "migration",
                columns: table => new
                {
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_bytes = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_content_import_evidence", x => x.digest);
                    table.CheckConstraint("ck_public_content_manifest_bytes", "octet_length(manifest_bytes) BETWEEN 1 AND 33554432");
                    table.ForeignKey(
                        name: "FK_public_content_import_evidence_import_state_digest",
                        column: x => x.digest,
                        principalSchema: "public_content",
                        principalTable: "import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "public_content_source_documents",
                schema: "migration",
                columns: table => new
                {
                    collection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    import_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_content_source_documents", x => new { x.collection, x.ordinal });
                    table.CheckConstraint("ck_public_content_source", "collection IN ('club_profile','news','teams','academy_groups','fixtures') AND ordinal >= 0 AND json_typeof(source_json::json) = 'object'");
                    table.ForeignKey(
                        name: "FK_public_content_source_documents_import_state_import_digest",
                        column: x => x.import_digest,
                        principalSchema: "public_content",
                        principalTable: "import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_actor_realm",
                schema: "audit",
                table: "security_events",
                sql: "actor_realm IN ('mobile','admin')");

            migrationBuilder.CreateIndex(
                name: "IX_admin_auth_rate_buckets_updated_at",
                schema: "identity",
                table: "admin_auth_rate_buckets",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_admin_sessions_admin_account_id",
                schema: "identity",
                table: "admin_sessions",
                column: "admin_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_admin_sessions_expires_at",
                schema: "identity",
                table: "admin_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_admin_sessions_token_digest",
                schema: "identity",
                table: "admin_sessions",
                column: "token_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documents_organization_id_kind_display_order",
                schema: "public_content",
                table: "documents",
                columns: new[] { "organization_id", "kind", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_public_content_source_documents_import_digest",
                schema: "migration",
                table: "public_content_source_documents",
                column: "import_digest");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $web_platform_recovery$
                BEGIN
                  IF EXISTS(SELECT 1 FROM public_content.import_state)
                    OR EXISTS(SELECT 1 FROM public_content.documents)
                    OR EXISTS(SELECT 1 FROM migration.public_content_import_evidence)
                    OR EXISTS(SELECT 1 FROM migration.public_content_source_documents)
                    OR EXISTS(SELECT 1 FROM identity.admin_sessions)
                    OR EXISTS(SELECT 1 FROM audit.security_events WHERE actor_realm='admin')
                  THEN RAISE EXCEPTION 'web_platform_data_requires_forward_recovery'; END IF;
                END
                $web_platform_recovery$;
                """);
            migrationBuilder.DropTable(
                name: "admin_auth_rate_buckets",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "admin_sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "public_content");

            migrationBuilder.DropTable(
                name: "public_content_import_evidence",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "public_content_source_documents",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "import_state",
                schema: "public_content");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_actor_realm",
                schema: "audit",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "actor_realm",
                schema: "audit",
                table: "security_events");
        }
    }
}
