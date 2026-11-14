using System.Text.Json.Nodes;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Identity.IntegrationTests;
using LefteriaFc.Infrastructure.PublicContent;
using Npgsql;

namespace LefteriaFc.MobilePreview;

// Preview-only: replaces the workspace's fictional public snapshot with manifests built from the
// read-only live mirror, through the repo's own public content/sections import implementations.
internal static class WorkspacePublicImport
{
    public static async Task<int> RunAsync(FixtureConfiguration fixture, string directory)
    {
        var publicPlan = PublicContentImportValidator.Validate(await File.ReadAllBytesAsync(Path.Combine(directory, "public-content.json")));
        var sections = JsonNode.Parse(await File.ReadAllBytesAsync(Path.Combine(directory, "public-sections.json")))!.AsObject();
        sections["base_public_sha256"] = publicPlan.Digest;
        var sectionsPlan = PublicSectionsImportValidator.Validate(System.Text.Encoding.UTF8.GetBytes(sections.ToJsonString()));
        await RemoveFictionalSnapshotAsync(fixture, publicPlan.Digest);
        await PostgresPublicContentImport.ApplyAsync(publicPlan, fixture.AdminConnectionString, publicPlan.Digest,
            FixtureConfiguration.ExpectedDatabase, "development");
        var result = await PostgresPublicSectionsImport.ApplyAsync(sectionsPlan, fixture.AdminConnectionString, sectionsPlan.Digest,
            FixtureConfiguration.ExpectedDatabase, "development");
        Console.WriteLine($"Public content: {publicPlan.Documents.Count} documents, digest {publicPlan.Digest[..12]}.");
        Console.WriteLine($"Public sections: {sectionsPlan.Source.Count} source rows, {sectionsPlan.Issues.Count} skipped/issue rows, result {result}.");
        foreach (var group in sectionsPlan.Issues.GroupBy(issue => issue.ToString()).Take(20))
            Console.WriteLine($"  issue {group.Key} x{group.Count()}");
        return 0;
    }

    // Deletes only the workspace's own fictional 2-document snapshot (fixed fictional commit, browser-* ids).
    private static async Task RemoveFictionalSnapshotAsync(FixtureConfiguration fixture, string nextDigest)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var state = new NpgsqlCommand("SELECT digest, source_commit, documents_count FROM public_content.import_state", connection, transaction))
        await using (var reader = await state.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) return;
            if (reader.GetString(0) == nextDigest) return;
            if (reader.GetString(1) != new string('b', 40) || reader.GetInt32(2) != 2)
                throw new WorkspaceException("The current public snapshot is not the workspace's fictional fixture; refusing to replace it.");
        }
        await using (var guard = new NpgsqlCommand("""
            SELECT NOT EXISTS (SELECT 1 FROM public_content.documents WHERE id NOT LIKE 'browser-%')
               AND NOT EXISTS (SELECT 1 FROM public_content.edits WHERE id NOT LIKE 'browser-%')
            """, connection, transaction))
            if (await guard.ExecuteScalarAsync() is not true)
                throw new WorkspaceException("Unexpected public rows; refusing to replace the snapshot.");
        await using (var delete = new NpgsqlCommand("""
            DELETE FROM public_content.edits; DELETE FROM public_content.documents;
            DELETE FROM migration.public_content_source_documents; DELETE FROM migration.public_content_import_evidence;
            DELETE FROM public_content.import_state;
            """, connection, transaction)) await delete.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
