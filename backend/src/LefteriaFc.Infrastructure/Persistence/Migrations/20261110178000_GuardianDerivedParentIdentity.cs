using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110178000_GuardianDerivedParentIdentity")]
public sealed class GuardianDerivedParentIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE identity.mobile_identities ADD COLUMN guardian_derived_parent boolean NOT NULL DEFAULT false;
        ALTER TABLE identity.mobile_identities ADD CONSTRAINT ck_guardian_derived_parent_role
          CHECK (NOT guardian_derived_parent OR role = 'parent');
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $restore_policy$
        BEGIN
          IF EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'identity'
            AND tablename = 'mobile_identities' AND policyname = 'mobile_runtime_insert_fan_only') THEN
            ALTER POLICY mobile_runtime_insert_fan_only ON identity.mobile_identities WITH CHECK (role = 'fan');
          END IF;
        END $restore_policy$;
        ALTER TABLE identity.mobile_identities DROP CONSTRAINT ck_guardian_derived_parent_role;
        ALTER TABLE identity.mobile_identities DROP COLUMN guardian_derived_parent;
        """);
}
