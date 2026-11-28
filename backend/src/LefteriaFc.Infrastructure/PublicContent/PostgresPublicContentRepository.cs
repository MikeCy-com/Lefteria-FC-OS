using System.Data;
using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Domain.PublicContent;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;

namespace LefteriaFc.Infrastructure.PublicContent;

public sealed class PostgresPublicContentRepository(LefteriaDbContext database) : IPublicContentRepository
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public async Task<PublicSiteResult?> ReadAsync(string organizationId, CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await database.Set<PublicContentImportStateRecord>().AsNoTracking().SingleOrDefaultAsync(token);
        if (state is null || state.DocumentsCount != await database.Set<PublicContentDocumentRecord>().CountAsync(token)) return null;
        var rows = await database.Set<PublicContentDocumentRecord>().AsNoTracking().Where(row => row.OrganizationId == organizationId).ToArrayAsync(token);
        T[] Read<T>(string kind) where T : class => rows.Where(row => row.Kind == kind).OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => JsonSerializer.Deserialize<T>(row.DataJson, Options) ?? throw new InvalidOperationException("Invalid public projection.")).ToArray();
        var club = Read<PublicClub>(PublicContentKinds.Club).SingleOrDefault();
        if (club is not null)
        {
            // The runtime sees safe payload columns, never editor version metadata.
            var edited = await database.Set<AdminClubEditRecord>().AsNoTracking()
                .Where(row => row.Kind == PublicContentKinds.Club && row.OrganizationId == organizationId && row.Id == club.Id)
                .Select(row => row.DataJson).SingleOrDefaultAsync(token);
            if (edited is not null) club = JsonSerializer.Deserialize<PublicClub>(edited, Options) ?? throw new InvalidOperationException("Invalid club edit.");
        }
        // Overlay only safe, full public DTOs under the current organization's
        // immutable imported news IDs. The initial import/evidence count stays
        // unchanged and the public response never contains a version or source.
        var newsEdits = await database.Set<AdminClubEditRecord>().AsNoTracking()
            .Where(row => row.Kind == PublicContentKinds.News && row.OrganizationId == organizationId)
            // The public runtime has SELECT on safe projection columns only;
            // never materialize Version (or any editor metadata) under its role.
            .Select(row => new { row.Id, row.DataJson })
            .ToDictionaryAsync(row => row.Id, row => row.DataJson, token);
        var news = Read<PublicArticle>(PublicContentKinds.News)
            .Select(row => newsEdits.TryGetValue(row.Id, out var edited)
                ? JsonSerializer.Deserialize<PublicArticle>(edited, Options) ??
                    throw new InvalidOperationException("Invalid edited public article.") : row)
            .OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();

        // Academy navigation must reflect the actual group records managed in
        // the authenticated admin backend. Imported public group documents can
        // provide original presentation fields ONLY for the same canonical ID;
        // they cannot invent a website group by existing in the import alone.
        // The fixed SECURITY DEFINER reader returns an allowlisted JSON array:
        // no parent, child contact, staff, attendance or private table access.
        PublicAcademyGroup[] academyGroups = [];
        if (organizationId == "lefteria_fc_academy")
        {
            string? approvedJson;
            try
            {
                approvedJson = await database.Database.SqlQueryRaw<string>(
                    "SELECT club.read_public_academy_group_directory()::text AS \"Value\"")
                    .SingleOrDefaultAsync(token);
            }
            catch (PostgresException ex) when (ex.SqlState is "42883" or "42501" or "3F000")
            {
                // Missing migration/explicit runtime grant is unavailable
                // rather than a misleading "no Academy groups" success.
                return null;
            }
            if (approvedJson is null) return null; // canonical roster not initialized
            academyGroups = JsonSerializer.Deserialize<PublicAcademyGroup[]>(approvedJson, Options)
                ?? throw new InvalidOperationException("Invalid canonical Academy projection.");
        }
        var result = new PublicSiteResult(organizationId, club,
            Read<PublicTeam>(PublicContentKinds.Team), academyGroups,
            news, Read<PublicFixture>(PublicContentKinds.Fixture)
                .OrderBy(row => row.MatchDate).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray());
        await transaction.CommitAsync(token);
        return result;
    }

    public async Task<IReadOnlyList<PublicArticle>?> ReadNewsAsync(string? organizationId, CancellationToken token, string? articleId = null)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await database.Set<PublicContentImportStateRecord>().AsNoTracking().SingleOrDefaultAsync(token);
        if (state is null || state.DocumentsCount != await database.Set<PublicContentDocumentRecord>().CountAsync(token)) return null;
        // Keep readiness checks ahead of not-found responses. Invalid imported
        // identifiers cannot match any row, and PostgreSQL text rejects NUL.
        if (articleId is not null && (articleId.Length > 128 || articleId.Any(char.IsControl)))
        {
            await transaction.CommitAsync(token);
            return Array.Empty<PublicArticle>();
        }
        var query = database.Set<PublicContentDocumentRecord>().AsNoTracking().Where(row => row.Kind == PublicContentKinds.News);
        if (organizationId is not null) query = query.Where(row => row.OrganizationId == organizationId);
        // Detail reads materialize only the requested ID across permitted
        // organizations. Preserve the existing .NET ordinal owner precedence.
        if (articleId is not null) query = query.Where(row => row.Id == articleId);
        var rows = await query.ToArrayAsync(token);
        var editQuery = database.Set<AdminClubEditRecord>().AsNoTracking()
            .Where(row => row.Kind == PublicContentKinds.News);
        if (organizationId is not null) editQuery = editQuery.Where(row => row.OrganizationId == organizationId);
        if (articleId is not null) editQuery = editQuery.Where(row => row.Id == articleId);
        var newsEdits = (await editQuery
            .Select(row => new { row.OrganizationId, row.Id, row.DataJson })
            .ToArrayAsync(token))
            .ToDictionary(row => (row.OrganizationId, row.Id), row => row.DataJson);
        // No selector means the original public all-news feed. The same story
        // may have distinct owner-specific edits; deduplicate *after* applying
        // each owner's safe published projection, preserving original behavior.
        var articles = rows.OrderBy(row => row.OrganizationId, StringComparer.Ordinal)
            .Select(row => JsonSerializer.Deserialize<PublicArticle>(
                newsEdits.TryGetValue((row.OrganizationId, row.Id), out var edited)
                    ? edited : row.DataJson, Options) ??
                throw new InvalidOperationException("Invalid public projection."))
            .GroupBy(row => row.Id, StringComparer.Ordinal).Select(group => group.First())
            .OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
        await transaction.CommitAsync(token);
        return articles;
    }
}

public static class PublicContentRegistration
{
    public static IServiceCollection AddPublicContent(this IServiceCollection services) => services
        .AddScoped<IPublicContentRepository, PostgresPublicContentRepository>().AddScoped<PublicContentService>();
}
