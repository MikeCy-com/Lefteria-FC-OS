using System.Data;
using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LefteriaFc.Infrastructure.PublicContent;

public sealed class PostgresPublicSectionsRepository(LefteriaDbContext database, TimeProvider clock) : IPublicSectionsRepository
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public async Task<PublicSectionsResult?> ReadAsync(string organizationId, CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var version = await database.Set<PublicSectionsCurrentRecord>().AsNoTracking().Select(row => (int?)row.SnapshotVersion).SingleOrDefaultAsync(token);
        if (version is null) return null;
        // Source counts, issues and provenance are owner-only columns. Read only
        // the exact readiness projection granted to the application role.
        var snapshot = await database.Set<PublicSectionsSnapshotRecord>().AsNoTracking().Where(row => row.Version == version)
            .Select(row => new { row.Version, row.DocumentsCount, row.BasePublicDigest, row.ExportedAt }).SingleOrDefaultAsync(token);
        if (snapshot is null || !await database.Set<PublicContentImportStateRecord>().AnyAsync(row => row.Digest == snapshot.BasePublicDigest, token) ||
            snapshot.DocumentsCount != await database.Set<PublicSectionsDocumentRecord>().CountAsync(row => row.SnapshotVersion == version, token)) return null;
        var rows = await database.Set<PublicSectionsDocumentRecord>().AsNoTracking().Where(row => row.SnapshotVersion == version && row.OrganizationId == organizationId).ToArrayAsync(token);
        T[] Read<T>(string kind) where T : class => rows.Where(row => row.Kind == kind).OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => JsonSerializer.Deserialize<T>(row.DataJson, Json) ?? throw new InvalidOperationException("Invalid public section projection.")).ToArray();
        var fixtures = Read<PublicSectionFixture>("fixture").OrderBy(row => row.MatchDate).ThenBy(row => row.MatchTime, StringComparer.Ordinal).ThenBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => new PublicSectionFixtureView(row, PublicFixtureClock.KickoffAt(row.MatchDate, row.MatchTime))).ToArray();
        var now = clock.GetUtcNow();
        var month = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(PublicFixtureClock.TimeZone))
            .ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var branding = Read<PublicSectionBranding>("branding").SingleOrDefault();
        if (branding is not null)
        {
            // Keep the overlapping public site/section fields consistent after a
            // club edit. Stadium and scope-specific social fields remain imported
            // because the safe club editor does not expose those capabilities.
            var edited = await database.Set<AdminClubEditRecord>().AsNoTracking()
                .Where(row => row.Kind == "club" && row.OrganizationId == organizationId && row.Id == branding.Id)
                .Select(row => row.DataJson).SingleOrDefaultAsync(token);
            if (edited is not null)
            {
                var club = JsonSerializer.Deserialize<PublicClub>(edited, Json) ?? throw new InvalidOperationException("Invalid club edit.");
                branding = branding with { Name = club.Name, GreekName = club.GreekName, LogoUrl = club.LogoUrl,
                    City = club.City, Country = club.Country, History = club.History, StadiumImageUrl = club.StadiumImageUrl,
                    LegacySocials = club.SocialLinks.Select(pair => new PublicSectionSocial(pair.Key, pair.Value)).ToArray() };
            }
        }
        var response = new PublicSectionsResult(organizationId, Read<PublicSectionPlayer>("player"), Read<PublicSectionStaff>("staff"), Read<PublicSectionGroup>("academy_group"),
            Read<PublicSectionStanding>("standing").OrderByDescending(row => row.Points).ThenByDescending(row => row.GoalDifference).ThenBy(row => row.TeamName, StringComparer.Ordinal).ToArray(),
            Read<PublicSectionVenue>("venue"), Read<PublicSectionSeason>("season").OrderByDescending(row => row.StartDate, StringComparer.Ordinal).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            Read<PublicSectionGallery>("gallery").OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(), Read<PublicSectionSponsor>("sponsor"),
            Read<PublicSectionPartner>("partner"), Read<PublicSectionEvent>("event").OrderBy(row => row.StartAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            Read<PublicSectionProduct>("product").OrderBy(row => row.Name, StringComparer.Ordinal).ToArray(), Read<PublicSectionTrials>("trials").SingleOrDefault(), fixtures,
            Read<PublicSectionTicket>("ticket"), Read<PublicSectionArchive>("archive").OrderByDescending(row => row.ArchivedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            Read<PublicSectionReview>("review").OrderByDescending(row => row.PublishedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            Read<PublicSectionAcademyStats>("academy_stats").SingleOrDefault(), Read<PublicSectionPotm>("potm").SingleOrDefault(row => row.MonthKey == month) ?? new PublicSectionPotm(month, organizationId, month, 0, []),
            branding, Read<PublicSectionModule>("module"), snapshot.ExportedAt, now, PublicFixtureClock.TimeZone);
        await transaction.CommitAsync(token);
        return response;
    }
}

public static class PublicSectionsRegistration
{
    public static IServiceCollection AddPublicSections(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services.AddScoped<IPublicSectionsRepository, PostgresPublicSectionsRepository>();
    }
}
