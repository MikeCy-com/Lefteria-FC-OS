using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Roster;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    private readonly Dictionary<string, ValidatedRosterImport> rosterImports = new(StringComparer.Ordinal);
    public Task<RosterImportApplyResult> ApplyRosterAsRuntimeAsync(ValidatedRosterImport plan) =>
        PostgresRosterImport.ApplyAsync(plan, fixture.AppConnectionString, plan.Digest, FixtureConfiguration.ExpectedDatabase, "development");
    public async Task<RosterImportApplyResult> ApplyRosterAsync(ValidatedRosterImport plan)
    {
        rosterImports[plan.Digest] = plan;
        return await PostgresRosterImport.ApplyAsync(plan, fixture.AdminConnectionString, plan.Digest,
            FixtureConfiguration.ExpectedDatabase, "development");
    }
    public async Task CleanupRosterImportsAsync()
    {
        foreach (var plan in rosterImports.Values.ToArray()) await CleanupRosterImportAsync(plan);
    }
    public async Task CleanupRosterImportAsync(ValidatedRosterImport plan)
    {
        await using var database = CreateContext();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5495884701258)");
        var state = await database.RosterImportStates.AsNoTracking().SingleOrDefaultAsync();
        if (state?.Digest != plan.Digest) return;
        if (await database.RosterTeams.CountAsync() != plan.Teams.Count || await database.RosterAcademyGroups.CountAsync() != plan.Groups.Count ||
            await database.RosterPlayers.CountAsync() != plan.Players.Count || await database.RosterSourceDocuments.CountAsync() != plan.Source.Count)
            throw new InvalidOperationException("Refusing cleanup of an unexpected roster snapshot.");
        var playerIds = plan.Players.Select(row => row.Id).ToArray();
        var teamIds = plan.Teams.Select(row => row.Id).ToArray();
        var groupIds = plan.Groups.Select(row => row.Id).ToArray();
        await database.RosterPlayerAcademyGroups.Where(row => playerIds.Contains(row.PlayerId)).ExecuteDeleteAsync();
        await database.RosterPlayers.Where(row => playerIds.Contains(row.Id)).ExecuteDeleteAsync();
        await database.RosterTeams.Where(row => teamIds.Contains(row.Id)).ExecuteDeleteAsync();
        await database.RosterAcademyGroups.Where(row => groupIds.Contains(row.Id)).ExecuteDeleteAsync();
        await database.RosterSourceDocuments.Where(row => row.ImportDigest == plan.Digest).ExecuteDeleteAsync();
        await database.RosterImportStates.Where(row => row.Id == "initial" && row.Digest == plan.Digest).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        rosterImports.Remove(plan.Digest);
    }
    public async Task AssertRosterRuntimePermissionsAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[]
        {
            "SELECT * FROM club.players LIMIT 1", "SELECT source_json FROM migration.roster_source_documents LIMIT 1",
            "UPDATE club.players SET name='forbidden' WHERE false", "DELETE FROM club.players WHERE false",
            "UPDATE club.roster_import_state SET players_count=0 WHERE false"
        })
        {
            try { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege) { continue; }
            throw new InvalidOperationException("Runtime exceeded the read-only picker/archive boundary.");
        }
    }
}
