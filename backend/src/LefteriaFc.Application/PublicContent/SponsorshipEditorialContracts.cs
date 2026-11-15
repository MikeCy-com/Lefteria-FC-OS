namespace LefteriaFc.Application.PublicContent;

/// <summary>Only these five public sponsor/partner fields are editable.
/// Imported identity, slugs, staff, benefits and provenance are immutable.</summary>
public sealed record SponsorshipEditorialCopy(
    string Name, string? Description, string? ImageUrl, string? WebsiteUrl, int DisplayOrder)
{
    public bool Valid()
    {
        return Name is { Length: > 0 and <= 160 } && !string.IsNullOrWhiteSpace(Name) &&
            Clean(Name) && Description is null or { Length: <= 800 } &&
            (Description is null || Clean(Description)) &&
            (ImageUrl is null || SafeUrl(ImageUrl, allowLocal: true)) &&
            (WebsiteUrl is null || SafeUrl(WebsiteUrl, allowLocal: false)) &&
            DisplayOrder is >= 0 and <= 10000;
    }

    private static bool Clean(string text) => text.All(c =>
        (c >= ' ' && c != '\u007f') || c is '\n' or '\r' or '\t');

    private static bool SafeUrl(string url, bool allowLocal)
    {
        if (url.Length is < 1 or > 2048 || url.Trim() != url ||
            url.Any(c => char.IsControl(c) || c is '\\' or '"' or '\'' or '<' or '>')) return false;
        if (allowLocal && url.StartsWith("/", StringComparison.Ordinal) &&
            !url.StartsWith("//", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(url, UriKind.Relative, out _)) return false;
            var path = url.Split('?', '#')[0].ToLowerInvariant();
            return !path.StartsWith("/admin", StringComparison.Ordinal) &&
                !path.StartsWith("/app", StringComparison.Ordinal) &&
                (!path.StartsWith("/api/", StringComparison.Ordinal) ||
                 path.StartsWith("/api/uploads/", StringComparison.Ordinal));
        }
        return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host) &&
            string.IsNullOrEmpty(uri.UserInfo);
    }
}

// JSON returned by the fixed SQL definer function. No draft/version fields.
public sealed record PublishedSponsorshipEditorial(string Kind, string ItemId, SponsorshipEditorialCopy Content);
