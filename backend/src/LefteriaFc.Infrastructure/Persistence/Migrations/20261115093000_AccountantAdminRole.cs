using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Make finance-only accountant a recognized admin role.
—Authorization remains controlled by AdminPermissions and organization grants.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115093000_AccountantAdminRole")]
public sealed class AccountantAdminRole : Migration
{
    private const string Before = """
        ALTER TABLE identity.admin_accounts DROP CONSTRAINT ck_admin_account_role;
        ALTER TABLE identity.admin_accounts ADD CONSTRAINT ck_admin_account_role
          CHECK(role IN ('super_admin','manager','editor','marketing','coach','matchday_editor'));
        """;
    private const string After = """
        ALTER TABLE identity.admin_accounts DROP CONSTRAINT ck_admin_account_role;
        ALTER TABLE identity.admin_accounts ADD CONSTRAINT ck_admin_account_role
          CHECK(role IN ('super_admin','manager','editor','marketing','coach','matchday_editor','accountant'));
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(After);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $accountant_rollback$
            BEGIN
              IF EXISTS(SELECT 1 FROM identity.admin_accounts WHERE role='accountant')
              THEN RAISE EXCEPTION 'accountant_roles_require_forward_recovery'; END IF;
            END $accountant_rollback$;
            """);
        migrationBuilder.Sql(Before);
    }
}
