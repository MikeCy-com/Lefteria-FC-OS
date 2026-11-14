using System.Text.Json.Serialization;
using LefteriaFc.Application.Roster;
using LefteriaFc.Application.WebAdmin;

namespace LefteriaFc.Application.PublicContent;

/// <summary>
/// Private news workspace. Drafts are never part of PublicArticle, the
/// imported public feed, the mobile feed or a social publication worker.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminNewsDraftEditable(
    string Title, string Content, string Excerpt, string? ImageUrl,
    string Category, string Audience, bool IsFeatured)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || !Text(Title, 250) ||
            !Text(Content, 100000, multiline: true) ||
            !Text(Excerpt, 2000, multiline: true) ||
            !Text(Category, 120) || !Text(Audience, 120) ||
            !Image(ImageUrl))
            throw Invalid();
    }

    private static bool Text(string? text, int max, bool multiline = false) =>
        text is not null && text.Length <= max && text.All(ch =>
            ch >= ' ' && ch != '\u007f' || multiline && ch is '\n' or '\r' or '\t');

    private static bool Image(string? value) => string.IsNullOrEmpty(value) ||
        value.Length <= 2048 && RosterImportValidator.PublicImageUrl(value) &&
        (value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    public static WebAdminException Invalid() =>
        new(422, "invalid_news_draft", "Check the news draft fields.");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminNewsDraftCreate([property: JsonRequired] AdminNewsDraftEditable Article);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminNewsDraftRevision(
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] long ExpectedVersion,
    [property: JsonRequired] AdminNewsDraftEditable Article);

public sealed record AdminNewsDraftDto(
    string Id, string OrganizationId, string Status,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version,
    AdminNewsDraftEditable Article, DateTimeOffset UpdatedAt);

public sealed record AdminNewsDraftPageDto(string OrganizationId, IReadOnlyList<AdminNewsDraftDto> Drafts);

public interface IAdminNewsDraftRepository
{
    Task<AdminNewsDraftPageDto> ReadAsync(string token, string organization, CancellationToken cancellationToken);
    Task<AdminNewsDraftDto> CreateAsync(string token, string organization,
        AdminNewsDraftCreate command, CancellationToken cancellationToken);
    Task<AdminNewsDraftDto> UpdateAsync(string token, string organization, string draftId,
        AdminNewsDraftRevision command, CancellationToken cancellationToken);
}
