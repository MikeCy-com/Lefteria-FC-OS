using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LegacyMobileBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default view-owner permissions intentionally expose only this
            // projection. The runtime still has no migration-schema access.
            migrationBuilder.Sql("""
                CREATE VIEW identity.imported_mobile_subjects WITH (security_barrier=true) AS
                SELECT mapping.legacy_id AS legacy_subject,mapping.mobile_identity_id AS identity_id
                FROM migration.legacy_account_mappings mapping
                JOIN identity.mobile_identities mobile ON mobile.id=mapping.mobile_identity_id
                  AND mobile.legacy_id COLLATE "C"=mapping.legacy_id
                WHERE mapping.collection='mobile_users' AND mapping.disposition='mobile'
                  AND mapping.target_id=mapping.mobile_identity_id;
                REVOKE ALL ON identity.imported_mobile_subjects FROM PUBLIC;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No account, mapping, token or audit data is deleted by recovery.
            migrationBuilder.Sql("DROP VIEW identity.imported_mobile_subjects;");

        }
    }
}
