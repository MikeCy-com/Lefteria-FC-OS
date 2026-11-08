using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Infrastructure.PublicContent;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    // Synthetic published-only fixture. Private evidence remains in migration
    // sources but can never be returned by the website CMS API.
    public async Task<ValidatedPublicContentImport> ImportNewsFixtureAsync()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1,
            source_repository = "MikeCy-com/Lefteria-FC",
            source_commit = new string('c', 40),
            complete_collections = new[] { "club_profile", "news", "teams", "academy_groups", "fixtures" },
            club_profile = new[] {
                new { id = "news-fixture-club", organization_id = "lefteria_fc", name = "Fixture FC", bank_iban = "private-news-fixture" },
                new { id = "news-fixture-academy", organization_id = "lefteria_fc_academy", name = "Fixture Academy", bank_iban = "private-news-fixture" }
            },
            news = new[] {
                new { id = "news-first-team", title = "Fixture match report", excerpt = "FC report",
                    content = "Published first-team content", category = "Νέα", audience = "first_team",
                    created_at = "2026-11-01T12:00:00Z" },
                new { id = "news-academy", title = "Fixture Academy report", excerpt = "Academy report",
                    content = "Published Academy content", category = "Ακαδημία", audience = "academy",
                    created_at = "2026-11-02T12:00:00Z" },
                new { id = "news-shared", title = "Fixture club announcement", excerpt = "Both audiences",
                    content = "Published shared content", category = "Νέα", audience = "both",
                    created_at = "2026-11-03T12:00:00Z" }
            },
            teams = Array.Empty<object>(), academy_groups = Array.Empty<object>(), fixtures = Array.Empty<object>()
        });
        var plan = PublicContentImportValidator.Validate(bytes);
        await PostgresPublicContentImport.ApplyAsync(plan, fixture.AdminConnectionString,
            plan.Digest, FixtureConfiguration.ExpectedDatabase, "development");
        return plan;
    }

    public Task CleanupNewsFixtureAsync(ValidatedPublicContentImport plan) =>
        new TestDatabase(fixture, factory.OtpKey).CleanupPublicContentAsync(plan);
}
