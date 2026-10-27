using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    public IdentityApplicationFactory CreateLegacyBridgeFactory()=>new(fixture,true);
    public async Task RestoreLegacyBridgeViewAsync()
    {
        await fixture.VerifyServerAsync();
        await using var context=CreateContext();
        var latest=(await context.Database.GetAppliedMigrationsAsync()).LastOrDefault();
        if(latest is null||!(latest.EndsWith("_LegacyAccountImport",StringComparison.Ordinal)||latest.EndsWith("_LegacyMobileBridge",StringComparison.Ordinal)))
            throw new InvalidOperationException("Refusing view recovery outside the expected account/bridge migration boundary.");
        await context.GetService<IMigrator>().MigrateAsync("LegacyMobileBridge");
        await context.Database.OpenConnectionAsync();
        using var builder=new NpgsqlCommandBuilder();var role=builder.QuoteIdentifier(fixture.AppUsername);
        await using var grant=new NpgsqlCommand("GRANT SELECT (legacy_subject,identity_id) ON identity.imported_mobile_subjects TO "+role,
            (NpgsqlConnection)context.Database.GetDbConnection());
        await grant.ExecuteNonQueryAsync();
    }
    public async Task AssertLegacyBridgeViewRecoveryAsync(AccountImportFixture input)
    {
        await fixture.VerifyServerAsync();
        await using var context=CreateContext();var applied=(await context.Database.GetAppliedMigrationsAsync()).ToArray();
        if(applied.Length<2||!applied[^1].EndsWith("_LegacyMobileBridge",StringComparison.Ordinal)||!applied[^2].EndsWith("_LegacyAccountImport",StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing recovery probe outside the exact view-only migration boundary.");
        var before=await CountsAsync();var migrator=context.Database.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync("LegacyAccountImport");
            Check.True(await context.Database.SqlQueryRaw<bool>("SELECT to_regclass('identity.imported_mobile_subjects') IS NULL AS \"Value\"").SingleAsync(),
                "Bridge Down did not remove only its view.");
            Check.Equal(before,await CountsAsync(),"Bridge view rollback changed populated account/source/mapping rows.");
        }
        finally
        {
            await migrator.MigrateAsync("LegacyMobileBridge");
            await context.Database.OpenConnectionAsync();
            using var builder=new NpgsqlCommandBuilder();var role=builder.QuoteIdentifier(fixture.AppUsername);
            await using var grant=new NpgsqlCommand("GRANT SELECT (legacy_subject,identity_id) ON identity.imported_mobile_subjects TO "+role,
                (NpgsqlConnection)context.Database.GetDbConnection());
            await grant.ExecuteNonQueryAsync();await context.Database.CloseConnectionAsync();
        }
        Check.Equal(before,await CountsAsync(),"Bridge view reapply changed populated imported records.");
        Check.True((await ApplyAccountsAsync(input)).Noop,"View recovery did not retain exact typed/raw account state.");
        await AssertLegacySubjectViewAsync(input);
        await using var restricted=new NpgsqlConnection(fixture.AppConnectionString);await restricted.OpenAsync();
        try
        {
            await using var denied=new NpgsqlCommand("SELECT legacy_id FROM migration.legacy_account_mappings LIMIT 1",restricted);
            await denied.ExecuteScalarAsync();
        }
        catch(PostgresException error)when(error.SqlState==PostgresErrorCodes.InsufficientPrivilege){return;}
        throw new InvalidOperationException("View recovery exposed the owner-only mapping archive.");

        async Task<(int,int,int,int,int,int,int,int)> CountsAsync()=>(await context.MobileIdentities.CountAsync(),await context.AdminAccounts.CountAsync(),
            await context.AdminOrganizationGrants.CountAsync(),await context.FanRegistrationEvidence.CountAsync(),await context.LegacyAccountMappings.CountAsync(),
            await context.AccountSourceDocuments.CountAsync(),await context.AccountImportIssues.CountAsync(),await context.AccountImportStates.CountAsync());
    }
    public async Task AssertLegacySubjectViewAsync(AccountImportFixture input)
    {
        await using var connection=new NpgsqlConnection(fixture.AppConnectionString);await connection.OpenAsync();
        await using var command=new NpgsqlCommand("SELECT legacy_subject,identity_id FROM identity.imported_mobile_subjects ORDER BY legacy_subject COLLATE \"C\"",connection);
        await using var reader=await command.ExecuteReaderAsync();
        var expected=input.Plan.Mobiles.ToDictionary(row=>row.LegacyId,row=>row.Id,StringComparer.Ordinal);var count=0;
        while(await reader.ReadAsync())
        {
            Check.True(expected.TryGetValue(reader.GetString(0),out var id)&&id==reader.GetGuid(1),"Runtime legacy-subject view exposed nonmobile/quarantined/unmapped subjects.");
            count++;
        }
        Check.Equal(expected.Count,count,"Runtime subject view omitted an exact imported mobile mapping.");
    }
    public async Task CleanupLegacyBridgeBucketsAsync(string key,IEnumerable<int> clientIds)
    {
        var fingerprints=clientIds.Select(client=>Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(key),
            Encoding.UTF8.GetBytes("lefteria-auth-throttle-v1\nLegacyExchange\n192.0.2."+client))).ToLowerInvariant()).ToArray();
        await using var context=CreateContext();
        await context.AuthRateBuckets.Where(row=>fingerprints.Contains(row.Key)).ExecuteDeleteAsync();
    }
}
