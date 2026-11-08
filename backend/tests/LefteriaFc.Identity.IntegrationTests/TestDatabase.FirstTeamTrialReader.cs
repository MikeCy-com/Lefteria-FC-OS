using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.Trials;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    public async Task AssertFirstTeamTrialRuntimeReaderClosedAsync()
    {
        // Real app-role connection: only the audited fixed closed projection
        // should succeed. No raw settings SELECT, admin identity, or trial data.
        await using var context = new LefteriaDbContext(
            new DbContextOptionsBuilder<LefteriaDbContext>()
                .UseNpgsql(fixture.AppConnectionString).Options);
        var reader = new PostgresFirstTeamTrialDisplayReader(context);
        var settings = await reader.ReadAsync(CancellationToken.None);
        Check.True(settings is not null, "App role could not read approved trials projection.");
        Check.True(!settings.Open, "Public trials opened without submission approval.");
        Check.Equal("Ετοιμος να Ξεκινησεις το Ταξιδι σου;", settings.Headline,
            "First-team trial read altered its source headline.");
        Check.True(settings.ClosedMessage.Contains("προσωρινά κλειστές", StringComparison.Ordinal),
            "First-team trial closed message was lost.");
    }
}
