using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopedCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calendar_import_state",
                schema: "club",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    roster_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_commit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_complete = table.Column<bool>(type: "boolean", nullable: false),
                    exported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    counts_json = table.Column<string>(type: "jsonb", nullable: false),
                    manifest_bytes = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_import_state", x => x.id);
                    table.UniqueConstraint("AK_calendar_import_state_digest", x => x.digest);
                    table.CheckConstraint("ck_calendar_complete", "counts_json ? 'quarantined_rows' AND jsonb_typeof(counts_json->'quarantined_rows')='number' AND (counts_json->>'quarantined_rows')::integer>=0 AND is_complete = ((counts_json->>'quarantined_rows')::integer = 0)");
                    table.CheckConstraint("ck_calendar_counts_json", "jsonb_typeof(counts_json)='object'");
                    table.CheckConstraint("ck_calendar_initial", "id='initial' AND digest ~ '^[0-9a-f]{64}$' AND roster_digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
                    table.CheckConstraint("ck_calendar_manifest_bytes", "octet_length(manifest_bytes) BETWEEN 1 AND 33554432");
                });

            migrationBuilder.CreateTable(
                name: "calendar_source_documents",
                schema: "migration",
                columns: table => new
                {
                    collection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    import_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    target_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    source_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_source_documents", x => new { x.collection, x.ordinal });
                    table.CheckConstraint("ck_calendar_source_collection", "collection IN ('fixtures','events','training_sessions','availability','event_attendance','attendance') AND ordinal>=0");
                    table.CheckConstraint("ck_calendar_source_disposition", "disposition IN ('item','response','quarantined')");
                    table.CheckConstraint("ck_calendar_source_json", "json_typeof(source_json::json)='object'");
                    table.ForeignKey(
                        name: "FK_calendar_source_documents_calendar_import_state_import_dige~",
                        column: x => x.import_digest,
                        principalSchema: "club",
                        principalTable: "calendar_import_state",
                        principalColumn: "digest",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_import_issues",
                schema: "migration",
                columns: table => new
                {
                    collection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_import_issues", x => new { x.collection, x.ordinal, x.code });
                    table.ForeignKey(
                        name: "FK_calendar_import_issues_calendar_source_documents_collection~",
                        columns: x => new { x.collection, x.ordinal },
                        principalSchema: "migration",
                        principalTable: "calendar_source_documents",
                        principalColumns: new[] { "collection", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_items",
                schema: "club",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_collection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_ordinal = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    is_cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    content_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    staff_day = table.Column<DateOnly>(type: "date", nullable: false),
                    staff_end_day = table.Column<DateOnly>(type: "date", nullable: false),
                    member_day = table.Column<DateOnly>(type: "date", nullable: true),
                    member_end_day = table.Column<DateOnly>(type: "date", nullable: true),
                    staff_projection_json = table.Column<string>(type: "jsonb", nullable: false),
                    member_projection_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_items", x => new { x.kind, x.id });
                    table.UniqueConstraint("AK_calendar_items_organization_id_kind_id", x => new { x.organization_id, x.kind, x.id });
                    table.CheckConstraint("ck_calendar_item_kind", "kind IN ('event','match','training')");
                    table.CheckConstraint("ck_calendar_item_origin", "(kind='match' AND source_collection='fixtures') OR (kind='event' AND source_collection='events') OR (kind='training' AND source_collection='training_sessions')");
                    table.CheckConstraint("ck_calendar_item_projection", "jsonb_typeof(staff_projection_json)='object' AND staff_end_day>=staff_day AND ((member_projection_json IS NULL AND member_day IS NULL AND member_end_day IS NULL) OR (jsonb_typeof(member_projection_json)='object' AND member_day IS NOT NULL AND member_end_day IS NOT NULL AND member_end_day>=member_day))");
                    table.ForeignKey(
                        name: "FK_calendar_items_calendar_source_documents_source_collection_~",
                        columns: x => new { x.source_collection, x.source_ordinal },
                        principalSchema: "migration",
                        principalTable: "calendar_source_documents",
                        principalColumns: new[] { "collection", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_calendar_items_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "club",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_item_guests",
                schema: "club",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    player_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_item_guests", x => new { x.kind, x.event_id, x.player_id });
                    table.ForeignKey(
                        name: "FK_calendar_item_guests_calendar_items_organization_id_kind_ev~",
                        columns: x => new { x.organization_id, x.kind, x.event_id },
                        principalSchema: "club",
                        principalTable: "calendar_items",
                        principalColumns: new[] { "organization_id", "kind", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_calendar_item_guests_players_organization_id_player_id",
                        columns: x => new { x.organization_id, x.player_id },
                        principalSchema: "club",
                        principalTable: "players",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_item_scopes",
                schema: "club",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    scope_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    record_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    team_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    academy_group_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_item_scopes", x => new { x.kind, x.event_id, x.scope_kind, x.record_id });
                    table.CheckConstraint("ck_calendar_scope_kind", "(scope_kind='team' AND team_id=record_id AND team_id IS NOT NULL AND academy_group_id IS NULL) OR (scope_kind='group' AND academy_group_id=record_id AND academy_group_id IS NOT NULL AND team_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_calendar_item_scopes_academy_groups_organization_id_academy~",
                        columns: x => new { x.organization_id, x.academy_group_id },
                        principalSchema: "club",
                        principalTable: "academy_groups",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_calendar_item_scopes_calendar_items_organization_id_kind_ev~",
                        columns: x => new { x.organization_id, x.kind, x.event_id },
                        principalSchema: "club",
                        principalTable: "calendar_items",
                        principalColumns: new[] { "organization_id", "kind", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_calendar_item_scopes_teams_organization_id_team_id",
                        columns: x => new { x.organization_id, x.team_id },
                        principalSchema: "club",
                        principalTable: "teams",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_responses",
                schema: "club",
                columns: table => new
                {
                    collection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    player_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actor_identity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_ordinal = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_responses", x => new { x.collection, x.kind, x.event_id, x.player_id });
                    table.CheckConstraint("ck_calendar_response_status", "(collection='availability' AND status IN ('going','not_going','maybe')) OR (collection='event_attendance' AND status IN ('going','not_going','maybe','no_response')) OR (collection='attendance' AND status IN ('present','absent'))");
                    table.ForeignKey(
                        name: "FK_calendar_responses_calendar_items_organization_id_kind_even~",
                        columns: x => new { x.organization_id, x.kind, x.event_id },
                        principalSchema: "club",
                        principalTable: "calendar_items",
                        principalColumns: new[] { "organization_id", "kind", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_calendar_responses_calendar_source_documents_collection_sou~",
                        columns: x => new { x.collection, x.source_ordinal },
                        principalSchema: "migration",
                        principalTable: "calendar_source_documents",
                        principalColumns: new[] { "collection", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_calendar_responses_mobile_identities_actor_identity_id",
                        column: x => x.actor_identity_id,
                        principalSchema: "identity",
                        principalTable: "mobile_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_calendar_responses_players_organization_id_player_id",
                        columns: x => new { x.organization_id, x.player_id },
                        principalSchema: "club",
                        principalTable: "players",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_item_guests_organization_id_kind_event_id",
                schema: "club",
                table: "calendar_item_guests",
                columns: new[] { "organization_id", "kind", "event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_item_guests_organization_id_player_id",
                schema: "club",
                table: "calendar_item_guests",
                columns: new[] { "organization_id", "player_id" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_item_scopes_organization_id_academy_group_id",
                schema: "club",
                table: "calendar_item_scopes",
                columns: new[] { "organization_id", "academy_group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_item_scopes_organization_id_kind_event_id",
                schema: "club",
                table: "calendar_item_scopes",
                columns: new[] { "organization_id", "kind", "event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_item_scopes_organization_id_team_id",
                schema: "club",
                table: "calendar_item_scopes",
                columns: new[] { "organization_id", "team_id" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_items_organization_id_member_day_kind_id",
                schema: "club",
                table: "calendar_items",
                columns: new[] { "organization_id", "member_day", "kind", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_items_organization_id_staff_day_kind_id",
                schema: "club",
                table: "calendar_items",
                columns: new[] { "organization_id", "staff_day", "kind", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_items_source_collection_source_ordinal",
                schema: "club",
                table: "calendar_items",
                columns: new[] { "source_collection", "source_ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_responses_actor_identity_id",
                schema: "club",
                table: "calendar_responses",
                column: "actor_identity_id");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_responses_collection_source_ordinal",
                schema: "club",
                table: "calendar_responses",
                columns: new[] { "collection", "source_ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_responses_organization_id_kind_event_id",
                schema: "club",
                table: "calendar_responses",
                columns: new[] { "organization_id", "kind", "event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_responses_organization_id_player_id",
                schema: "club",
                table: "calendar_responses",
                columns: new[] { "organization_id", "player_id" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_source_documents_import_digest",
                schema: "migration",
                table: "calendar_source_documents",
                column: "import_digest");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $calendar_recovery$
                BEGIN
                  IF EXISTS(SELECT 1 FROM club.calendar_import_state)
                    OR EXISTS(SELECT 1 FROM club.calendar_items)
                    OR EXISTS(SELECT 1 FROM club.calendar_item_scopes)
                    OR EXISTS(SELECT 1 FROM club.calendar_item_guests)
                    OR EXISTS(SELECT 1 FROM club.calendar_responses)
                    OR EXISTS(SELECT 1 FROM migration.calendar_source_documents)
                    OR EXISTS(SELECT 1 FROM migration.calendar_import_issues)
                  THEN RAISE EXCEPTION 'calendar_data_requires_forward_recovery'; END IF;
                END
                $calendar_recovery$;
                """);

            migrationBuilder.DropTable(
                name: "calendar_import_issues",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "calendar_item_guests",
                schema: "club");

            migrationBuilder.DropTable(
                name: "calendar_item_scopes",
                schema: "club");

            migrationBuilder.DropTable(
                name: "calendar_responses",
                schema: "club");

            migrationBuilder.DropTable(
                name: "calendar_items",
                schema: "club");

            migrationBuilder.DropTable(
                name: "calendar_source_documents",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "calendar_import_state",
                schema: "club");
        }
    }
}
