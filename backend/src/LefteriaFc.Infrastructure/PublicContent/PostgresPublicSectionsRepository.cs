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
        // Only the security-definer projection is granted to the public role.
        // Never load unpublished drafts into the public DbContext.
        var approvedJson = await database.Database.SqlQueryRaw<string>(
            "SELECT public_content.read_published_sponsorship({0})::text AS \"Value\"",
            organizationId).SingleAsync(token);
        var approved = JsonSerializer.Deserialize<PublishedSponsorshipEditorial[]>(approvedJson, Json)
            ?? Array.Empty<PublishedSponsorshipEditorial>();
        // Imported rows are overlays only. Native records are never allowed to
        // override a signed imported identity, even if a future import collides.
        var approvedImported = approved.Where(e => !e.IsNative && e.Content is not null && e.Content.Valid()).ToArray();
        var sponsorCopies = approvedImported.Where(e => e.Kind == "sponsor")
            .GroupBy(e => e.ItemId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Content, StringComparer.Ordinal);
        var partnerCopies = approvedImported.Where(e => e.Kind == "partner")
            .GroupBy(e => e.ItemId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Content, StringComparer.Ordinal);
        var importedSponsors = Read<PublicSectionSponsor>("sponsor");
        var importedPartners = Read<PublicSectionPartner>("partner");
        // No new partner/sponsor appears without a manager-approved snapshot.
        // The public projection carries neither private drafts nor unpublished names.
        var approvedNative = approved.Where(e => e.IsNative && e.Content is not null && e.Content.Valid() &&
            System.Text.RegularExpressions.Regex.IsMatch(e.ItemId, @"\Acms-[a-f0-9]{32}\z")).ToArray();
        var sponsorIds = importedSponsors.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var partnerIds = importedPartners.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var nativeSponsors = approvedNative.Where(e => e.Kind == "sponsor" && sponsorIds.Add(e.ItemId))
            .Select(e => new PublicSectionSponsor(
                e.ItemId, organizationId, e.Content.Name, e.Content.Description, e.Content.ImageUrl,
                null, e.Content.WebsiteUrl, null, null, null, null, null,
                "ΧΟΡΗΓΟΣ", "general", e.Content.DisplayOrder, []));
        var nativePartners = approvedNative.Where(e => e.Kind == "partner" && partnerIds.Add(e.ItemId))
            .Select(e => new PublicSectionPartner(
                e.ItemId, organizationId, e.ItemId, e.Content.Name, "Συνεργάτης",
                e.Content.Description, e.Content.ImageUrl, null, null, null, null,
                [], [], [], e.Content.WebsiteUrl, [], null, null, null, null, null, null,
                e.Content.DisplayOrder));
        var sponsors = importedSponsors.Select(item =>
            sponsorCopies.TryGetValue(item.Id, out var copy) ? item with {
                Name = copy.Name, Description = copy.Description, LogoUrl = copy.ImageUrl,
                Website = copy.WebsiteUrl, DisplayOrder = copy.DisplayOrder
            } : item).Concat(nativeSponsors)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var partners = importedPartners.Select(item =>
            partnerCopies.TryGetValue(item.Id, out var copy) ? item with {
                Name = copy.Name, Tagline = copy.Description, HeroImageUrl = copy.ImageUrl,
                WebsiteUrl = copy.WebsiteUrl, DisplayOrder = copy.DisplayOrder
            } : item).Concat(nativePartners)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Name, StringComparer.Ordinal).ToArray();

        var players = Read<PublicSectionPlayer>("player");
        var potm = Read<PublicSectionPotm>("potm").SingleOrDefault(row => row.MonthKey == month)
            ?? new PublicSectionPotm(month, organizationId, month, 0, []);
        if (organizationId == "lefteria_fc")
        {
            // The aggregate is read inside the same repeatable-read snapshot
            // as the public roster. Native and imported counts never combine.
            var nativeJson = await database.Database.SqlQueryRaw<string>(
                "SELECT public_sections.read_native_potm({0})::text AS \"Value\"", month).SingleAsync(token);
            potm = SelectNativePotm(nativeJson, month, players, potm);
        }

        var response = new PublicSectionsResult(organizationId, players, Read<PublicSectionStaff>("staff"), Read<PublicSectionGroup>("academy_group"),
            Read<PublicSectionStanding>("standing").OrderByDescending(row => row.Points).ThenByDescending(row => row.GoalDifference).ThenBy(row => row.TeamName, StringComparer.Ordinal).ToArray(),
            Read<PublicSectionVenue>("venue"), Read<PublicSectionSeason>("season").OrderByDescending(row => row.StartDate, StringComparer.Ordinal).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            Read<PublicSectionGallery>("gallery").OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(), sponsors,
            partners, Read<PublicSectionEvent>("event").OrderBy(row => row.StartAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            Read<PublicSectionProduct>("product").OrderBy(row => row.Name, StringComparer.Ordinal).ToArray(), Read<PublicSectionTrials>("trials").SingleOrDefault(), fixtures,
            Read<PublicSectionTicket>("ticket"), Read<PublicSectionArchive>("archive").OrderByDescending(row => row.ArchivedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            Read<PublicSectionReview>("review").OrderByDescending(row => row.PublishedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            Read<PublicSectionAcademyStats>("academy_stats").SingleOrDefault(), potm,
            branding, Read<PublicSectionModule>("module"), snapshot.ExportedAt, now, PublicFixtureClock.TimeZone);
        await transaction.CommitAsync(token);
        return response;
    }

    private sealed record NativePotmCount(string PlayerId, int Votes);
    private sealed record NativePotmTally(string State, NativePotmCount[] Counts);

    private static PublicSectionPotm SelectNativePotm(string raw, string month,
        PublicSectionPlayer[] players, PublicSectionPotm imported)
    {
        const string club = "lefteria_fc";
        // Import IDs are canonical YYYY-MM. Reserved native IDs convey source
        // without changing the historical serialized import contract/digest.
        PublicSectionPotm Empty(string state) => new($"native-{state}:{month}", club, month, 0, []);
        var unavailable = Empty("unavailable");
        try
        {
            var native = JsonSerializer.Deserialize<NativePotmTally>(raw, Json);
            if (native?.Counts is null) return unavailable;
            if (native.State == "absent") return native.Counts.Length == 0 ? imported : unavailable;
            if (native.State == "pending") return native.Counts.Length == 0 ? Empty("pending") : unavailable;
            if (native.State != "closed") return unavailable;

            var roster = players.Where(p => p.OrganizationId == club && p.TeamType == "First Team")
                .ToDictionary(p => p.Id, StringComparer.Ordinal);
            var results = new List<PublicSectionPotmCandidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var total = 0;
            foreach (var count in native.Counts)
            {
                if (count is null || string.IsNullOrWhiteSpace(count.PlayerId) || count.Votes <= 0 ||
                    !seen.Add(count.PlayerId) || !roster.TryGetValue(count.PlayerId, out var player))
                    return unavailable;
                total = checked(total + count.Votes);
                results.Add(new(player.Id, player.Name, player.Number, player.Position, player.ImageUrl, count.Votes));
            }
            return new($"native-closed:{month}", club, month, total,
                results.OrderByDescending(row => row.Votes).ThenBy(row => row.PlayerId, StringComparer.Ordinal).ToArray());
        }
        catch (Exception error) when (error is JsonException or OverflowException or ArgumentException)
        {
            return unavailable;
        }
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
