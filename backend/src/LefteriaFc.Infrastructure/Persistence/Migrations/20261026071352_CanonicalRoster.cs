using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "migration");

            migrationBuilder.CreateTable(
                name: "academy_groups",
                schema: "club",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_academy_groups", x => x.id);
                    table.UniqueConstraint("AK_academy_groups_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_group_academy", "organization_id = 'lefteria_fc_academy'");
                    table.ForeignKey(
                        name: "FK_academy_groups_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "club",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roster_import_state",
                schema: "club",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_repository = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_commit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    teams_count = table.Column<int>(type: "integer", nullable: false),
                    academy_groups_count = table.Column<int>(type: "integer", nullable: false),
                    players_count = table.Column<int>(type: "integer", nullable: false),
                    memberships_count = table.Column<int>(type: "integer", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_import_state", x => x.id);
                    table.UniqueConstraint("AK_roster_import_state_digest", x => x.digest);
                    table.CheckConstraint("ck_roster_counts", "teams_count >= 0 AND academy_groups_count >= 0 AND players_count >= 0 AND memberships_count >= 0");
                    table.CheckConstraint("ck_roster_initial_state", "id = 'initial' AND digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
                });

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "club",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.id);
                    table.UniqueConstraint("AK_teams_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_team_club", "organization_id = 'lefteria_fc'");
                    table.ForeignKey(
                        name: "FK_teams_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "club",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roster_source_documents",
                schema: "migration",
                columns: table => new
                {
                    collection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    import_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_source_documents", x => new { x.collection, x.source_id });
                    table.CheckConstraint("ck_roster_source_json", "collection IN ('teams','academy_groups','players') AND json_typeof(source_json::json) = 'object'");
                    table.ForeignKey(
                        name: "FK_roster_source_documents_roster_import_state_import_digest",
                        column: x => x.import_digest,
                        principalSchema: "club",
                        principalTable: "roster_import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "players",
                schema: "club",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    team_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    team_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    primary_academy_group_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    image_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    is_transferred = table.Column<bool>(type: "boolean", nullable: false),
                    has_explicit_picker_fields = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_players", x => x.id);
                    table.UniqueConstraint("AK_players_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_player_position", "position IN ('Goalkeeper','Defender','Midfielder','Forward')");
                    table.CheckConstraint("ck_player_team_type", "team_type IN ('First Team','Academy')");
                    table.ForeignKey(
                        name: "FK_players_academy_groups_organization_id_primary_academy_grou~",
                        columns: x => new { x.organization_id, x.primary_academy_group_id },
                        principalSchema: "club",
                        principalTable: "academy_groups",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_players_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "club",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_players_teams_organization_id_team_id",
                        columns: x => new { x.organization_id, x.team_id },
                        principalSchema: "club",
                        principalTable: "teams",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_academy_groups",
                schema: "club",
                columns: table => new
                {
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    player_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    academy_group_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_academy_groups", x => new { x.organization_id, x.player_id, x.academy_group_id });
                    table.ForeignKey(
                        name: "FK_player_academy_groups_academy_groups_organization_id_academ~",
                        columns: x => new { x.organization_id, x.academy_group_id },
                        principalSchema: "club",
                        principalTable: "academy_groups",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_academy_groups_players_organization_id_player_id",
                        columns: x => new { x.organization_id, x.player_id },
                        principalSchema: "club",
                        principalTable: "players",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_player_academy_groups_organization_id_academy_group_id",
                schema: "club",
                table: "player_academy_groups",
                columns: new[] { "organization_id", "academy_group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_players_organization_id_has_explicit_picker_fields_team_typ~",
                schema: "club",
                table: "players",
                columns: new[] { "organization_id", "has_explicit_picker_fields", "team_type", "is_active", "is_archived", "is_transferred", "number", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_players_organization_id_primary_academy_group_id",
                schema: "club",
                table: "players",
                columns: new[] { "organization_id", "primary_academy_group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_players_organization_id_team_id",
                schema: "club",
                table: "players",
                columns: new[] { "organization_id", "team_id" });

            migrationBuilder.CreateIndex(
                name: "IX_roster_source_documents_import_digest",
                schema: "migration",
                table: "roster_source_documents",
                column: "import_digest");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_academy_groups",
                schema: "club");

            migrationBuilder.DropTable(
                name: "roster_source_documents",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "players",
                schema: "club");

            migrationBuilder.DropTable(
                name: "roster_import_state",
                schema: "club");

            migrationBuilder.DropTable(
                name: "academy_groups",
                schema: "club");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "club");
        }
    }
}
