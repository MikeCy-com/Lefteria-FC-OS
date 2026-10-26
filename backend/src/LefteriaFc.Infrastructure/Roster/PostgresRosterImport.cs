using System.Text.RegularExpressions;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.Roster;

// Deliberately not registered in API DI. Only the separate owner-operated tool
// supplies its explicit connection; runtime configuration is never a fallback.
public static partial class PostgresRosterImport
{
    public static async Task<RosterImportApplyResult> ApplyAsync(ValidatedRosterImport plan,
        string connectionString, string expectedDigest, string expectedDatabase, string environment,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (environment is not ("development" or "staging")) throw Refused("environment_not_allowed");
        if (string.IsNullOrWhiteSpace(expectedDatabase) || expectedDatabase is "postgres" or "template0" or "template1" ||
            ProductionName().IsMatch(expectedDatabase)) throw Refused("database_not_allowed");
        if (!string.Equals(plan.Digest, expectedDigest, StringComparison.Ordinal)) throw Refused("digest_mismatch");
        NpgsqlConnectionStringBuilder settings;
        try { settings = new(connectionString); }
        catch (ArgumentException) { throw Refused("connection_invalid"); }
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Username) || settings.Database != expectedDatabase)
            throw Refused("connection_target_mismatch");
        await using var database = new LefteriaDbContext(new DbContextOptionsBuilder<LefteriaDbContext>().UseNpgsql(settings.ConnectionString).Options);
        await database.Database.OpenConnectionAsync(token);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using (var identity = new NpgsqlCommand("SELECT current_database(), (r.rolsuper OR d.datdba=r.oid) FROM pg_database d JOIN pg_roles r ON r.rolname=current_user WHERE d.datname=current_database()", connection))
        await using (var reader = await identity.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token) || reader.GetString(0) != expectedDatabase || !reader.GetBoolean(1))
                throw Refused("owner_target_required");
        }
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5495884701258)", token);
        var previous = await database.RosterImportStates.AsNoTracking().SingleOrDefaultAsync(token);
        if (previous is not null)
        {
            if (previous.Digest != plan.Digest) throw Refused("initial_snapshot_already_applied");
            if (previous.TeamsCount != await database.RosterTeams.CountAsync(token) ||
                previous.AcademyGroupsCount != await database.RosterAcademyGroups.CountAsync(token) ||
                previous.PlayersCount != await database.RosterPlayers.CountAsync(token) ||
                previous.MembershipsCount != await database.RosterPlayerAcademyGroups.CountAsync(token) ||
                await database.RosterSourceDocuments.CountAsync(token) != plan.Source.Count)
                throw Refused("target_reconciliation_failed");
            await transaction.CommitAsync(token);
            return new(false, true, plan.Digest);
        }
        if (await database.RosterTeams.AnyAsync(token) || await database.RosterAcademyGroups.AnyAsync(token) ||
            await database.RosterPlayers.AnyAsync(token) || await database.RosterPlayerAcademyGroups.AnyAsync(token) ||
            await database.RosterSourceDocuments.AnyAsync(token)) throw Refused("uninitialized_target_not_empty");

        database.RosterTeams.AddRange(plan.Teams.Select(row => new RosterTeamRecord { Id = row.Id, OrganizationId = row.OrganizationId, Name = row.Name }));
        database.RosterAcademyGroups.AddRange(plan.Groups.Select(row => new RosterAcademyGroupRecord { Id = row.Id, OrganizationId = row.OrganizationId, Name = row.Name }));
        database.RosterPlayers.AddRange(plan.Players.Select(row => new RosterPlayerRecord
        {
            Id = row.Id, OrganizationId = row.OrganizationId, Name = row.Name, Number = row.Number, Position = row.Position,
            TeamType = row.TeamType, TeamId = row.TeamId, PrimaryAcademyGroupId = row.PrimaryGroupId, ImageUrl = row.ImageUrl,
            IsActive = row.IsActive, IsArchived = row.IsArchived, IsTransferred = row.IsTransferred,
            HasExplicitPickerFields = row.HasExplicitPickerFields
        }));
        database.RosterPlayerAcademyGroups.AddRange(plan.Players.SelectMany(player => player.Groups.Select(group =>
            new RosterPlayerAcademyGroupRecord { OrganizationId = player.OrganizationId, PlayerId = player.Id, AcademyGroupId = group })));
        // Flush typed rows inside the transaction. A later archive/audit failure
        // must roll these writes back too; readiness is never committed early.
        await database.SaveChangesAsync(token);
        var now = DateTimeOffset.UtcNow;
        database.RosterImportStates.Add(new RosterImportStateRecord
        {
            Digest = plan.Digest, SourceRepository = ValidatedRosterImport.Repository, SourceCommit = plan.SourceCommit,
            Environment = environment, TeamsCount = plan.Teams.Count, AcademyGroupsCount = plan.Groups.Count,
            PlayersCount = plan.Players.Count, MembershipsCount = plan.MembershipCount, AppliedAt = now
        });
        database.RosterSourceDocuments.AddRange(plan.Source.Select(row => new RosterSourceDocumentRecord
        {
            Collection = row.Collection, SourceId = row.SourceId, ImportDigest = plan.Digest, SourceJson = row.Json
        }));
        database.SecurityAuditEvents.Add(new SecurityAuditRecord
        {
            Id = Guid.NewGuid(), OccurredAt = now, Action = "roster.import", Outcome = "success",
            ReasonCode = "initial_snapshot", SubjectDigest = plan.Digest
        });
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(true, false, plan.Digest);
    }
    private static RosterImportApplyException Refused(string code) => new(code);
    [GeneratedRegex("(?:^|[^a-z])(prod|production)(?:$|[^a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductionName();
}
