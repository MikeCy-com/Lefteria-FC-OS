namespace LefteriaFc.Application.PublicContent;

// Source public React cards, not the overbroad legacy API objects, define these
// projections. Academy names are abbreviated before serialization. Private
// contacts, DOB, guardians, health, contracts and login data have no DTO members.
public sealed record PublicSectionStatistics(int? Appearances, int? Goals, int? Assists, int? MinutesPlayed, int? CleanSheets, int? YellowCards, int? RedCards);
public sealed record PublicSectionPreviousClub(string ClubName);
public sealed record PublicSectionPlayer(string Id, string OrganizationId, string Name, int Number, string? ImageUrl, string TeamType,
    string? Position, int? Age, string? Nationality, string? Bio, string? Height, string? Weight, string? PreferredFoot,
    string? TeamId, string? AcademyGroupName, IReadOnlyList<string> AcademyGroupIds, PublicSectionStatistics? Statistics, IReadOnlyList<PublicSectionPreviousClub> PreviousClubs);
public sealed record PublicSectionExperience(string? Club, string? Role, string? Years);
public sealed record PublicSectionStaff(string Id, string OrganizationId, string Name, string Role, string? ImageUrl, string TeamType,
    string? Nationality, string? Bio, string? JoinedDate, IReadOnlyList<string> Qualifications, IReadOnlyList<string> Badges,
    string? CoachingPhilosophy, string? FavoriteQuote, IReadOnlyList<string> TeamIds, IReadOnlyList<string> AcademyGroupIds,
    IReadOnlyList<PublicSectionExperience> PreviousExperience);
public sealed record PublicSectionGroup(string Id, string OrganizationId, string? CoachId, string? CoachName, string? CoachRole, string? CoachImageUrl);
public sealed record PublicSectionStanding(string Id, string OrganizationId, string TeamName, string? TeamLogo,
    int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst, int GoalDifference, int Points,
    string Competition, string? Season, string? Form);
public sealed record PublicSectionVenue(string Id, string OrganizationId, string Name, string? Address, string? City,
    string? Country, int? Capacity, string? Surface, string? ImageUrl, string? MapUrl, decimal? Latitude, decimal? Longitude, bool IsHomeGround);
public sealed record PublicSectionSeason(string Id, string OrganizationId, string Name, string? StartDate, string? EndDate,
    bool IsCurrent, bool IsArchived, IReadOnlyList<string> Competitions);
public sealed record PublicSectionGallery(string Id, string OrganizationId, string Title, string ImageUrl, string Category,
    string? Description, string? PlayerId, string? TeamId, string? AcademyGroupId, bool IsFeatured, DateTimeOffset? CreatedAt);
public sealed record PublicSectionHighlight(string? Value, string? Label);
public sealed record PublicSectionBlock(string? Id, string Type, string? Title, string? Content, string? ImageUrl, string? LinkUrl,
    string? LinkText, IReadOnlyList<string> Images, IReadOnlyList<PublicSectionHighlight> Items);
public sealed record PublicSectionSponsor(string Id, string OrganizationId, string Name, string? Description, string? LogoUrl,
    string? BannerUrl, string? Website, string? Facebook, string? Instagram, string? Twitter, string? Youtube, string? Linkedin,
    string Level, string SponsorType, int DisplayOrder, IReadOnlyList<PublicSectionBlock> ContentBlocks);
public sealed record PublicSectionPartnerStaff(string Name, string? Role, string? PhotoUrl, string? Bio);
public sealed record PublicSectionBenefit(string Title, string? Description, string? Icon);
public sealed record PublicSectionSocial(string Platform, string Url);
public sealed record PublicSectionPartner(string Id, string OrganizationId, string Slug, string Name, string PartnerType,
    string? Tagline, string? HeroImageUrl, string? HeadshotUrl, string? ExecutiveName, string? ExecutiveRole, string? Bio,
    IReadOnlyList<string> BioBullets, IReadOnlyList<PublicSectionPartnerStaff> StaffMembers, IReadOnlyList<PublicSectionBenefit> Benefits,
    string? WebsiteUrl, IReadOnlyList<PublicSectionSocial> Socials, string? CtaTitle, string? CtaText,
    string? CtaButton1Label, string? CtaButton1Url, string? CtaButton2Label, string? CtaButton2Url, int DisplayOrder);
public sealed record PublicSectionEvent(string Id, string OrganizationId, string Title, string? Description, string EventType,
    DateTimeOffset StartAt, DateTimeOffset? EndAt, string? Location, string? CoverImageUrl, int? Capacity, decimal? PriceEur,
    string? RegistrationUrl, string Status, bool IsFeatured);
public sealed record PublicSectionProduct(string Id, string OrganizationId, string Name, string? Description, decimal Price,
    string? ImageUrl, string Category, IReadOnlyList<string> Sizes, bool InStock, string ProductType, IReadOnlyList<string> DeliveryOptions);
public sealed record PublicSectionTrials(string Id, string OrganizationId, bool Open, string? Headline, string? Subtitle,
    string? ButtonText, string? ClosedMessage);
public sealed record PublicSectionTicket(string Id, string OrganizationId, string Name, string? Description, decimal Price,
    string TicketType, string? FixtureId, bool Available, int MaxQuantity);
