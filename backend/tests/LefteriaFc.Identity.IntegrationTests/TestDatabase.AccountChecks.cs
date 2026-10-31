using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using LefteriaFc.Tools.RosterImport;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    public async Task AssertAccountCliAsync(AccountImportFixture input,bool applied)
    {
        var path=Path.Combine(Path.GetTempPath(),"lefteria-account-cli-"+Guid.NewGuid().ToString("N")+".json");
        var priorConnection=Environment.GetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION");
        var stdout=Console.Out;var stderr=Console.Error;
        try
        {
            await File.WriteAllBytesAsync(path,input.Manifest.CopySourceBytes());
            Environment.SetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION","deliberately invalid owner connection");
            await RunAsync(["--kind","accounts","--input",path],0,"manifest_valid");
            await RunAsync(["--kind","accounts","--input",path,"--apply","--review-target"],2,"invalid_review_mode");
            Environment.SetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION",fixture.AdminConnectionString);
            var confirmed=new[]{"--kind","accounts","--input",path,"--expected-sha256",input.Manifest.Digest,
                "--expected-database",FixtureConfiguration.ExpectedDatabase,"--environment","development"};
            await RunAsync([..confirmed,"--review-target"],0,"reviewed_with_quarantine");
            if(applied)await RunAsync([..confirmed,"--apply"],0,"same_snapshot_noop_with_quarantine");
        }
        finally
        {
            Console.SetOut(stdout);Console.SetError(stderr);
            Environment.SetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION",priorConnection);
            File.Delete(path);
        }
        async Task RunAsync(string[] arguments,int expectedExit,string expectedStatus)
        {
            using var output=new StringWriter();using var error=new StringWriter();
            Console.SetOut(output);Console.SetError(error);
            int exit;
            try{exit=await RosterImportProgram.Main(arguments);}
            finally{Console.SetOut(stdout);Console.SetError(stderr);}
            var report=output.ToString()+error.ToString();
            Check.Equal(expectedExit,exit,"Account CLI returned an unexpected exit code.");
            Check.True(report.Contains(expectedStatus,StringComparison.Ordinal),"Account CLI did not report the expected fixed status.");
            Check.True(!report.Contains(input.PlayerPhone.Digits,StringComparison.Ordinal)&&!report.Contains("$2b$",StringComparison.Ordinal)&&
                !report.Contains("private-principal-marker",StringComparison.Ordinal)&&!report.Contains(fixture.AdminConnectionString,StringComparison.Ordinal),
                "Account CLI report exposed source or owner secrets.");
        }
    }
    public async Task AssertAccountRollbackRefusedAsync()
    {
        await using var context=CreateContext();
        await using var transaction=await context.Database.BeginTransactionAsync();
        try{await ExecuteMigrationOperationsAsync(context,"LegacyAccountImport",true);}
        catch(PostgresException error)when(error.SqlState==PostgresErrorCodes.RaiseException&&error.MessageText=="account_data_requires_forward_recovery")
        {
            await transaction.RollbackAsync();
            Check.True(await context.AccountImportStates.AnyAsync()&&await context.MobileIdentities.AnyAsync()&&await context.AccountSourceDocuments.AnyAsync(),
                "Refused account rollback still removed imported data.");
            return;
        }
        throw new InvalidOperationException("Populated account migration allowed destructive rollback.");
    }
}
