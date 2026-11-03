using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Identity.IntegrationTests;
using LefteriaFc.Infrastructure.PublicContent;
using Npgsql;

namespace LefteriaFc.MobilePreview;

// This tool is excluded from owner/API images. Only fresh fictional public
// records may accompany the already guarded disposable administrator fixture.
internal sealed class WebPlatformPreviewClubFixture(FixtureConfiguration fixture)
{
    public const string FcName = "Fictional preview FC";
    public const string AcademyName = "Fictional preview Academy";
    private ValidatedPublicContentImport? plan;

    public async Task SeedAsync()
    {
        await using (var connection = await OwnerAsync())
        await using (var empty = new NpgsqlCommand("""
            SELECT NOT EXISTS(SELECT 1 FROM public_content.import_state)
              AND NOT EXISTS(SELECT 1 FROM public_content.documents)
              AND NOT EXISTS(SELECT 1 FROM public_content.edits)
              AND NOT EXISTS(SELECT 1 FROM migration.public_content_source_documents)
              AND NOT EXISTS(SELECT 1 FROM migration.public_content_import_evidence)
            """, connection))
            if (await empty.ExecuteScalarAsync() is not true)
                throw new InvalidOperationException("The browser fixture requires an empty public snapshot.");
        var suffix = Guid.NewGuid().ToString("N");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1, source_repository = ValidatedPublicContentImport.Repository, source_commit = new string('b', 40),
            complete_collections = new[] { "club_profile", "news", "teams", "academy_groups", "fixtures" },
            club_profile = new[]
            {
                new { id = "browser-fc-" + suffix, organization_id = "lefteria_fc", name = FcName,
                    email = "fc@example.invalid", history = "Fictional public history for browser verification." },
                new { id = "browser-academy-" + suffix, organization_id = "lefteria_fc_academy", name = AcademyName,
                    email = "academy@example.invalid", history = "Fictional Academy history for browser verification." }
            },
            news = Array.Empty<object>(), teams = Array.Empty<object>(), academy_groups = Array.Empty<object>(), fixtures = Array.Empty<object>()
        });
        plan = PublicContentImportValidator.Validate(bytes);
        await PostgresPublicContentImport.ApplyAsync(plan, fixture.AdminConnectionString, plan.Digest,
            FixtureConfiguration.ExpectedDatabase, "development");
    }

    public async Task CleanupAsync()
    {
        if (plan is null) return;
        await using var connection = await OwnerAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var locks = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(5495884701258);
            LOCK TABLE public_content.import_state,public_content.documents,public_content.edits,
              migration.public_content_source_documents,migration.public_content_import_evidence IN EXCLUSIVE MODE;
            """, connection, transaction)) await locks.ExecuteNonQueryAsync();
        await using (var state = new NpgsqlCommand("SELECT digest FROM public_content.import_state", connection, transaction))
        {
            var digest = await state.ExecuteScalarAsync();
            if (digest is null)
            {
                // A failed atomic import can leave no rows. Never treat an
                // unrelated orphan snapshot as this run's disposable content.
                await using var empty = new NpgsqlCommand("""
                    SELECT NOT EXISTS(SELECT 1 FROM public_content.documents)
                      AND NOT EXISTS(SELECT 1 FROM public_content.edits)
                      AND NOT EXISTS(SELECT 1 FROM migration.public_content_source_documents)
                      AND NOT EXISTS(SELECT 1 FROM migration.public_content_import_evidence)
                    """, connection, transaction);
                if (await empty.ExecuteScalarAsync() is not true) throw new InvalidOperationException("Refusing uninitialized public fixture cleanup.");
                return;
            }
            if (digest is not string value || value != plan.Digest)
                throw new InvalidOperationException("Refusing cleanup of an unowned public snapshot.");
        }
        var expected = plan.Documents.ToDictionary(row => (row.Kind, row.OrganizationId, row.Id));
        await using (var rows = new NpgsqlCommand("SELECT kind,organization_id,id,data_json::text FROM public_content.documents", connection, transaction))
        await using (var reader = await rows.ExecuteReaderAsync())
        {
            var count = 0;
            while (await reader.ReadAsync())
            {
                if (!expected.TryGetValue((reader.GetString(0), reader.GetString(1), reader.GetString(2)), out var owned) ||
                    !SameJson(reader.GetString(3), owned.DataJson)) throw new InvalidOperationException("Refusing unexpected public fixture rows.");
                ++count;
            }
            if (count != expected.Count) throw new InvalidOperationException("The owned public fixture is incomplete.");
        }
        var ids = plan.Documents.Select(row => row.Id).ToArray();
        var sources = plan.Source.ToDictionary(row => (row.Collection, row.Ordinal));
        await using (var rows = new NpgsqlCommand("SELECT collection,ordinal,source_json FROM migration.public_content_source_documents", connection, transaction))
        await using (var reader = await rows.ExecuteReaderAsync())
        {
            var count = 0;
            while (await reader.ReadAsync())
            {
                if (!sources.TryGetValue((reader.GetString(0), reader.GetInt32(1)), out var owned) || reader.GetString(2) != owned.Json)
                    throw new InvalidOperationException("Refusing unexpected source fixture evidence.");
                ++count;
            }
            if (count != sources.Count) throw new InvalidOperationException("The owned source fixture is incomplete.");
        }
        await using (var guard = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM public_content.import_state WHERE digest=@digest
                AND source_repository=@repository AND source_commit=@commit AND environment='development'
                AND documents_count=2 AND sources_count=2)
              AND (SELECT count(*) FROM migration.public_content_source_documents)=2
              AND NOT EXISTS(SELECT 1 FROM migration.public_content_source_documents WHERE import_digest<>@digest)
              AND (SELECT count(*) FROM migration.public_content_import_evidence)=1
              AND EXISTS(SELECT 1 FROM migration.public_content_import_evidence WHERE digest=@digest AND manifest_bytes=@manifest)
              AND NOT EXISTS(SELECT 1 FROM public_content.edits WHERE kind<>'club' OR NOT(id=ANY(@ids)))
            """, connection, transaction))
        {
            guard.Parameters.AddWithValue("digest", plan.Digest); guard.Parameters.AddWithValue("manifest", plan.ManifestBytes);
            guard.Parameters.AddWithValue("repository", ValidatedPublicContentImport.Repository); guard.Parameters.AddWithValue("commit", plan.SourceCommit);
            guard.Parameters.AddWithValue("ids", ids);
            if (await guard.ExecuteScalarAsync() is not true) throw new InvalidOperationException("Refusing unexpected public fixture evidence or edits.");
        }
        await using var cleanup = new NpgsqlCommand("""
            DELETE FROM public_content.edits WHERE kind='club' AND id=ANY(@ids);
            DELETE FROM public_content.documents WHERE kind='club' AND id=ANY(@ids);
            DELETE FROM migration.public_content_source_documents WHERE import_digest=@digest;
            DELETE FROM migration.public_content_import_evidence WHERE digest=@digest;
            DELETE FROM public_content.import_state WHERE digest=@digest;
            DELETE FROM audit.security_events WHERE action='public_content.import' AND subject_digest=@digest;
            """, connection, transaction);
        cleanup.Parameters.AddWithValue("digest", plan.Digest); cleanup.Parameters.AddWithValue("ids", ids);
        await cleanup.ExecuteNonQueryAsync(); await transaction.CommitAsync(); plan = null;
    }

    private async Task<NpgsqlConnection> OwnerAsync()
    {
        FixtureConfiguration.ValidateConnection(fixture.AdminConnectionString, fixture.AdminUsername);
        var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        try
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT current_database()=@database AND current_user=@owner
                  AND host(inet_server_addr())='127.0.0.1' AND inet_server_port()=@port
                """, connection);
            command.Parameters.AddWithValue("database", FixtureConfiguration.ExpectedDatabase);
            command.Parameters.AddWithValue("owner", fixture.AdminUsername); command.Parameters.AddWithValue("port", FixtureConfiguration.ExpectedPort);
            if (await command.ExecuteScalarAsync() is not true) throw new InvalidOperationException("The browser fixture server identity differs.");
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static bool SameJson(string left, string right)
    {
        using var first = JsonDocument.Parse(left); using var second = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(first.RootElement, second.RootElement);
    }
}
