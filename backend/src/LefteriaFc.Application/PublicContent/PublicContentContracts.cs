namespace LefteriaFc.Application.PublicContent;

// These DTOs are deliberate public allowlists. Source credentials, bank details,
// contacts for players/guardians, medical records and RSVP data are never members.
public sealed record PublicClub(string Id, string OrganizationId, string Name, string? GreekName,
    int? Founded, string? Description, string? Motto, string? History, string? LogoUrl,
    string? StadiumImageUrl, string? City, string? Country, string? Address, string? Email,
    string? Phone, string? Website, IReadOnlyDictionary<string, string> SocialLinks);
public sealed record PublicTeam(string Id, string OrganizationId, string Name, string? Level,
    string? Description, string? BannerUrl);
public sealed record PublicAcademyGroup(string Id, string OrganizationId, string Name, string? AgeRange,
    string? City, string? LocationName, string? TrainingSchedule, string? Description,
    string? BannerUrl, string? Season, int DisplayOrder);
public sealed record PublicArticle(string Id, string OrganizationId, string Title, string Content,
    string Excerpt, string? ImageUrl, string Category, string Audience, bool IsFeatured, DateTimeOffset? CreatedAt);
public sealed record PublicFixture(string Id, string OrganizationId, string HomeTeam, string AwayTeam,
    string? HomeTeamLogo, string? AwayTeamLogo, int? HomeScore, int? AwayScore,
    DateOnly MatchDate, string? MatchTime, string? Venue, string? Competition, string Status,
    string? TeamId, string? AcademyGroupId);
public sealed record PublicSiteResult(string OrganizationId, PublicClub? Club,
    IReadOnlyList<PublicTeam> Teams, IReadOnlyList<PublicAcademyGroup> AcademyGroups,
    IReadOnlyList<PublicArticle> News, IReadOnlyList<PublicFixture> Fixtures)
{
    // Read-time metadata is separate from immutable imported fixture JSON.
    public DateTimeOffset? GeneratedAt { get; init; }
    public string? TimeZone { get; init; }
    public IReadOnlyList<PublicFixtureInstant>? FixtureClock { get; init; }
}
public sealed record PublicFixtureInstant(string Id, DateTimeOffset? KickoffAt);
public sealed record PublicNewsResult(IReadOnlyList<PublicArticle> Items, int Total, int Offset, int Limit);
public sealed record PublicContentError(string Detail, string Code);

public interface IPublicContentRepository
{
    Task<PublicSiteResult?> ReadAsync(string organizationId, CancellationToken token);
    Task<IReadOnlyList<PublicArticle>?> ReadNewsAsync(string? organizationId, CancellationToken token);
}

public sealed class PublicContentService(IPublicContentRepository repository)
{
    public static bool IsOrganization(string value) => value is "lefteria_fc" or "lefteria_fc_academy";

    public async Task<PublicSiteResult?> GetSiteAsync(string organizationId, CancellationToken token)
    {
        if (!IsOrganization(organizationId)) throw new ArgumentException("Unknown public organization.");
        return await repository.ReadAsync(organizationId, token);
    }

    public Task<IReadOnlyList<PublicArticle>?> GetNewsAsync(string? organizationId, CancellationToken token)
    {
        if (organizationId is not null && !IsOrganization(organizationId)) throw new ArgumentException("Unknown public organization.");
        return repository.ReadNewsAsync(organizationId, token);
    }
}
