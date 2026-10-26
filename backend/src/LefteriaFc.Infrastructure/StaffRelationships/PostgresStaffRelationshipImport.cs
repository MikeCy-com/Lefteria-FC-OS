using System.Text.Json;
using LefteriaFc.Application.Roster;
using LefteriaFc.Application.StaffRelationships;
using LefteriaFc.Domain.Organizations;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Roster;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.StaffRelationships;

// Owner-only executable operation, never registered in API DI.
public static class PostgresStaffRelationshipImport
{
    public static async Task<StaffRelationshipApplyResult> ApplyAsync(ValidatedStaffRelationshipImport plan,
        string connectionString, string expectedDigest, string expectedDatabase, string environment, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await using var database = await OwnerImportTarget.OpenAsync(connectionString, plan.Digest, expectedDigest, expectedDatabase, environment, token);
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5495884701258)", token);
        // Hold the owner-validated base snapshot stable through commit, even if
        // another owner command does not participate in the importer lock.
        await database.Database.ExecuteSqlRawAsync("LOCK TABLE club.teams,club.academy_groups,club.players,club.player_academy_groups,club.roster_import_state,migration.roster_source_documents IN SHARE MODE", token);
        await database.Database.ExecuteSqlRawAsync("LOCK TABLE club.staff,club.staff_teams,club.staff_academy_groups,club.academy_group_coaches,migration.staff_relationship_import_state,migration.staff_source_documents,migration.contact_provenance IN SHARE ROW EXCLUSIVE MODE", token);
        var roster = await BoundRosterArchive.ReadAsync(database, plan.RosterDigest, plan.SourceCommit, token);
        var staffIds = plan.Staff.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var teams = roster.Teams.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var groups = roster.Groups.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var teamLinks = new List<StaffTeamRecord>();
        var groupLinks = new Dictionary<(string Staff, string Group), StaffAcademyGroupRecord>();
        var coaches = new List<AcademyGroupCoachRecord>();
        foreach (var person in plan.Staff)
        {
            foreach (var team in person.Teams)
            {
                if (!teams.TryGetValue(team.Id, out var target) || target.OrganizationId != OrganizationIds.Club) throw Refused("staff_team_reference");
                teamLinks.Add(new() { StaffId = person.Id, OrganizationId = target.OrganizationId, TeamId = team.Id, FromPlural = team.FromPlural, FromSingular = team.FromSingular });
            }
            foreach (var group in person.Groups)
            {
                if (!groups.TryGetValue(group.Id, out var target) || target.OrganizationId != OrganizationIds.Academy) throw Refused("staff_group_reference");
                groupLinks.Add((person.Id, group.Id), new() { StaffId = person.Id, OrganizationId = target.OrganizationId, AcademyGroupId = group.Id, FromPlural = group.FromPlural, FromSingular = group.FromSingular });
            }
        }
        var contacts = plan.Contacts.ToList();
        foreach (var source in roster.Source)
        {
            using var document = JsonDocument.Parse(source.Json); var row = document.RootElement;
            if (source.Collection == "academy_groups" && StaffRelationshipImportValidator.OptionalId(row, "coach_id") is { } coach)
            {
                if (!staffIds.Contains(coach)) throw Refused("group_coach_reference");
                coaches.Add(new() { OrganizationId = groups[source.SourceId].OrganizationId, AcademyGroupId = source.SourceId, StaffId = coach });
                if (!groupLinks.TryGetValue((coach, source.SourceId), out var link))
                    groupLinks.Add((coach, source.SourceId), link = new() { StaffId = coach, AcademyGroupId = source.SourceId, OrganizationId = groups[source.SourceId].OrganizationId });
                link.FromGroupCoach = true;
            }
            if (source.Collection == "players")
            {
                contacts.Add(StaffRelationshipImportValidator.ReadContact(row, "players", source.SourceId, "phone", "player_own", 128));
                contacts.Add(StaffRelationshipImportValidator.ReadContact(row, "players", source.SourceId, "parent_phone", "guardian", 128));
                contacts.Add(StaffRelationshipImportValidator.ReadContact(row, "players", source.SourceId, "parent_email", "guardian", 320));
                contacts.Add(StaffRelationshipImportValidator.ReadContact(row, "players", source.SourceId, "parent_name", "guardian", 200));
            }
        }
        var staffRows = plan.Staff.Select(row => new StaffRecord
        {
            Id = row.Id, Name = row.Name, JobRole = row.JobRole, TeamType = row.TeamType,
            IsActive = row.IsActive, IsArchived = row.IsArchived, HasExplicitActive = row.HasExplicitActive, HasExplicitArchived = row.HasExplicitArchived
        }).ToArray();
        var sourceRows = plan.Source.Select(row => new StaffSourceDocumentRecord { SourceId = row.SourceId, ImportDigest = plan.Digest, SourceJson = row.Json }).ToArray();
        var contactRows = contacts.Select(row => new ContactProvenanceRecord
        {
            SourceCollection = row.Collection, SourceId = row.SourceId, Field = row.Field, Kind = row.Kind, Presence = row.Presence, RawValue = row.RawValue,
            PlayerId = row.Collection == "players" ? row.SourceId : null, StaffId = row.Collection == "staff" ? row.SourceId : null,
            SourceDigest = row.Collection == "players" ? plan.RosterDigest : plan.Digest, ImportDigest = plan.Digest
        }).ToArray();
        var previous = await database.StaffRelationshipImportStates.AsNoTracking().SingleOrDefaultAsync(token);
        if (previous is not null)
        {
            if (previous.Digest != plan.Digest) throw Refused("staff_initial_snapshot_already_applied");
            if (previous.RosterDigest != plan.RosterDigest || previous.SourceCommit != plan.SourceCommit || previous.Environment != environment || previous.StaffCount != plan.Staff.Count ||
                previous.TeamAssignmentsCount != teamLinks.Count || previous.GroupAssignmentsCount != groupLinks.Count || previous.GroupCoachesCount != coaches.Count || previous.ContactsCount != contacts.Count ||
                !SameRows(await database.Staff.AsNoTracking().ToArrayAsync(token), staffRows,
                    row => (row.Id, row.Name, row.JobRole, row.TeamType, row.IsActive, row.IsArchived, row.HasExplicitActive, row.HasExplicitArchived)) ||
                !SameRows(await database.StaffTeams.AsNoTracking().ToArrayAsync(token), teamLinks,
                    row => (row.StaffId, row.OrganizationId, row.TeamId, row.FromPlural, row.FromSingular)) ||
                !SameRows(await database.StaffAcademyGroups.AsNoTracking().ToArrayAsync(token), groupLinks.Values,
                    row => (row.StaffId, row.OrganizationId, row.AcademyGroupId, row.FromPlural, row.FromSingular, row.FromGroupCoach)) ||
                !SameRows(await database.AcademyGroupCoaches.AsNoTracking().ToArrayAsync(token), coaches,
                    row => (row.OrganizationId, row.AcademyGroupId, row.StaffId)) ||
                !SameRows(await database.StaffSourceDocuments.AsNoTracking().ToArrayAsync(token), sourceRows,
                    row => (row.SourceId, row.ImportDigest, row.SourceJson)) ||
                !SameRows(await database.ContactProvenance.AsNoTracking().ToArrayAsync(token), contactRows,
                    row => (row.SourceCollection, row.SourceId, row.Field, row.Kind, row.Presence, row.RawValue, row.PlayerId, row.StaffId, row.SourceDigest, row.ImportDigest)))
                throw Refused("staff_target_reconciliation_failed");
            await transaction.CommitAsync(token);
            return Result(false, true);
        }
        if (await database.Staff.AnyAsync(token) || await database.StaffTeams.AnyAsync(token) || await database.StaffAcademyGroups.AnyAsync(token) ||
            await database.AcademyGroupCoaches.AnyAsync(token) || await database.StaffSourceDocuments.AnyAsync(token) || await database.ContactProvenance.AnyAsync(token))
            throw Refused("staff_uninitialized_target_not_empty");
        database.Staff.AddRange(staffRows);
        database.StaffTeams.AddRange(teamLinks); database.StaffAcademyGroups.AddRange(groupLinks.Values); database.AcademyGroupCoaches.AddRange(coaches);
        await database.SaveChangesAsync(token);
        var now = DateTimeOffset.UtcNow;
        database.StaffRelationshipImportStates.Add(new()
        {
            Digest = plan.Digest, RosterDigest = plan.RosterDigest, SourceCommit = plan.SourceCommit, Environment = environment,
            StaffCount = plan.Staff.Count, TeamAssignmentsCount = teamLinks.Count, GroupAssignmentsCount = groupLinks.Count, GroupCoachesCount = coaches.Count, ContactsCount = contacts.Count, AppliedAt = now
        });
        database.StaffSourceDocuments.AddRange(sourceRows);
        database.ContactProvenance.AddRange(contactRows);
        database.SecurityAuditEvents.Add(new SecurityAuditRecord { Id = Guid.NewGuid(), OccurredAt = now, Action = "staff_relationships.import", Outcome = "success", ReasonCode = "initial_snapshot", SubjectDigest = plan.Digest });
        await database.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return Result(true, false);

        StaffRelationshipApplyResult Result(bool applied, bool noop) => new(applied, noop, plan.Digest, plan.Staff.Count, teamLinks.Count, groupLinks.Count, coaches.Count, contacts.Count);
    }
    private static bool SameRows<TRow, TValue>(IReadOnlyCollection<TRow> actual, IEnumerable<TRow> expected, Func<TRow, TValue> fields)
    {
        var wanted = expected.Select(fields).ToArray();
        return actual.Count == wanted.Length && actual.Select(fields).ToHashSet().SetEquals(wanted);
    }
    private static RosterImportApplyException Refused(string code) => new(code);
}
