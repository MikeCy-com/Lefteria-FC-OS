using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Future Academy group profiles are private until a manager explicitly
/// approves public visibility. Preserve every existing published group.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115962000_AcademyGroupPrivateByDefault")]
public sealed class AcademyGroupPrivateByDefault : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            -- Changes future INSERT defaults only. Existing public listings
            -- keep their saved is_public value, with zero mass visibility edits.
            ALTER TABLE club.academy_group_profiles
              ALTER COLUMN is_public SET DEFAULT false;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            -- Restores only the old default, never rewrites user decisions.
            ALTER TABLE club.academy_group_profiles
              ALTER COLUMN is_public SET DEFAULT true;
            """);
}
