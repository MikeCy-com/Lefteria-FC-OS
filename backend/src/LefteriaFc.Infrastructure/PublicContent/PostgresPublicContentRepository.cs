using System.Data;
using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Domain.PublicContent;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
        var result = new PublicSiteResult(organizationId, Read<PublicClub>(PublicContentKinds.Club).SingleOrDefault(),
            Read<PublicTeam>(PublicContentKinds.Team), Read<PublicAcademyGroup>(PublicContentKinds.AcademyGroup),
            Read<PublicArticle>(PublicContentKinds.News).OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            Read<PublicFixture>(PublicContentKinds.Fixture).OrderBy(row => row.MatchDate).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray());
        await transaction.CommitAsync(token);
        return result;
    }

    public async Task<IReadOnlyList<PublicArticle>?> ReadNewsAsync(string? organizationId, CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await database.Set<PublicContentImportStateRecord>().AsNoTracking().SingleOrDefaultAsync(token);
        if (state is null || state.DocumentsCount != await database.Set<PublicContentDocumentRecord>().CountAsync(token)) return null;
        var query = database.Set<PublicContentDocumentRecord>().AsNoTracking().Where(row => row.Kind == PublicContentKinds.News);
        if (organizationId is not null) query = query.Where(row => row.OrganizationId == organizationId);
        var rows = await query.ToArrayAsync(token);
        // No selector means the original public all-news feed. A shared source
        // article has two visibility copies; return its logical source ID once.
        var articles = rows.OrderBy(row => row.OrganizationId, StringComparer.Ordinal)
            .Select(row => JsonSerializer.Deserialize<PublicArticle>(row.DataJson, Options) ?? throw new InvalidOperationException("Invalid public projection."))
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
