using LefteriaFc.Application.Marketing;
using LefteriaFc.Infrastructure.Marketing;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    public async Task AssertMarketingRuntimeWriterUnavailableAsync()
    {
        // A strict runtime database identity must not be able to invoke the
        // private writer until a separately reviewed function grant exists.
        var name="Blocked Lead "+Guid.NewGuid().ToString("N");
        await using var db=new LefteriaDbContext(
            new DbContextOptionsBuilder<LefteriaDbContext>()
                .UseNpgsql(fixture.AppConnectionString).Options);
        var writer=new PostgresAcademyLeadWriter(db);
        var lead=new ValidatedAcademyLead(
            name,"+35799000000",null,9,null,null,null,"website",true,false);
        var stored=await writer.WriteAsync(lead,"lefteria_fc_academy",new string('f',64),CancellationToken.None);
        Check.Equal(AcademyLeadStoreResult.Unavailable,stored,
            "Application role unexpectedly wrote a private marketing lead.");

        await using var conn=new NpgsqlConnection(fixture.AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd=new NpgsqlCommand(
            "SELECT count(*) FROM marketing.public_leads WHERE contact_name=@name",conn);
        cmd.Parameters.AddWithValue("name",name);
        Check.Equal(0L,Convert.ToInt64(await cmd.ExecuteScalarAsync()),
            "Unapproved runtime role persisted private enquiry data.");
    }
}
