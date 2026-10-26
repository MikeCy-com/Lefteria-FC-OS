using System.Text.RegularExpressions;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.Roster;

// Shared explicit target guard. The environment argument is an operator
// assertion, not independent proof of a host's deployment classification.
internal static partial class OwnerImportTarget
{
    public const long AdvisoryLock = 5495884701258;
    public static async Task<LefteriaDbContext> OpenAsync(string connectionString, string digest,
        string expectedDigest, string expectedDatabase, string environment, CancellationToken token)
    {
        if (environment is not ("development" or "staging")) throw Refused("environment_not_allowed");
        if (string.IsNullOrWhiteSpace(expectedDatabase) || expectedDatabase is "postgres" or "template0" or "template1" ||
            ProductionName().IsMatch(expectedDatabase)) throw Refused("database_not_allowed");
        if (!string.Equals(digest, expectedDigest, StringComparison.Ordinal)) throw Refused("digest_mismatch");
        NpgsqlConnectionStringBuilder settings;
        try { settings = new(connectionString); }
        catch (ArgumentException) { throw Refused("connection_invalid"); }
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Username) || settings.Database != expectedDatabase)
            throw Refused("connection_target_mismatch");
        var database = new LefteriaDbContext(new DbContextOptionsBuilder<LefteriaDbContext>().UseNpgsql(settings.ConnectionString).Options);
        try
        {
            await database.Database.OpenConnectionAsync(token);
            await using var command = new NpgsqlCommand("SELECT current_database(), (r.rolsuper OR d.datdba=r.oid) FROM pg_database d JOIN pg_roles r ON r.rolname=current_user WHERE d.datname=current_database()",
                (NpgsqlConnection)database.Database.GetDbConnection());
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) || reader.GetString(0) != expectedDatabase || !reader.GetBoolean(1)) throw Refused("owner_target_required");
            return database;
        }
        catch { await database.DisposeAsync(); throw; }
    }
    private static RosterImportApplyException Refused(string code) => new(code);
    [GeneratedRegex("(?:^|[^a-z])(prod|production)(?:$|[^a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductionName();
}
