using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.PublicContent;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task PublicSectionsEndToEndAsync()
    {
        PublicSectionsPolicyChecks.Run();
        using var client = Client();
        Check.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/public/sections")).StatusCode, "Uninitialized sections must be unavailable.");
        Check.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/public/sections?organization_id=other")).StatusCode, "Unknown organization must fail.");
        var basis = PublicContentImportValidator.Validate(PublicContentFixture());
        var first = PublicSectionsImportValidator.Validate(SectionsFixture(basis, 1));
        var second = PublicSectionsImportValidator.Validate(SectionsFixture(basis, 2));
        await database.AssertEmptySectionsMigrationAsync();
        try
        {
            await database.ApplyPublicContentAsync(basis);
            var changedBase = PublicSectionsImportValidator.Validate(SectionsFixture(basis, 1, changeBase: true));
            await Refuses(() => database.ApplyPublicSectionsAsync(changedBase), "base_source_reconciliation_failed");
            var outcomes = await Task.WhenAll(database.ApplyPublicSectionsAsync(first), database.ApplyPublicSectionsAsync(first));
            Check.True(outcomes.Count(row => row.Applied) == 1 && outcomes.Count(row => row.Noop) == 1, "Concurrent same-version import must apply exactly once.");
            client.DefaultRequestHeaders.Add("Cookie", "access_token=invalid; active_organization_id=lefteria_fc_academy");
            client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-public-probe");
            async Task<JsonDocument> Read(string org)
            {
                var response = await client.GetAsync("/api/public/sections?organization_id=" + org);
                var text = await response.Content.ReadAsStringAsync();
                Check.Equal(HttpStatusCode.OK, response.StatusCode, "Public sections must be independent of credentials.");
                Check.True(response.Headers.CacheControl?.NoStore == true && !text.Contains("sentinel-private", StringComparison.Ordinal) && !text.Contains("Παπαδόπουλος", StringComparison.Ordinal), "Private source data must never leave the projection.");
                return JsonDocument.Parse(text);
            }
            using (var club = await Read("lefteria_fc"))
            {
                var players = club.RootElement.GetProperty("players");
                Check.True(players.GetArrayLength() == 1 && players[0].GetProperty("name").GetString() == "Public Player v1", "First-team scope lost its roster.");
                Check.True(players[0].GetProperty("statistics").GetProperty("goals").GetInt32() == 0 && players[0].GetProperty("statistics").GetProperty("assists").ValueKind == JsonValueKind.Null, "Known zero and unknown statistics must remain distinct through PostgreSQL and HTTP.");
            }
            using (var academy = await Read("lefteria_fc_academy"))
                Check.True(academy.RootElement.GetProperty("players").GetArrayLength() == 1 && academy.RootElement.GetProperty("players")[0].GetProperty("name").GetString() == "Ανδρέας Πα.", "Academy scope or surname privacy failed.");
            await database.AssertSectionsRuntimeBoundaryAsync();
            await database.ApplyPublicSectionsAsync(second);
            Check.True((await database.ApplyPublicSectionsAsync(first)).Noop, "Historical replay must remain safe.");
            using (var latest = await Read("lefteria_fc"))
                Check.True(latest.RootElement.GetProperty("players")[0].GetProperty("name").GetString() == "Public Player v2", "Historical replay rolled current publication backwards.");
            await database.AssertSectionsTamperRefusedAsync(second);
            await database.AssertPopulatedSectionsMigrationAsync();
        }
        finally
        {
            await database.CleanupSectionsAsync(first, second);
            await database.CleanupPublicContentAsync(basis);
        }
        await database.AssertEmptySectionsMigrationAsync();
    }

    private static async Task Refuses(Func<Task<PublicSectionsImportResult>> action, string code)
    {
        try { await action(); }
        catch (RosterImportApplyException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected public sections refusal: " + code);
    }

    private static byte[] SectionsFixture(ValidatedPublicContentImport basis, int version, bool changeBase = false)
    {
        var root = new JsonObject
        {
            ["schema_version"] = 1, ["snapshot_version"] = version,
            ["source_repository"] = ValidatedPublicContentImport.Repository, ["source_commit"] = new string('a', 40),
            ["base_public_sha256"] = basis.Digest, ["exported_at"] = "2026-10-31T00:00:00Z",
            ["complete_collections"] = JsonSerializer.SerializeToNode(PublicSectionsImportValidator.Collections)
        };
        foreach (var collection in PublicSectionsImportValidator.Collections) root[collection] = new JsonArray();
        using var baseJson = JsonDocument.Parse(basis.ManifestBytes);
        foreach (var family in new[] { "teams", "academy_groups", "fixtures", "club_profile" })
            root[family] = JsonNode.Parse(baseJson.RootElement.GetProperty(family).GetRawText());
        if (changeBase) root["teams"]![0]!["name"] = "Conflicting source";
        root["players"] = JsonSerializer.SerializeToNode(new object[]
        {
            new { id="public-player",name="Public Player v"+version,number=9,is_active=true,team_type="First Team",statistics=new { goals=0 },medical_notes="sentinel-private" },
            new { id="academy-player",name="Ανδρέας Παπαδόπουλος",number=7,is_active=true,team_type="Academy",academy_group_id="group",parent_phone="sentinel-private" }
        });
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }
}

