using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MobileProfileDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email",
                schema: "identity",
                table: "mobile_identities",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "favourite_player_id",
                schema: "identity",
                table: "mobile_identities",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "identity",
                table: "mobile_identities",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email",
                schema: "identity",
                table: "mobile_identities");

            migrationBuilder.DropColumn(
                name: "favourite_player_id",
                schema: "identity",
                table: "mobile_identities");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "identity",
                table: "mobile_identities");
        }
    }
}
