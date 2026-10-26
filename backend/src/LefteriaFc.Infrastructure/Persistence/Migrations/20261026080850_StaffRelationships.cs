using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff",
                schema: "club",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    job_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    team_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    has_explicit_active = table.Column<bool>(type: "boolean", nullable: false),
                    has_explicit_archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff", x => x.id);
                    table.CheckConstraint("ck_staff_job_role", "job_role IN ('Head Coach','Assistant Coach','Goalkeeper Coach','Fitness Coach','Physiotherapist','Team Manager','Youth Coach','Scout')");
                    table.CheckConstraint("ck_staff_team_type", "team_type IN ('First Team','Academy')");
                });

            migrationBuilder.CreateTable(
                name: "staff_relationship_import_state",
                schema: "migration",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    roster_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_commit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    staff_count = table.Column<int>(type: "integer", nullable: false),
                    team_assignments_count = table.Column<int>(type: "integer", nullable: false),
                    group_assignments_count = table.Column<int>(type: "integer", nullable: false),
                    group_coaches_count = table.Column<int>(type: "integer", nullable: false),
                    contacts_count = table.Column<int>(type: "integer", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_relationship_import_state", x => x.id);
                    table.UniqueConstraint("AK_staff_relationship_import_state_digest", x => x.digest);
                    table.CheckConstraint("ck_staff_import_counts", "staff_count >= 0 AND team_assignments_count >= 0 AND group_assignments_count >= 0 AND group_coaches_count >= 0 AND contacts_count >= 0");
                    table.CheckConstraint("ck_staff_initial_state", "id = 'initial' AND digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
                    table.ForeignKey(
                        name: "FK_staff_relationship_import_state_roster_import_state_roster_~",
                        column: x => x.roster_digest,
                        principalSchema: "club",
                        principalTable: "roster_import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "academy_group_coaches",
                schema: "club",
                columns: table => new
                {
                    academy_group_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    staff_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_academy_group_coaches", x => x.academy_group_id);
                    table.ForeignKey(
                        name: "FK_academy_group_coaches_academy_groups_organization_id_academ~",
                        columns: x => new { x.organization_id, x.academy_group_id },
                        principalSchema: "club",
                        principalTable: "academy_groups",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_academy_group_coaches_staff_staff_id",
                        column: x => x.staff_id,
                        principalSchema: "club",
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_academy_groups",
                schema: "club",
                columns: table => new
                {
                    staff_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    academy_group_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_plural = table.Column<bool>(type: "boolean", nullable: false),
                    from_singular = table.Column<bool>(type: "boolean", nullable: false),
                    from_group_coach = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_academy_groups", x => new { x.staff_id, x.academy_group_id });
                    table.CheckConstraint("ck_staff_group_origin", "from_plural OR from_singular OR from_group_coach");
                    table.ForeignKey(
                        name: "FK_staff_academy_groups_academy_groups_organization_id_academy~",
                        columns: x => new { x.organization_id, x.academy_group_id },
                        principalSchema: "club",
                        principalTable: "academy_groups",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_academy_groups_staff_staff_id",
                        column: x => x.staff_id,
                        principalSchema: "club",
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_teams",
                schema: "club",
                columns: table => new
                {
                    staff_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    team_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_plural = table.Column<bool>(type: "boolean", nullable: false),
                    from_singular = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_teams", x => new { x.staff_id, x.team_id });
                    table.CheckConstraint("ck_staff_team_origin", "from_plural OR from_singular");
                    table.ForeignKey(
                        name: "FK_staff_teams_staff_staff_id",
                        column: x => x.staff_id,
                        principalSchema: "club",
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_teams_teams_organization_id_team_id",
                        columns: x => new { x.organization_id, x.team_id },
                        principalSchema: "club",
                        principalTable: "teams",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contact_provenance",
                schema: "migration",
                columns: table => new
                {
                    source_collection = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    field = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    presence = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    raw_value = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    player_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    staff_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    source_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    import_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_provenance", x => new { x.source_collection, x.source_id, x.field });
                    table.CheckConstraint("ck_contact_presence", "(presence IN ('missing','null') AND raw_value IS NULL) OR (presence = 'empty' AND raw_value IS NOT NULL AND raw_value = '') OR (presence = 'value' AND raw_value IS NOT NULL AND length(raw_value) > 0)");
                    table.CheckConstraint("ck_contact_source", "(source_collection = 'players' AND player_id = source_id AND player_id IS NOT NULL AND staff_id IS NULL AND ((field = 'phone' AND kind = 'player_own') OR (field IN ('parent_phone','parent_email','parent_name') AND kind = 'guardian'))) OR (source_collection = 'staff' AND staff_id = source_id AND staff_id IS NOT NULL AND player_id IS NULL AND field IN ('phone','email') AND kind = 'staff_own')");
                    table.ForeignKey(
                        name: "FK_contact_provenance_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "club",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contact_provenance_staff_relationship_import_state_import_d~",
                        column: x => x.import_digest,
                        principalSchema: "migration",
                        principalTable: "staff_relationship_import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contact_provenance_staff_staff_id",
                        column: x => x.staff_id,
                        principalSchema: "club",
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_source_documents",
                schema: "migration",
                columns: table => new
                {
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    import_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_source_documents", x => x.source_id);
                    table.CheckConstraint("ck_staff_source_json", "json_typeof(source_json::json) = 'object'");
                    table.ForeignKey(
                        name: "FK_staff_source_documents_staff_relationship_import_state_impo~",
                        column: x => x.import_digest,
                        principalSchema: "migration",
                        principalTable: "staff_relationship_import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_source_documents_staff_source_id",
                        column: x => x.source_id,
                        principalSchema: "club",
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_academy_group_coaches_organization_id_academy_group_id",
                schema: "club",
                table: "academy_group_coaches",
                columns: new[] { "organization_id", "academy_group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_academy_group_coaches_staff_id",
                schema: "club",
                table: "academy_group_coaches",
                column: "staff_id");

            migrationBuilder.CreateIndex(
                name: "IX_contact_provenance_import_digest",
                schema: "migration",
                table: "contact_provenance",
                column: "import_digest");

            migrationBuilder.CreateIndex(
                name: "IX_contact_provenance_player_id",
                schema: "migration",
                table: "contact_provenance",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "IX_contact_provenance_staff_id",
                schema: "migration",
                table: "contact_provenance",
                column: "staff_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_academy_groups_organization_id_academy_group_id",
                schema: "club",
                table: "staff_academy_groups",
                columns: new[] { "organization_id", "academy_group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_relationship_import_state_roster_digest",
                schema: "migration",
                table: "staff_relationship_import_state",
                column: "roster_digest");

            migrationBuilder.CreateIndex(
                name: "IX_staff_source_documents_import_digest",
                schema: "migration",
                table: "staff_source_documents",
                column: "import_digest");

            migrationBuilder.CreateIndex(
                name: "IX_staff_teams_organization_id_team_id",
                schema: "club",
                table: "staff_teams",
                columns: new[] { "organization_id", "team_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "academy_group_coaches",
                schema: "club");

            migrationBuilder.DropTable(
                name: "contact_provenance",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "staff_academy_groups",
                schema: "club");

            migrationBuilder.DropTable(
                name: "staff_source_documents",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "staff_teams",
                schema: "club");

            migrationBuilder.DropTable(
                name: "staff_relationship_import_state",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "staff",
                schema: "club");
        }
    }
}
