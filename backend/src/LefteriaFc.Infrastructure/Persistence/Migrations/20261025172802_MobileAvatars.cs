using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MobileAvatars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mobile_avatars",
                schema: "identity",
                columns: table => new
                {
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(101)", maxLength: 101, nullable: false),
                    previous_avatar_url = table.Column<string>(type: "text", nullable: true),
                    jpeg_bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_avatars", x => x.identity_id);
                    table.CheckConstraint("ck_avatar_bytes", "octet_length(jpeg_bytes) BETWEEN 4 AND 1048576 AND substring(jpeg_bytes from 1 for 2) = decode('ffd8','hex') AND substring(jpeg_bytes from octet_length(jpeg_bytes)-1 for 2) = decode('ffd9','hex')");
                    table.CheckConstraint("ck_avatar_dimensions", "width BETWEEN 1 AND 1024 AND height BETWEEN 1 AND 1024");
                    table.CheckConstraint("ck_avatar_filename", "file_name ~ '^[0-9a-f]{32}_[0-9a-f]{64}\\.jpg$'");
                    table.ForeignKey(
                        name: "FK_mobile_avatars_mobile_identities_identity_id",
                        column: x => x.identity_id,
                        principalSchema: "identity",
                        principalTable: "mobile_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_avatars_file_name",
                schema: "identity",
                table: "mobile_avatars",
                column: "file_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore only this slice's current URL. Preserve unrelated later imports.
            // New JPEG bytes still require backup/forward recovery after real use.
            migrationBuilder.Sql("""
                UPDATE identity.mobile_identities AS identity
                SET avatar_url = avatar.previous_avatar_url
                FROM identity.mobile_avatars AS avatar
                WHERE identity.id = avatar.identity_id
                  AND identity.avatar_url = '/api/uploads/avatars/' || avatar.file_name;
                """);
            migrationBuilder.DropTable(
                name: "mobile_avatars",
                schema: "identity");
        }
    }
}
