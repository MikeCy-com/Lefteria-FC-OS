using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LegacyAccountImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pending_evidence_id",
                schema: "identity",
                table: "otp_challenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "invite_pending",
                schema: "identity",
                table: "admin_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "normalized_phone",
                schema: "identity",
                table: "admin_accounts",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "otp2fa_enabled",
                schema: "identity",
                table: "admin_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_mobile_identities_id_normalized_phone_role",
                schema: "identity",
                table: "mobile_identities",
                columns: new[] { "id", "normalized_phone", "role" });

            migrationBuilder.CreateTable(
                name: "account_import_state",
                schema: "migration",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    roster_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    staff_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_commit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    exported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    counts_json = table.Column<string>(type: "jsonb", nullable: false),
                    manifest_bytes = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_import_state", x => x.id);
                    table.UniqueConstraint("AK_account_import_state_digest", x => x.digest);
                    table.CheckConstraint("ck_account_import_counts", "jsonb_typeof(counts_json)='object'");
                    table.CheckConstraint("ck_account_import_initial", "id='initial' AND digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
                    table.CheckConstraint("ck_account_manifest_bytes", "octet_length(manifest_bytes) BETWEEN 1 AND 33554432");
                    table.ForeignKey(
                        name: "FK_account_import_state_roster_import_state_roster_digest",
                        column: x => x.roster_digest,
                        principalSchema: "club",
                        principalTable: "roster_import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_import_state_staff_relationship_import_state_staff_~",
                        column: x => x.staff_digest,
                        principalSchema: "migration",
                        principalTable: "staff_relationship_import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admin_staff_links",
                schema: "identity",
                columns: table => new
                {
                    admin_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_staff_links", x => x.admin_account_id);
                    table.ForeignKey(
                        name: "FK_admin_staff_links_admin_accounts_admin_account_id",
                        column: x => x.admin_account_id,
                        principalSchema: "identity",
                        principalTable: "admin_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admin_staff_links_staff_staff_id",
                        column: x => x.staff_id,
                        principalSchema: "club",
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fan_registration_evidence",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legacy_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    normalized_phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    birthday = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    favourite_player_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    notification_preferences_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fan_registration_evidence", x => x.id);
                    table.UniqueConstraint("AK_fan_registration_evidence_id_normalized_phone", x => new { x.id, x.normalized_phone });
                    table.CheckConstraint("ck_fan_evidence_phone", "normalized_phone ~ '^[1-9][0-9]{7,14}$'");
                });

            migrationBuilder.CreateTable(
                name: "imported_admin_grants",
                schema: "migration",
                columns: table => new
                {
                    admin_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provenance = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_imported_admin_grants", x => new { x.admin_account_id, x.organization_id });
                    table.CheckConstraint("ck_imported_grant_origin", "provenance IN ('explicit_plural','explicit_singular','legacy_super_admin','coach_group_backfill','coach_team_backfill')");
                    table.ForeignKey(
                        name: "FK_imported_admin_grants_admin_accounts_admin_account_id",
                        column: x => x.admin_account_id,
                        principalSchema: "identity",
                        principalTable: "admin_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_imported_admin_grants_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "club",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_source_documents",
                schema: "migration",
                columns: table => new
                {
                    collection = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    legacy_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    import_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_source_documents", x => new { x.collection, x.ordinal });
                    table.CheckConstraint("ck_account_source_collection", "collection IN ('mobile_users','fans','admin_users') AND ordinal >= 0");
                    table.CheckConstraint("ck_account_source_disposition", "(disposition='quarantined' AND target_id IS NULL) OR (disposition IN ('mobile','admin','fan_evidence') AND target_id IS NOT NULL)");
                    table.CheckConstraint("ck_account_source_json", "json_typeof(source_json::json)='object'");
                    table.ForeignKey(
                        name: "FK_account_source_documents_account_import_state_import_digest",
                        column: x => x.import_digest,
                        principalSchema: "migration",
                        principalTable: "account_import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "canonical_contact_bindings",
                schema: "identity",
                columns: table => new
                {
                    collection = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    normalized_phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    player_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    staff_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    import_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canonical_contact_bindings", x => new { x.collection, x.source_id, x.kind });
                    table.UniqueConstraint("AK_canonical_contact_bindings_collection_source_id_kind_normal~", x => new { x.collection, x.source_id, x.kind, x.normalized_phone });
                    table.CheckConstraint("ck_canonical_binding_origin", "(collection='players' AND kind IN ('player_own','guardian') AND player_id IS NOT NULL AND player_id=source_id AND staff_id IS NULL) OR (collection='staff' AND kind='staff_own' AND staff_id IS NOT NULL AND staff_id=source_id AND player_id IS NULL)");
                    table.CheckConstraint("ck_canonical_binding_phone", "normalized_phone ~ '^[1-9][0-9]{7,14}$'");
                    table.ForeignKey(
                        name: "FK_canonical_contact_bindings_account_import_state_import_dige~",
                        column: x => x.import_digest,
                        principalSchema: "migration",
                        principalTable: "account_import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_canonical_contact_bindings_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "club",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_canonical_contact_bindings_staff_staff_id",
                        column: x => x.staff_id,
                        principalSchema: "club",
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_import_issues",
                schema: "migration",
                columns: table => new
                {
                    collection = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_import_issues", x => new { x.collection, x.ordinal, x.code });
                    table.CheckConstraint("ck_account_issue_code", "code ~ '^[a-z_]{1,80}$'");
                    table.ForeignKey(
                        name: "FK_account_import_issues_account_source_documents_collection_o~",
                        columns: x => new { x.collection, x.ordinal },
                        principalSchema: "migration",
                        principalTable: "account_source_documents",
                        principalColumns: new[] { "collection", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "legacy_account_mappings",
                schema: "migration",
                columns: table => new
                {
                    collection = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    legacy_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    mobile_identity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    admin_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fan_evidence_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legacy_account_mappings", x => new { x.collection, x.legacy_id });
                    table.CheckConstraint("ck_account_mapping_target", "(collection='mobile_users' AND disposition='mobile' AND mobile_identity_id IS NOT NULL AND mobile_identity_id=target_id AND admin_account_id IS NULL AND fan_evidence_id IS NULL) OR (collection='admin_users' AND disposition='admin' AND admin_account_id IS NOT NULL AND admin_account_id=target_id AND mobile_identity_id IS NULL AND fan_evidence_id IS NULL) OR (collection='fans' AND disposition='fan_evidence' AND fan_evidence_id IS NOT NULL AND fan_evidence_id=target_id AND mobile_identity_id IS NULL AND admin_account_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_legacy_account_mappings_account_source_documents_collection~",
                        columns: x => new { x.collection, x.ordinal },
                        principalSchema: "migration",
                        principalTable: "account_source_documents",
                        principalColumns: new[] { "collection", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_legacy_account_mappings_admin_accounts_admin_account_id",
                        column: x => x.admin_account_id,
                        principalSchema: "identity",
                        principalTable: "admin_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_legacy_account_mappings_fan_registration_evidence_fan_evide~",
                        column: x => x.fan_evidence_id,
                        principalSchema: "identity",
                        principalTable: "fan_registration_evidence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_legacy_account_mappings_mobile_identities_mobile_identity_id",
                        column: x => x.mobile_identity_id,
                        principalSchema: "identity",
                        principalTable: "mobile_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mobile_record_links",
                schema: "identity",
                columns: table => new
                {
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    normalized_phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    collection = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_record_links", x => new { x.identity_id, x.kind, x.record_id });
                    table.CheckConstraint("ck_mobile_record_link_role", "(role='parent' AND collection='players' AND kind='guardian') OR (role='player' AND collection='players' AND kind='player_own') OR (role='coach' AND collection='staff' AND kind='staff_own')");
                    table.ForeignKey(
                        name: "FK_mobile_record_links_canonical_contact_bindings_collection_r~",
                        columns: x => new { x.collection, x.record_id, x.kind, x.normalized_phone },
                        principalSchema: "identity",
                        principalTable: "canonical_contact_bindings",
                        principalColumns: new[] { "collection", "source_id", "kind", "normalized_phone" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mobile_record_links_mobile_identities_identity_id_normalize~",
                        columns: x => new { x.identity_id, x.normalized_phone, x.role },
                        principalSchema: "identity",
                        principalTable: "mobile_identities",
                        principalColumns: new[] { "id", "normalized_phone", "role" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenges_pending_evidence_id_normalized_phone",
                schema: "identity",
                table: "otp_challenges",
                columns: new[] { "pending_evidence_id", "normalized_phone" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_pending_evidence_fields",
                schema: "identity",
                table: "otp_challenges",
                sql: "pending_evidence_id IS NULL OR (pending_first_name IS NOT NULL AND pending_last_name IS NOT NULL AND pending_birthday IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_normalized_phone",
                schema: "identity",
                table: "admin_accounts",
                sql: "normalized_phone IS NULL OR normalized_phone ~ '^[1-9][0-9]{7,14}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_pending_inactive",
                schema: "identity",
                table: "admin_accounts",
                sql: "NOT invite_pending OR NOT is_active");

            migrationBuilder.CreateIndex(
                name: "IX_account_import_state_roster_digest",
                schema: "migration",
                table: "account_import_state",
                column: "roster_digest");

            migrationBuilder.CreateIndex(
                name: "IX_account_import_state_staff_digest",
                schema: "migration",
                table: "account_import_state",
                column: "staff_digest");

            migrationBuilder.CreateIndex(
                name: "IX_account_source_documents_import_digest",
                schema: "migration",
                table: "account_source_documents",
                column: "import_digest");

            migrationBuilder.CreateIndex(
                name: "IX_admin_staff_links_staff_id",
                schema: "identity",
                table: "admin_staff_links",
                column: "staff_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_canonical_contact_bindings_import_digest",
                schema: "identity",
                table: "canonical_contact_bindings",
                column: "import_digest");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_contact_bindings_player_id",
                schema: "identity",
                table: "canonical_contact_bindings",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_contact_bindings_staff_id",
                schema: "identity",
                table: "canonical_contact_bindings",
                column: "staff_id");

            migrationBuilder.CreateIndex(
                name: "IX_fan_registration_evidence_normalized_phone",
                schema: "identity",
                table: "fan_registration_evidence",
                column: "normalized_phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_imported_admin_grants_organization_id",
                schema: "migration",
                table: "imported_admin_grants",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_legacy_account_mappings_admin_account_id",
                schema: "migration",
                table: "legacy_account_mappings",
                column: "admin_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_legacy_account_mappings_collection_ordinal",
                schema: "migration",
                table: "legacy_account_mappings",
                columns: new[] { "collection", "ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_legacy_account_mappings_collection_target_id",
                schema: "migration",
                table: "legacy_account_mappings",
                columns: new[] { "collection", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legacy_account_mappings_fan_evidence_id",
                schema: "migration",
                table: "legacy_account_mappings",
                column: "fan_evidence_id");

            migrationBuilder.CreateIndex(
                name: "IX_legacy_account_mappings_mobile_identity_id",
                schema: "migration",
                table: "legacy_account_mappings",
                column: "mobile_identity_id");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_record_link_primary",
                schema: "identity",
                table: "mobile_record_links",
                column: "identity_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_record_link_single_self",
                schema: "identity",
                table: "mobile_record_links",
                columns: new[] { "identity_id", "role" },
                unique: true,
                filter: "kind <> 'guardian'");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_record_links_collection_record_id_kind_normalized_ph~",
                schema: "identity",
                table: "mobile_record_links",
                columns: new[] { "collection", "record_id", "kind", "normalized_phone" });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_record_links_identity_id_normalized_phone_role",
                schema: "identity",
                table: "mobile_record_links",
                columns: new[] { "identity_id", "normalized_phone", "role" });

            migrationBuilder.AddForeignKey(
                name: "FK_otp_challenges_fan_registration_evidence_pending_evidence_i~",
                schema: "identity",
                table: "otp_challenges",
                columns: new[] { "pending_evidence_id", "normalized_phone" },
                principalSchema: "identity",
                principalTable: "fan_registration_evidence",
                principalColumns: new[] { "id", "normalized_phone" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Imported credentials, provenance and verified profile changes need
            // forward recovery or an approved protected restore, never a drop.
            migrationBuilder.Sql("""
                DO $account_recovery$
                BEGIN
                  IF EXISTS(SELECT 1 FROM migration.account_import_state)
                    OR EXISTS(SELECT 1 FROM migration.account_source_documents)
                    OR EXISTS(SELECT 1 FROM migration.account_import_issues)
                    OR EXISTS(SELECT 1 FROM migration.legacy_account_mappings)
                    OR EXISTS(SELECT 1 FROM migration.imported_admin_grants)
                    OR EXISTS(SELECT 1 FROM identity.fan_registration_evidence)
                    OR EXISTS(SELECT 1 FROM identity.canonical_contact_bindings)
                    OR EXISTS(SELECT 1 FROM identity.mobile_record_links)
                    OR EXISTS(SELECT 1 FROM identity.admin_staff_links)
                    OR EXISTS(SELECT 1 FROM identity.admin_accounts
                      WHERE normalized_phone IS NOT NULL OR invite_pending OR otp2fa_enabled)
                  THEN RAISE EXCEPTION 'account_data_requires_forward_recovery'; END IF;
                END
                $account_recovery$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_otp_challenges_fan_registration_evidence_pending_evidence_i~",
                schema: "identity",
                table: "otp_challenges");

            migrationBuilder.DropTable(
                name: "account_import_issues",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "admin_staff_links",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "imported_admin_grants",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "legacy_account_mappings",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "mobile_record_links",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "account_source_documents",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "fan_registration_evidence",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "canonical_contact_bindings",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "account_import_state",
                schema: "migration");

            migrationBuilder.DropIndex(
                name: "IX_otp_challenges_pending_evidence_id_normalized_phone",
                schema: "identity",
                table: "otp_challenges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pending_evidence_fields",
                schema: "identity",
                table: "otp_challenges");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_mobile_identities_id_normalized_phone_role",
                schema: "identity",
                table: "mobile_identities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_normalized_phone",
                schema: "identity",
                table: "admin_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_pending_inactive",
                schema: "identity",
                table: "admin_accounts");

            migrationBuilder.DropColumn(
                name: "pending_evidence_id",
                schema: "identity",
                table: "otp_challenges");

            migrationBuilder.DropColumn(
                name: "invite_pending",
                schema: "identity",
                table: "admin_accounts");

            migrationBuilder.DropColumn(
                name: "normalized_phone",
                schema: "identity",
                table: "admin_accounts");

            migrationBuilder.DropColumn(
                name: "otp2fa_enabled",
                schema: "identity",
                table: "admin_accounts");
        }
    }
}
