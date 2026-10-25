using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LefteriaFc.Infrastructure.Persistence;

public sealed class LefteriaDbContextFactory : IDesignTimeDbContextFactory<LefteriaDbContext>
{
    public LefteriaDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("LEFTERIA_MIGRATION_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("Set LEFTERIA_MIGRATION_CONNECTION explicitly before running migration tooling.");
        }
        return new LefteriaDbContext(new DbContextOptionsBuilder<LefteriaDbContext>().UseNpgsql(connection).Options);
    }
}
