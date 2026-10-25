using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "club");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "auth_rate_buckets",
                schema: "identity",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tokens = table.Column<double>(type: "double precision", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_rate_buckets", x => x.key);
                    table.CheckConstraint("ck_rate_tokens", "tokens >= 0");
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                schema: "club",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    code_digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verification_calls = table.Column<int>(type: "integer", nullable: false),
                    is_consumed = table.Column<bool>(type: "boolean", nullable: false),
                    pending_first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pending_last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pending_birthday = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_challenges", x => x.id);
                    table.CheckConstraint("ck_otp_calls", "verification_calls BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_otp_digest", "octet_length(code_digest) = 32");
                    table.CheckConstraint("ck_otp_expiry", "expires_at > created_at AND expires_at <= created_at + interval '5 minutes'");
                    table.CheckConstraint("ck_otp_phone", "normalized_phone ~ '^[1-9][0-9]{7,14}$'");
                    table.CheckConstraint("ck_pending_registration", "(pending_first_name IS NULL AND pending_last_name IS NULL AND pending_birthday IS NULL) OR (pending_first_name IS NOT NULL AND pending_last_name IS NOT NULL AND pending_birthday IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "security_events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_identity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    subject_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "admin_accounts",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "C"),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    legacy_id = table.Column<string>(type: "text", nullable: true),
                    active_organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_accounts", x => x.id);
                    table.CheckConstraint("ck_admin_account_role", "role IN ('super_admin','manager','editor','marketing','coach','matchday_editor')");
                });

            migrationBuilder.CreateTable(
                name: "admin_organization_grants",
                schema: "identity",
                columns: table => new
                {
                    admin_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_organization_grants", x => new { x.admin_account_id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_admin_organization_grants_admin_accounts_admin_account_id",
                        column: x => x.admin_account_id,
                        principalSchema: "identity",
                        principalTable: "admin_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admin_organization_grants_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "club",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mobile_identities",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    legacy_id = table.Column<string>(type: "text", nullable: true),
                    management_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    birthday = table.Column<DateOnly>(type: "date", nullable: true),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    favorite_team = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    notification_preferences = table.Column<string>(type: "jsonb", nullable: true),
                    platform = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_identities", x => x.id);
                    table.CheckConstraint("ck_management_identity_admin", "(role = 'management') = (management_admin_id IS NOT NULL)");
                    table.CheckConstraint("ck_mobile_identity_phone", "normalized_phone ~ '^[1-9][0-9]{7,14}$'");
                    table.CheckConstraint("ck_mobile_identity_role", "role IN ('parent','coach','management','player','fan')");
                    table.ForeignKey(
                        name: "FK_mobile_identities_admin_accounts_management_admin_id",
                        column: x => x.management_admin_id,
                        principalSchema: "identity",
                        principalTable: "admin_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "club",
                table: "organizations",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { "lefteria_fc", "Lefteria FC" },
                    { "lefteria_fc_academy", "Lefteria FC Academy" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_accounts_id_active_organization_id",
                schema: "identity",
                table: "admin_accounts",
                columns: new[] { "id", "active_organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_accounts_username",
                schema: "identity",
                table: "admin_accounts",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_admin_organization_grants_organization_id",
                schema: "identity",
                table: "admin_organization_grants",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_auth_rate_buckets_updated_at",
                schema: "identity",
                table: "auth_rate_buckets",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_identities_management_admin_id",
                schema: "identity",
                table: "mobile_identities",
                column: "management_admin_id");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_identities_normalized_phone_role",
                schema: "identity",
                table: "mobile_identities",
                columns: new[] { "normalized_phone", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenges_expires_at",
                schema: "identity",
                table: "otp_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenges_normalized_phone",
                schema: "identity",
                table: "otp_challenges",
                column: "normalized_phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_security_events_occurred_at",
                schema: "audit",
                table: "security_events",
                column: "occurred_at");

            migrationBuilder.AddForeignKey(
                name: "FK_admin_accounts_admin_organization_grants_id_active_organiza~",
                schema: "identity",
                table: "admin_accounts",
                columns: new[] { "id", "active_organization_id" },
                principalSchema: "identity",
                principalTable: "admin_organization_grants",
                principalColumns: new[] { "admin_account_id", "organization_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_admin_accounts_admin_organization_grants_id_active_organiza~",
                schema: "identity",
                table: "admin_accounts");

            migrationBuilder.DropTable(
                name: "auth_rate_buckets",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "mobile_identities",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "otp_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "security_events",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "admin_organization_grants",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "admin_accounts",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "organizations",
                schema: "club");
        }
    }
}
