using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Roster;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.PublicContent;

// Owner tool only. The normal application never registers/imports owner evidence.
public static class PostgresPublicContentImport
{
    public static async Task<PublicContentImportResult> ApplyAsync(ValidatedPublicContentImport plan,
        string connectionString, string expectedDigest, string expectedDatabase, string environment, CancellationToken token = default)
    {
        await using var database = await OwnerImportTarget.OpenAsync(connectionString, plan.Digest, expectedDigest, expectedDatabase, environment, token);
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5495884701258)", token);
        var states = database.Set<PublicContentImportStateRecord>();
        var documents = database.Set<PublicContentDocumentRecord>();
        var sources = database.Set<PublicContentSourceDocumentRecord>();
        var evidence = database.Set<PublicContentImportEvidenceRecord>();
        var previous = await states.AsNoTracking().SingleOrDefaultAsync(token);
        if (previous is not null)
        {
            if (previous.Digest != plan.Digest) throw Refused("initial_snapshot_already_applied");
            var archived = await evidence.AsNoTracking().SingleOrDefaultAsync(token);
            var publicRows = await documents.AsNoTracking().ToArrayAsync(token);
            var sourceRows = await sources.AsNoTracking().ToArrayAsync(token);
            if (previous.SourceRepository != ValidatedPublicContentImport.Repository || previous.SourceCommit != plan.SourceCommit || previous.Environment != environment ||
                previous.DocumentsCount != plan.Documents.Count || previous.SourcesCount != plan.Source.Count ||
                archived is null || archived.Digest != plan.Digest || !archived.ManifestBytes.AsSpan().SequenceEqual(plan.ManifestBytes) ||
                publicRows.Length != plan.Documents.Count || sourceRows.Length != plan.Source.Count) throw Refused("target_reconciliation_failed");
            var expected = plan.Documents.ToDictionary(row => (row.Kind, row.OrganizationId, row.Id));
            foreach (var row in publicRows)
            {
                if (!expected.TryGetValue((row.Kind, row.OrganizationId, row.Id), out var match) || row.DisplayOrder != match.DisplayOrder ||
                    !SameJson(row.DataJson, match.DataJson))
                    throw Refused("target_reconciliation_failed");
            }
            var raw = plan.Source.ToDictionary(row => (row.Collection, row.Ordinal));
            if (sourceRows.Any(row => row.ImportDigest != plan.Digest || !raw.TryGetValue((row.Collection, row.Ordinal), out var match) || row.SourceJson != match.Json))
                throw Refused("target_reconciliation_failed");
            await transaction.CommitAsync(token);
            return new(false, true, plan.Documents.Count, plan.Source.Count);
        }
        if (await documents.AnyAsync(token) || await sources.AnyAsync(token) || await evidence.AnyAsync(token)) throw Refused("uninitialized_target_not_empty");
        documents.AddRange(plan.Documents.Select(row => new PublicContentDocumentRecord
        { Kind = row.Kind, OrganizationId = row.OrganizationId, Id = row.Id, DataJson = row.DataJson, DisplayOrder = row.DisplayOrder }));
        states.Add(new PublicContentImportStateRecord
        {
            Digest = plan.Digest, SourceRepository = ValidatedPublicContentImport.Repository, SourceCommit = plan.SourceCommit,
            Environment = environment, DocumentsCount = plan.Documents.Count, SourcesCount = plan.Source.Count, AppliedAt = DateTimeOffset.UtcNow
        });
        await database.SaveChangesAsync(token);
        sources.AddRange(plan.Source.Select(row => new PublicContentSourceDocumentRecord
        { Collection = row.Collection, Ordinal = row.Ordinal, ImportDigest = plan.Digest, SourceJson = row.Json }));
        evidence.Add(new PublicContentImportEvidenceRecord { Digest = plan.Digest, ManifestBytes = plan.ManifestBytes });
        database.SecurityAuditEvents.Add(new SecurityAuditRecord
        {
            Id = Guid.NewGuid(), Action = "public_content.import", Outcome = "success", OccurredAt = DateTimeOffset.UtcNow,
            SubjectDigest = plan.Digest, ReasonCode = "initial_snapshot"
        });
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(true, false, plan.Documents.Count, plan.Source.Count);
    }
    private static RosterImportApplyException Refused(string code) => new(code);
    private static bool SameJson(string left, string right)
    {
        using var first = JsonDocument.Parse(left);
        using var second = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(first.RootElement, second.RootElement);
    }
}
