using System.Text.Json.Serialization;
using LefteriaFc.Application.Roster;
using LefteriaFc.Application.WebAdmin;

namespace LefteriaFc.Application.PublicContent;

/// <summary>
/// Only already-published imported news can be edited in this first CMS slice.
/// The ownership, imported article identifier and original publication date are
/// immutable and never accepted from the browser in an update command.
/// </summary>
public sealed record AdminNewsArticleDto(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version, PublicArticle Article);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminNewsUpdateCommand(
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] long ExpectedVersion,
    [property: JsonRequired] AdminNewsEditable Article);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminNewsEditable(
    string Title, string Content, string Excerpt, string? ImageUrl,
    string Category, string Audience, bool IsFeatured)
{
    public void Validate()
    {
        // Public CMS content is text/Markdown rendered through escaped tokens,
        // never untrusted raw HTML, inline JavaScript or a user-provided file.
        if (string.IsNullOrWhiteSpace(Title) || !Text(Title, 250) ||
            string.IsNullOrWhiteSpace(Content) || !Text(Content, 100000, multiline: true) ||
            !Text(Excerpt, 2000, multiline: true) ||
            string.IsNullOrWhiteSpace(Category) || !Text(Category, 120) ||
            string.IsNullOrWhiteSpace(Audience) || !Text(Audience, 120) ||
            !Image(ImageUrl))
            throw Invalid();
    }

    public PublicArticle WithEditableFields(PublicArticle original) =>
        original with
        {
            Title = Title,
            Content = Content,
            Excerpt = Excerpt,
            ImageUrl = ImageUrl,
            Category = Category,
            Audience = Audience,
            IsFeatured = IsFeatured
        };

    private static bool Text(string? text, int max, bool multiline = false) =>
        text is not null && text.Length <= max && text.All(ch =>
            (ch >= ' ' && ch != '\u007f') || multiline && ch is '\n' or '\r' or '\t');

    private static bool Image(string? url) => string.IsNullOrWhiteSpace(url) ||
        url.Length <= 2048 &&
        RosterImportValidator.PublicImageUrl(url) &&
        (url.StartsWith("/", StringComparison.Ordinal) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    public static WebAdminException Invalid() =>
        new(422, "invalid_news_article", "Check the public article fields.");
}

public interface IAdminNewsRepository
{
    Task<AdminNewsArticleDto> ReadAsync(string token, string organization, string articleId, CancellationToken cancellationToken);
    Task<AdminNewsArticleDto> UpdateAsync(string token, string organization, string articleId,
        AdminNewsUpdateCommand command, CancellationToken cancellationToken);
}
