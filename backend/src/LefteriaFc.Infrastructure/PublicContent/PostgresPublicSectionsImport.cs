using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.Roster;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.PublicContent;

// Owner-only versioned capability. Every snapshot's source/projection/evidence
// stays immutable. Replaying an older snapshot never rolls current visibility back.
public static class PostgresPublicSectionsImport
{
    public static async Task<PublicSectionsImportResult> ApplyAsync(ValidatedPublicSectionsImport plan, string connectionString,
        string expectedDigest, string expectedDatabase, string environment, CancellationToken token = default)
    {
        await using var database = await OwnerImportTarget.OpenAsync(connectionString, plan.Digest, expectedDigest, expectedDatabase, environment, token);
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5495884701258)", token);
        var basis = await database.Set<PublicContentImportStateRecord>().AsNoTracking().SingleOrDefaultAsync(token);
        if (basis is null || basis.Digest != plan.BasePublicDigest || basis.DocumentsCount != await database.Set<PublicContentDocumentRecord>().CountAsync(token)) throw Refused("base_public_snapshot_mismatch");
        // These repeated source families must still be the same immutable base.
        // New fields/records require an explicit forward capability contract;
        // copying an unrelated live snapshot cannot relabel the original base.
        var repeated = new[] { "teams", "academy_groups", "fixtures", "club_profile" };
        var baseSource = await database.Set<PublicContentSourceDocumentRecord>().AsNoTracking().Where(row => repeated.Contains(row.Collection)).ToArrayAsync(token);
        var nextSource = plan.Source.Where(row => repeated.Contains(row.Collection)).ToDictionary(row => (row.Collection, row.Ordinal));
        if (baseSource.Length != nextSource.Count || baseSource.Any(row => row.ImportDigest != basis.Digest ||
            !nextSource.TryGetValue((row.Collection, row.Ordinal), out var source) || !SameJson(row.SourceJson, source.Json))) throw Refused("base_source_reconciliation_failed");
        var snapshots = database.Set<PublicSectionsSnapshotRecord>(); var current = database.Set<PublicSectionsCurrentRecord>();
        var previous = await snapshots.AsNoTracking().SingleOrDefaultAsync(row => row.Version == plan.Version, token);
        var pointer = await current.SingleOrDefaultAsync(token);
        if (previous is not null)
        {
            if (pointer is null || pointer.SnapshotVersion < plan.Version) throw Refused("public_sections_current_invalid");
            await ReconcileAsync(database, plan, previous, environment, token);
            await transaction.CommitAsync(token);
            return new(false, true, plan.Version, plan.Documents.Count, plan.Source.Count, plan.Issues.Count);
        }
        var maximum = await snapshots.Select(row => (int?)row.Version).MaxAsync(token) ?? 0;
        if (plan.Version != maximum + 1 || (maximum == 0) != (pointer is null)) throw Refused("public_sections_version_sequence");
        if (await database.Set<PublicSectionsDocumentRecord>().AnyAsync(row => row.SnapshotVersion == plan.Version, token) ||
            await database.Set<PublicSectionsSourceRecord>().AnyAsync(row => row.SnapshotVersion == plan.Version, token)) throw Refused("uninitialized_sections_not_empty");
        snapshots.Add(new PublicSectionsSnapshotRecord
        {
            Version = plan.Version, Digest = plan.Digest, BasePublicDigest = plan.BasePublicDigest, SourceRepository = ValidatedPublicContentImport.Repository,
            SourceCommit = plan.SourceCommit, Environment = environment, ExportedAt = plan.ExportedAt, AppliedAt = DateTimeOffset.UtcNow,
            DocumentsCount = plan.Documents.Count, SourcesCount = plan.Source.Count, IssuesCount = plan.Issues.Count
        });
        await database.SaveChangesAsync(token);
        database.Set<PublicSectionsDocumentRecord>().AddRange(plan.Documents.Select(row => new PublicSectionsDocumentRecord
        { SnapshotVersion = plan.Version, Kind = row.Kind, OrganizationId = row.OrganizationId, Id = row.Id, DataJson = row.Json, DisplayOrder = row.DisplayOrder }));
        database.Set<PublicSectionsSourceRecord>().AddRange(plan.Source.Select(row => new PublicSectionsSourceRecord
        { SnapshotVersion = plan.Version, Collection = row.Collection, Ordinal = row.Ordinal, SourceJson = row.Json }));
        database.Set<PublicSectionsEvidenceRecord>().Add(new PublicSectionsEvidenceRecord { Digest = plan.Digest, ManifestBytes = plan.ManifestBytes });
        await database.SaveChangesAsync(token);
        database.Set<PublicSectionsIssueRecord>().AddRange(plan.Issues.Select(row => new PublicSectionsIssueRecord
        { SnapshotVersion = plan.Version, Collection = row.Collection, Ordinal = row.Ordinal, Code = row.Code }));
        if (pointer is null) current.Add(new PublicSectionsCurrentRecord { SnapshotVersion = plan.Version });
        else pointer.SnapshotVersion = plan.Version;
        database.SecurityAuditEvents.Add(new SecurityAuditRecord
        { Id = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, Action = "public_sections.import", Outcome = "success", SubjectDigest = plan.Digest, ReasonCode = "versioned_snapshot" });
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(true, false, plan.Version, plan.Documents.Count, plan.Source.Count, plan.Issues.Count);
    }

    private static async Task ReconcileAsync(LefteriaDbContext database, ValidatedPublicSectionsImport plan, PublicSectionsSnapshotRecord previous,
        string environment, CancellationToken token)
    {
        if (previous.Digest != plan.Digest || previous.BasePublicDigest != plan.BasePublicDigest || previous.SourceRepository != ValidatedPublicContentImport.Repository ||
            previous.SourceCommit != plan.SourceCommit || previous.Environment != environment || previous.ExportedAt != plan.ExportedAt || previous.DocumentsCount != plan.Documents.Count ||
            previous.SourcesCount != plan.Source.Count || previous.IssuesCount != plan.Issues.Count) throw Refused("public_sections_reconciliation_failed");
        var archived = await database.Set<PublicSectionsEvidenceRecord>().AsNoTracking().SingleOrDefaultAsync(row => row.Digest == plan.Digest, token);
        if (archived is null || !archived.ManifestBytes.AsSpan().SequenceEqual(plan.ManifestBytes)) throw Refused("public_sections_evidence_changed");
        var documents = await database.Set<PublicSectionsDocumentRecord>().AsNoTracking().Where(row => row.SnapshotVersion == plan.Version).ToArrayAsync(token);
        var expected = plan.Documents.ToDictionary(row => (row.Kind, row.OrganizationId, row.Id));
        if (documents.Length != expected.Count || documents.Any(row => !expected.TryGetValue((row.Kind, row.OrganizationId, row.Id), out var match) ||
            row.DisplayOrder != match.DisplayOrder || !SameJson(row.DataJson, match.Json))) throw Refused("public_sections_projection_changed");
        var sources = await database.Set<PublicSectionsSourceRecord>().AsNoTracking().Where(row => row.SnapshotVersion == plan.Version).ToArrayAsync(token);
        var raw = plan.Source.ToDictionary(row => (row.Collection, row.Ordinal));
        if (sources.Length != raw.Count || sources.Any(row => !raw.TryGetValue((row.Collection, row.Ordinal), out var match) || row.SourceJson != match.Json)) throw Refused("public_sections_source_changed");
        var issues = await database.Set<PublicSectionsIssueRecord>().AsNoTracking().Where(row => row.SnapshotVersion == plan.Version).ToArrayAsync(token);
        if (!issues.Select(row => (row.Collection, row.Ordinal, row.Code)).ToHashSet().SetEquals(plan.Issues.Select(row => (row.Collection, row.Ordinal, row.Code)))) throw Refused("public_sections_issues_changed");
    }
    private static bool SameJson(string left, string right)
    {
        using var first = JsonDocument.Parse(left); using var second = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(first.RootElement, second.RootElement);
    }
    private static RosterImportApplyException Refused(string code) => new(code);
}