public sealed record PublicSectionReview(string Id, string OrganizationId, string AuthorName, string? AuthorRole,
    int Rating, string? Title, string Body, string? Response, DateTimeOffset? PublishedAt);
public sealed record PublicSectionArchivePlayer(string PlayerId, string Name, int? Number, string? ImageUrl, PublicSectionStatistics Statistics);
public sealed record PublicSectionArchive(string Id, string OrganizationId, string SeasonId, string SeasonName, DateTimeOffset? ArchivedAt,
    string TeamType, IReadOnlyList<PublicSectionFixture> Fixtures, IReadOnlyList<PublicSectionStanding> Standings,
    IReadOnlyList<PublicSectionArchivePlayer> PlayerStats, string? TopScorerName, int TopScorerGoals);
public sealed record PublicSectionAcademyStats(string Id, string OrganizationId, int AgeGroups, int Athletes,
    int? TrainingsPerWeek, int? DedicationPct, DateTimeOffset MeasuredAt);
public sealed record PublicSectionPotmCandidate(string PlayerId, string Name, int Number, string? Position, string? ImageUrl, int Votes);
public sealed record PublicSectionPotm(string Id, string OrganizationId, string MonthKey, int TotalVotes, IReadOnlyList<PublicSectionPotmCandidate> Results);
public sealed record PublicSectionBranding(string Id, string OrganizationId, string Name, string? GreekName, string? LogoUrl,
    string? Stadium, string? HomeVenueId, string? City, string? Country, string? History, int? StadiumCapacity, string? StadiumSurface,
    string? StadiumDimensions, string? StadiumImageUrl, IReadOnlyList<PublicSectionSocial> FirstTeamSocials,
    IReadOnlyList<PublicSectionSocial> AcademySocials, IReadOnlyList<PublicSectionSocial> LegacySocials);
// Counts describe only published projections and malformed/ambiguous source
// records. Intentionally inactive/draft/unapproved rows are not public counts.
public sealed record PublicSectionModule(string Id, string OrganizationId, string Status, int PublishedCount, int QuarantinedCount);
public record PublicSectionFixture(string Id, string OrganizationId, string HomeTeam, string AwayTeam,
    string? HomeTeamLogo, string? AwayTeamLogo, int? HomeScore, int? AwayScore, DateOnly MatchDate, string? MatchTime,
    string? Venue, string? Competition, string Status, string? TeamId, string? AcademyGroupId, string? Season, string TeamType);
public sealed record PublicSectionFixtureView : PublicSectionFixture
{
    public DateTimeOffset? KickoffAt { get; }
    public PublicSectionFixtureView(PublicSectionFixture source, DateTimeOffset? kickoffAt) : base(source) => KickoffAt = kickoffAt;
}
public sealed record PublicSectionsResult(string OrganizationId, IReadOnlyList<PublicSectionPlayer> Players,
    IReadOnlyList<PublicSectionStaff> Staff, IReadOnlyList<PublicSectionGroup> AcademyGroups, IReadOnlyList<PublicSectionStanding> Standings,
    IReadOnlyList<PublicSectionVenue> Venues, IReadOnlyList<PublicSectionSeason> Seasons, IReadOnlyList<PublicSectionGallery> Gallery,
    IReadOnlyList<PublicSectionSponsor> Sponsors, IReadOnlyList<PublicSectionPartner> Partners, IReadOnlyList<PublicSectionEvent> Events,
    IReadOnlyList<PublicSectionProduct> Products, PublicSectionTrials? TrialSettings, IReadOnlyList<PublicSectionFixtureView> Fixtures,
    IReadOnlyList<PublicSectionTicket> Tickets, IReadOnlyList<PublicSectionArchive> ArchivedSeasons, IReadOnlyList<PublicSectionReview> Reviews,
    PublicSectionAcademyStats? AcademyStats, PublicSectionPotm Potm, PublicSectionBranding? Branding, IReadOnlyList<PublicSectionModule> Modules,
    DateTimeOffset SnapshotAt, DateTimeOffset GeneratedAt, string TimeZone);

public interface IPublicSectionsRepository
{
    Task<PublicSectionsResult?> ReadAsync(string organizationId, CancellationToken token);
}

public sealed record PublicSectionsDocument(string Kind, string OrganizationId, string Id, string Json, int DisplayOrder);
public sealed record PublicSectionsSource(string Collection, int Ordinal, string Json)
{
    public override string ToString() => "Owner-only public-sections source";
}
public sealed record PublicSectionsIssue(string Collection, int Ordinal, string Code);
public sealed record ValidatedPublicSectionsImport(int Version, string Digest, string SourceCommit, string BasePublicDigest,
    DateTimeOffset ExportedAt, byte[] ManifestBytes, IReadOnlyList<PublicSectionsDocument> Documents,
    IReadOnlyList<PublicSectionsSource> Source, IReadOnlyList<PublicSectionsIssue> Issues)
{
    public override string ToString() => "Validated public-sections snapshot (private evidence retained)";
}
public sealed record PublicSectionsImportResult(bool Applied, bool Noop, int Version, int Documents, int Sources, int Issues);
