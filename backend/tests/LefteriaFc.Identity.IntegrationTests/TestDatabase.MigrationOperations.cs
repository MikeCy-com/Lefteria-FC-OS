using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    // Exercise the real recorded migration operations without downgrading later
    // unrelated modules or altering their history, privileges and RLS policies.
    private static async Task ExecuteMigrationOperationsAsync(LefteriaDbContext context,string name,bool down)
    {
        var assembly=context.GetService<IMigrationsAssembly>();
        var definition=assembly.Migrations.Single(row=>row.Key.EndsWith("_"+name,StringComparison.Ordinal));
        var migration=assembly.CreateMigration(definition.Value,context.Database.ProviderName!);
        var commands=context.GetService<IMigrationsSqlGenerator>().Generate(down?migration.DownOperations:migration.UpOperations,
            context.Model,MigrationsSqlGenerationOptions.NoTransactions);
        var opened=context.Database.GetDbConnection().State!=System.Data.ConnectionState.Open;
        if(opened)await context.Database.OpenConnectionAsync();
        try
        {
            foreach(var command in commands)
            {
                // Generated SQL already contains provider literals (including JSON
                // braces); do not send it through composite-format interpolation.
                await using var sql=context.Database.GetDbConnection().CreateCommand();
                sql.CommandText=command.CommandText;
                sql.Transaction=context.Database.CurrentTransaction?.GetDbTransaction();
                await sql.ExecuteNonQueryAsync();
            }
        }
        finally{if(opened)await context.Database.CloseConnectionAsync();}
    }
}
