using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task OperationalReadinessProofAsync()
    {
        // CI exercises the actual app role, not a privileged migration-owner
        // session. It is intentionally forbidden from reading history rows.
        await using var context=new LefteriaDbContext(
            new DbContextOptionsBuilder<LefteriaDbContext>()
                .UseNpgsql(database.AppConnectionStringForTest()).Options);
        var migrations=context.Database.GetMigrations().ToArray();
        Check.True(migrations.Length>10,"Compiled migration history is missing.");
        await using var connection=new NpgsqlConnection(database.AppConnectionStringForTest());
        await connection.OpenAsync();

        await using(var proof=new NpgsqlCommand("""
            SELECT identity.runtime_schema_ready(@last,@count),
              has_function_privilege(current_user,
                'identity.runtime_schema_ready(text,integer)','EXECUTE'),
              has_table_privilege(current_user,
                'public."__EFMigrationsHistory"','SELECT')
            """,connection))
        {
            proof.Parameters.AddWithValue("last",migrations[^1]);
            proof.Parameters.AddWithValue("count",migrations.Length);
            await using var rows=await proof.ExecuteReaderAsync();
            Check.True(await rows.ReadAsync(),"Readiness proof produced no response.");
            Check.True(rows.GetBoolean(0),"Latest compiled EF schema was not installed.");
            Check.True(rows.GetBoolean(1),"Mobile runtime lacks the narrow proof privilege.");
            Check.True(!rows.GetBoolean(2),"Runtime can read private EF migration history.");
        }

        await using(var invalid=new NpgsqlCommand("""
            SELECT identity.runtime_schema_ready(@last,@count),
              identity.runtime_schema_ready(@real,@too_many)
            """,connection))
        {
            invalid.Parameters.AddWithValue("last","20991231235959_NotInstalled");
            invalid.Parameters.AddWithValue("real",migrations[^1]);
            invalid.Parameters.AddWithValue("count",migrations.Length);
            invalid.Parameters.AddWithValue("too_many",migrations.Length+1);
            await using var rows=await invalid.ExecuteReaderAsync();
            Check.True(await rows.ReadAsync(),"Invalid schema proof produced no response.");
            Check.True(!rows.GetBoolean(0),"Nonexistent future migration was accepted.");
            Check.True(!rows.GetBoolean(1),"Incomplete migration count was accepted.");
        }

        using var client=Client();
        using var response=await client.GetAsync("/api/health/ready");
        await ExpectAsync(response,200);
        Check.True(response.Headers.CacheControl?.NoStore==true,
            "Health readiness should not be cached across deployments.");
    }
}