internal sealed partial class TestDatabase
{
    public Task<PublicSectionsImportResult> ApplyPublicSectionsAsync(ValidatedPublicSectionsImport plan) =>
        PostgresPublicSectionsImport.ApplyAsync(plan, fixture.AdminConnectionString, plan.Digest, FixtureConfiguration.ExpectedDatabase, "development");

    public async Task AssertEmptySectionsMigrationAsync()
    {
        await using var context = CreateContext();
        Check.True(!await context.Set<PublicSectionsSnapshotRecord>().AnyAsync(), "Empty rollback test requires no section snapshots.");
        await using var transaction = await context.Database.BeginTransactionAsync();
        await ExecuteMigrationOperationsAsync(context, "PublicSections", true);
        await AssertBaseForeignKeyAsync(context);
        await ExecuteMigrationOperationsAsync(context, "PublicSections", false);
        await AssertBaseForeignKeyAsync(context);
        await transaction.RollbackAsync(); // Preserve grants and unrelated migration history.
    }

    private static async Task AssertBaseForeignKeyAsync(LefteriaFc.Infrastructure.Persistence.LefteriaDbContext context)
    {
        var count = await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conrelid='public_content.documents'::regclass AND confrelid='club.organizations'::regclass AND contype='f'").SingleAsync();
        Check.True(count == 1, "Public sections rollback removed an older module's organization constraint.");
    }

    public async Task AssertPopulatedSectionsMigrationAsync()
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        try { await ExecuteMigrationOperationsAsync(context, "PublicSections", true); }
        catch (PostgresException error) when (error.SqlState == "P0001" && error.MessageText == "public_sections_data_requires_forward_recovery")
        { await transaction.RollbackAsync(); return; }
        throw new InvalidOperationException("Populated sections rollback must require forward recovery.");
    }

    public async Task AssertSectionsRuntimeBoundaryAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[]
        {
            "SELECT source_json FROM migration.public_sections_source LIMIT 1",
            "SELECT manifest_bytes FROM migration.public_sections_evidence LIMIT 1",
            "SELECT * FROM migration.public_sections_issues LIMIT 1",
            "SELECT source_commit FROM public_sections.snapshots LIMIT 1",
            "UPDATE public_sections.documents SET display_order=0 WHERE false",
            "DELETE FROM public_sections.current WHERE false",
            "TRUNCATE public_sections.documents"
        })
        {
            try { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { continue; }
            throw new InvalidOperationException("Application role exceeded its public sections read boundary.");
        }
    }

    public async Task AssertSectionsTamperRefusedAsync(ValidatedPublicSectionsImport plan)
    {
        await using var context = CreateContext();
        var row = await context.Set<PublicSectionsDocumentRecord>().SingleAsync(item => item.SnapshotVersion == plan.Version && item.Kind == "player" && item.OrganizationId == "lefteria_fc");
        var original = row.DisplayOrder;
        row.DisplayOrder = 99; await context.SaveChangesAsync();
        try
        {
            try { await ApplyPublicSectionsAsync(plan); }
            catch (RosterImportApplyException error) when (error.Code == "public_sections_projection_changed") { return; }
            throw new InvalidOperationException("Modified projection replay was accepted.");
        }
        finally { row.DisplayOrder = original; await context.SaveChangesAsync(); }
    }

    public async Task CleanupSectionsAsync(params ValidatedPublicSectionsImport[] plans)
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5495884701258)");
        var digests = plans.Select(row => row.Digest).ToArray();
        var snapshots = await context.Set<PublicSectionsSnapshotRecord>().AsNoTracking().ToArrayAsync();
        Check.True(snapshots.All(row => digests.Contains(row.Digest)), "Refusing cleanup of an unowned sections snapshot.");
        var versions = snapshots.Select(row => row.Version).ToArray();
        await context.Set<PublicSectionsCurrentRecord>().Where(row => versions.Contains(row.SnapshotVersion)).ExecuteDeleteAsync();
        await context.Set<PublicSectionsIssueRecord>().Where(row => versions.Contains(row.SnapshotVersion)).ExecuteDeleteAsync();
        await context.Set<PublicSectionsDocumentRecord>().Where(row => versions.Contains(row.SnapshotVersion)).ExecuteDeleteAsync();
        await context.Set<PublicSectionsSourceRecord>().Where(row => versions.Contains(row.SnapshotVersion)).ExecuteDeleteAsync();
        await context.Set<PublicSectionsEvidenceRecord>().Where(row => digests.Contains(row.Digest)).ExecuteDeleteAsync();
        await context.Set<PublicSectionsSnapshotRecord>().Where(row => digests.Contains(row.Digest)).ExecuteDeleteAsync();
        await context.SecurityAuditEvents.Where(row => row.Action == "public_sections.import" && row.SubjectDigest != null && digests.Contains(row.SubjectDigest)).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }
}
