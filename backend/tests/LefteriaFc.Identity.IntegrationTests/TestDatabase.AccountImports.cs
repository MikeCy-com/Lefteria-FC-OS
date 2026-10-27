using LefteriaFc.Application.AccountImport;
using LefteriaFc.Infrastructure.AccountImport;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    private AccountImportFixture? accountFixture;
    public Task<AccountImportOutcome> ApplyAccountsAsync(AccountImportFixture input,bool reviewOnly=false,bool runtime=false,
        string? expectedDigest=null,ValidatedAccountImport? manifest=null)
    {
        accountFixture=input;var snapshot=manifest??input.Manifest;
        return PostgresAccountImport.ApplyAsync(snapshot,runtime?fixture.AppConnectionString:fixture.AdminConnectionString,
            expectedDigest??snapshot.Digest,FixtureConfiguration.ExpectedDatabase,"development",reviewOnly);
    }
    public async Task CleanupAccountImportsAsync()
    {
        var input=accountFixture;if(input is null)return;
        await using var context=CreateContext();await using var transaction=await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5495884701258)");
        var state=await context.AccountImportStates.AsNoTracking().SingleOrDefaultAsync();
        if(state is null){accountFixture=null;return;}
        if(state.Digest!=input.Manifest.Digest)throw new InvalidOperationException("Refusing cleanup of an unexpected account snapshot.");
        var phones=input.Phones;var mobileIds=input.Plan.Mobiles.Select(row=>row.Id).ToArray();var adminIds=input.Plan.Admins.Select(row=>row.Id).ToArray();
        var evidenceIds=input.Plan.FanEvidence.Select(row=>row.Id).ToArray();
        var mobiles=await context.MobileIdentities.AsNoTracking().Where(row=>phones.Contains(row.NormalizedPhone)).ToArrayAsync();
        if(mobiles.Any(row=>!mobileIds.Contains(row.Id)&&(row.Role!="fan"||row.LegacyId is not null))||
            mobiles.Count(row=>mobileIds.Contains(row.Id))!=mobileIds.Length||
            mobiles.Any(row=>mobileIds.Contains(row.Id)&&(row.LegacyId is null||!row.LegacyId.StartsWith(input.Prefix,StringComparison.Ordinal)))||
            await context.AccountSourceDocuments.AnyAsync(row=>row.ImportDigest!=input.Manifest.Digest)||
            await context.CanonicalContactBindings.AnyAsync(row=>row.ImportDigest!=input.Manifest.Digest))
            throw new InvalidOperationException("Refusing account cleanup outside exact owned IDs and fictional phones.");
        var ownedIds=mobiles.Select(row=>row.Id).ToArray();
        await context.OtpChallenges.Where(row=>phones.Contains(row.NormalizedPhone)).ExecuteDeleteAsync();
        await context.MobileRecordLinks.Where(row=>ownedIds.Contains(row.IdentityId)).ExecuteDeleteAsync();
        await context.AdminStaffLinks.Where(row=>adminIds.Contains(row.AdminAccountId)).ExecuteDeleteAsync();
        await context.ImportedAdminGrants.Where(row=>adminIds.Contains(row.AdminAccountId)).ExecuteDeleteAsync();
        await context.AdminAccounts.Where(row=>adminIds.Contains(row.Id)).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.ActiveOrganizationId,(string?)null));
        await context.AdminOrganizationGrants.Where(row=>adminIds.Contains(row.AdminAccountId)).ExecuteDeleteAsync();
        await context.LegacyAccountMappings.Where(row=>context.AccountSourceDocuments.Any(source=>source.ImportDigest==input.Manifest.Digest&&source.Collection==row.Collection&&source.Ordinal==row.Ordinal)).ExecuteDeleteAsync();
        await context.AccountImportIssues.Where(row=>context.AccountSourceDocuments.Any(source=>source.ImportDigest==input.Manifest.Digest&&source.Collection==row.Collection&&source.Ordinal==row.Ordinal)).ExecuteDeleteAsync();
        await context.AccountSourceDocuments.Where(row=>row.ImportDigest==input.Manifest.Digest).ExecuteDeleteAsync();
        await context.CanonicalContactBindings.Where(row=>row.ImportDigest==input.Manifest.Digest).ExecuteDeleteAsync();
        await context.MobileIdentities.Where(row=>ownedIds.Contains(row.Id)).ExecuteDeleteAsync();
        await context.FanRegistrationEvidence.Where(row=>evidenceIds.Contains(row.Id)).ExecuteDeleteAsync();
        await context.AdminAccounts.Where(row=>adminIds.Contains(row.Id)).ExecuteDeleteAsync();
        await context.AccountImportStates.Where(row=>row.Digest==input.Manifest.Digest).ExecuteDeleteAsync();
        await transaction.CommitAsync();accountFixture=null;
    }
    public async Task AssertAccountRuntimePermissionsAsync()
    {
        await using var connection=new NpgsqlConnection(fixture.AppConnectionString);await connection.OpenAsync();
        foreach(var sql in new[]{
            "SELECT password_hash FROM identity.admin_accounts LIMIT 1",
            "SELECT source_json FROM migration.account_source_documents LIMIT 1",
            "DELETE FROM migration.legacy_account_mappings WHERE false",
            "UPDATE identity.fan_registration_evidence SET is_active=true WHERE false",
            "DELETE FROM identity.canonical_contact_bindings WHERE false",
            "UPDATE identity.mobile_record_links SET is_primary=false WHERE false"})
        {
            try{await using var command=new NpgsqlCommand(sql,connection);await command.ExecuteNonQueryAsync();}
            catch(PostgresException error)when(error.SqlState==PostgresErrorCodes.InsufficientPrivilege){continue;}
            throw new InvalidOperationException("Runtime exceeded the imported account/contact/evidence boundary.");
        }
    }
}
