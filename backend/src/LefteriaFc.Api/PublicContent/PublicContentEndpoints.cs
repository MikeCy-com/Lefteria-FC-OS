using LefteriaFc.Application.PublicContent;

namespace LefteriaFc.Api.PublicContent;

public static class PublicContentEndpoints
{
    public static IEndpointRouteBuilder MapPublicContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/public").AllowAnonymous().WithTags("Public website");
        group.MapGet("/site", async (string? organization_id, PublicContentService service, TimeProvider clock, HttpContext context, CancellationToken token) =>
            await Execute(organization_id, service, context, token, site => SiteView(site, clock.GetUtcNow())))
            .WithName("GetPublicSite").Produces<PublicSiteResult>().Produces<PublicContentError>(400).Produces<PublicContentError>(503);
        group.MapGet("/news", async (string? organization_id, string? category, int? offset, int? limit,
            PublicContentService service, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (offset is < 0 or > 20000 || limit is < 1 or > 100 || category?.Length > 200)
                return Results.Json(new PublicContentError("Μη έγκυρα φίλτρα.", "invalid_filters"), statusCode: 400);
            return await ExecuteNews(organization_id, service, context, token, news =>
            {
                var rows = news.Where(row => category is null || row.Category == category).ToArray();
                return new PublicNewsResult(rows.Skip(offset ?? 0).Take(limit ?? 20).ToArray(), rows.Length, offset ?? 0, limit ?? 20);
            });
        }).WithName("GetPublicNews").Produces<PublicNewsResult>().Produces<PublicContentError>(400).Produces<PublicContentError>(503);
        group.MapGet("/news/{id}", async (string id, string? organization_id, PublicContentService service, HttpContext context, CancellationToken token) =>
            await ExecuteNews(organization_id, service, context, token, news => news.SingleOrDefault(row => row.Id == id), notFoundWhenNull: true))
            .WithName("GetPublicNewsArticle").Produces<PublicArticle>().Produces<PublicContentError>(400).Produces<PublicContentError>(404).Produces<PublicContentError>(503);
        return endpoints;
    }
    private static async Task<IResult> ExecuteNews<T>(string? organization, PublicContentService service, HttpContext context, CancellationToken token,
        Func<IReadOnlyList<PublicArticle>, T> project, bool notFoundWhenNull = false)
    {
        context.Response.Headers.CacheControl = "no-store";
        // Absence is a global public feed, not a third organization and not a
        // CMS ownership grant. An explicit selector remains exact visibility.
        if (organization is not null && !PublicContentService.IsOrganization(organization))
            return Results.Json(new PublicContentError("Μη έγκυρος οργανισμός.", "invalid_organization"), statusCode: 400);
        try
        {
            var news = await service.GetNewsAsync(organization, token);
            if (news is null) return Unavailable();
            var result = project(news);
            return notFoundWhenNull && result is null
                ? Results.Json(new PublicContentError("Το άρθρο δεν βρέθηκε.", "not_found"), statusCode: 404) : Results.Ok(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }
    }

    private static PublicSiteResult SiteView(PublicSiteResult site, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(PublicFixtureClock.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        bool Future(PublicFixture row) => PublicFixtureClock.KickoffAt(row.MatchDate, row.MatchTime) is { } kickoff
            ? kickoff > now : row.MatchTime is null && row.MatchDate >= today;
        var fixtures = site.Fixtures.OrderBy(row => Future(row) ? 0 : 1)
            .ThenBy(row => Future(row) ? row.MatchDate.DayNumber : -row.MatchDate.DayNumber)
            .ThenBy(row => row.MatchTime, StringComparer.Ordinal).ThenBy(row => row.Id, StringComparer.Ordinal).Take(50).ToArray();
        return site with { News = site.News.Take(20).ToArray(), Fixtures = fixtures, GeneratedAt = now,
            TimeZone = PublicFixtureClock.TimeZone,
            FixtureClock = fixtures.Select(row => new PublicFixtureInstant(row.Id, PublicFixtureClock.KickoffAt(row.MatchDate, row.MatchTime))).ToArray() };
    }
    private static async Task<IResult> Execute<T>(string? organization, PublicContentService service, HttpContext context, CancellationToken token,
        Func<PublicSiteResult, T> project, bool notFoundWhenNull = false)
    {
        // Public organization selection never reads an admin cookie/principal.
        organization ??= "lefteria_fc";
        context.Response.Headers.CacheControl = "no-store";
        if (!PublicContentService.IsOrganization(organization))
            return Results.Json(new PublicContentError("Μη έγκυρος οργανισμός.", "invalid_organization"), statusCode: 400);
        try
        {
            var site = await service.GetSiteAsync(organization, token);
            if (site is null) return Unavailable();
            var result = project(site);
            return notFoundWhenNull && result is null
                ? Results.Json(new PublicContentError("Το άρθρο δεν βρέθηκε.", "not_found"), statusCode: 404) : Results.Ok(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never expose database/input/source-document details in responses.
            return Unavailable();
        }
    }
    private static IResult Unavailable() => Results.Json(new PublicContentError("Το περιεχόμενο δεν είναι προσωρινά διαθέσιμο.", "public_content_unavailable"), statusCode: 503);
}
