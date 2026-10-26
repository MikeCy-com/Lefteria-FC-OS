using System.Text.Json;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.StaffRelationships;

internal static class BoundRosterArchive
{
    public static async Task<ValidatedRosterImport> ReadAsync(LefteriaDbContext database, string digest, string commit, CancellationToken token)
    {
        var state = await database.RosterImportStates.AsNoTracking().SingleOrDefaultAsync(token);
        if (state is null) throw Refused("roster_snapshot_required");
        if (state.Digest != digest) throw Refused("roster_digest_mismatch");
        if (state.SourceCommit != commit || state.SourceRepository != ValidatedRosterImport.Repository) throw Refused("source_commit_mismatch");
        var documents = await database.RosterSourceDocuments.AsNoTracking().ToArrayAsync(token);
        if (documents.Any(row => row.ImportDigest != digest)) throw Refused("roster_archive_mismatch");
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject(); writer.WriteNumber("schema_version", 1);
            writer.WriteString("source_repository", ValidatedRosterImport.Repository); writer.WriteString("source_commit", commit);
            writer.WriteStartArray("complete_collections");
            foreach (var collection in new[] { "teams", "academy_groups", "players" }) writer.WriteStringValue(collection);
            writer.WriteEndArray();
            foreach (var collection in new[] { "teams", "academy_groups", "players" })
            {
                writer.WriteStartArray(collection);
                foreach (var row in documents.Where(row => row.Collection == collection).OrderBy(row => row.SourceId, StringComparer.Ordinal))
                    writer.WriteRawValue(row.SourceJson);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        ValidatedRosterImport archived;
        try { archived = RosterImportValidator.Validate(bytes.ToArray()); }
        catch (RosterImportValidationException) { throw Refused("roster_archive_invalid"); }
        var archivedSources = archived.Source.ToDictionary(row => (row.Collection, row.SourceId));
        if (documents.Length != archived.Source.Count || documents.Any(row => !archivedSources.TryGetValue((row.Collection, row.SourceId), out var source) || source.Json != row.SourceJson) ||
            state.TeamsCount != archived.Teams.Count || state.AcademyGroupsCount != archived.Groups.Count || state.PlayersCount != archived.Players.Count || state.MembershipsCount != archived.MembershipCount)
            throw Refused("roster_archive_mismatch");
        var teams = await database.RosterTeams.AsNoTracking().ToArrayAsync(token);
        var groups = await database.RosterAcademyGroups.AsNoTracking().ToArrayAsync(token);
        var players = await database.RosterPlayers.AsNoTracking().ToArrayAsync(token);
        var memberships = await database.RosterPlayerAcademyGroups.AsNoTracking().ToArrayAsync(token);
        if (teams.Length != archived.Teams.Count || teams.Any(row => !archived.Teams.Contains(new(row.Id, row.OrganizationId, row.Name))) ||
            groups.Length != archived.Groups.Count || groups.Any(row => !archived.Groups.Contains(new(row.Id, row.OrganizationId, row.Name))) ||
            players.Length != archived.Players.Count || memberships.Length != archived.MembershipCount) throw Refused("roster_reconciliation_failed");
        var playerMap = archived.Players.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var membershipMap = memberships.ToLookup(row => row.PlayerId, StringComparer.Ordinal);
        foreach (var row in players)
        {
            if (!playerMap.TryGetValue(row.Id, out var expected) || expected.Name != row.Name || expected.OrganizationId != row.OrganizationId || expected.Number != row.Number ||
                expected.Position != row.Position || expected.TeamType != row.TeamType || expected.TeamId != row.TeamId || expected.PrimaryGroupId != row.PrimaryAcademyGroupId ||
                expected.ImageUrl != row.ImageUrl || expected.IsActive != row.IsActive || expected.IsArchived != row.IsArchived || expected.IsTransferred != row.IsTransferred ||
                expected.HasExplicitPickerFields != row.HasExplicitPickerFields ||
                !expected.Groups.SequenceEqual(membershipMap[row.Id].Where(link => link.OrganizationId == row.OrganizationId).Select(link => link.AcademyGroupId).Order(StringComparer.Ordinal)))
                throw Refused("roster_reconciliation_failed");
        }
        return archived;
    }
    private static RosterImportApplyException Refused(string code) => new(code);
}
