using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicClubProfileEditor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edits",
                schema: "public_content",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    organization_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edits", x => new { x.kind, x.organization_id, x.id });
                    table.CheckConstraint("ck_public_club_edit", "kind = 'club' AND version > 0");
                    table.CheckConstraint("ck_public_club_edit_identity", "(jsonb_typeof(data_json) = 'object' AND data_json->>'id' = id AND data_json->>'organization_id' = organization_id) IS TRUE");
                    table.ForeignKey(
                        name: "FK_edits_documents_kind_organization_id_id",
                        columns: x => new { x.kind, x.organization_id, x.id },
                        principalSchema: "public_content",
                        principalTable: "documents",
                        principalColumns: new[] { "kind", "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.Sql("""
                DO $owner_guard$
                BEGIN
                  IF (SELECT relowner FROM pg_catalog.pg_class WHERE oid='identity.admin_accounts'::pg_catalog.regclass)
                    <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname=current_user)
                  THEN RAISE EXCEPTION 'admin_club_lock_requires_table_owner'; END IF;
                END $owner_guard$;
                CREATE FUNCTION identity.lock_admin_club_authority(token_digest text, organization_id text)
                RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp
                AS $club_authority$
                DECLARE actor uuid;
                BEGIN
                  IF token_digest IS NULL OR token_digest !~ '^[0-9a-f]{64}$'
                    OR organization_id IS NULL OR organization_id NOT IN ('lefteria_fc','lefteria_fc_academy')
                  THEN RAISE EXCEPTION 'invalid_admin_club_lock_selector' USING ERRCODE='22023'; END IF;
                  SELECT s.admin_account_id INTO actor FROM identity.admin_sessions s WHERE s.token_digest=$1;
                  IF actor IS NULL THEN RAISE EXCEPTION 'admin_club_session_lock_missing'; END IF;
                  -- One order for every caller: account, session, organization grant.
                  -- No data/credential is returned and no row is modified.
                  PERFORM a.id FROM identity.admin_accounts a WHERE a.id=actor FOR SHARE;
                  IF NOT FOUND THEN RAISE EXCEPTION 'admin_club_session_lock_missing'; END IF;
                  PERFORM s.id FROM identity.admin_sessions s WHERE s.token_digest=$1 AND s.admin_account_id=actor FOR SHARE;
                  IF NOT FOUND THEN RAISE EXCEPTION 'admin_club_session_lock_missing'; END IF;
                  PERFORM g.admin_account_id FROM identity.admin_organization_grants g
                    WHERE g.admin_account_id=actor AND g.organization_id=$2 FOR SHARE;
                  IF NOT FOUND THEN RAISE EXCEPTION 'admin_club_grant_lock_missing'; END IF;
                END
                $club_authority$;
                REVOKE ALL ON FUNCTION identity.lock_admin_club_authority(text,text) FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $club_recovery$ BEGIN
                  IF EXISTS(SELECT 1 FROM public_content.edits)
                    OR EXISTS(SELECT 1 FROM audit.security_events WHERE action='admin.club.update')
                  THEN RAISE EXCEPTION 'club_profile_edits_require_forward_recovery'; END IF;
                END $club_recovery$;
                DROP FUNCTION identity.lock_admin_club_authority(text,text);
                """);
            migrationBuilder.DropTable(
                name: "edits",
                schema: "public_content");
        }
    }
}
