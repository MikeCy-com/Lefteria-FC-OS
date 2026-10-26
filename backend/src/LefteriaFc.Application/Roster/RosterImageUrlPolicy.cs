namespace LefteriaFc.Application.Roster;

public static partial class RosterImportValidator
{
    private static readonly string[] PublicImageFolders = ["avatars", "players", "gallery", "media", "sponsors"];

    public static bool PublicImageUrl(string value)
    {
        if (value.Length is < 1 or > 2048 || value.Any(char.IsControl) || value.Contains('\\') || value != value.Trim()) return false;
        if (value.StartsWith('/')) return !value.StartsWith("//", StringComparison.Ordinal) && NormalizePath(value) is { } local && PublicUploadPath(local);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        var path = NormalizePath(uri.AbsolutePath);
        if (path is null) return false;
        if (path.Split('/').Any(part => part.ToLowerInvariant() is "private_attachments" or "player_docs" or "staff_docs" or "expenses")) return false;
        return !(path.Equals("/api", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/uploads", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) || PublicUploadPath(path);
    }
    private static string? NormalizePath(string path)
    {
        var end = path.IndexOfAny(['?', '#']);
        if (end >= 0) path = path[..end];
        for (var pass = 0; pass < 4 && path.Contains('%'); pass++)
        {
            var decoded = Uri.UnescapeDataString(path);
            if (decoded == path) break;
            path = decoded;
        }
        if (path.Contains('%') || path.Contains('\\') || path.Any(char.IsControl)) return null;
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        return "/" + string.Join('/', parts);
    }
    private static bool PublicUploadPath(string value)
    {
        var end = value.IndexOfAny(['?', '#']);
        var path = end < 0 ? value : value[..end];
        var prefix = path.StartsWith("/api/uploads/", StringComparison.Ordinal) ? "/api/uploads/" :
            path.StartsWith("/uploads/", StringComparison.Ordinal) ? "/uploads/" : null;
        if (prefix is null || path.Contains('%')) return false;
        var parts = path[prefix.Length..].Split('/');
        return parts.Length >= 2 && PublicImageFolders.Contains(parts[0], StringComparer.Ordinal) &&
            parts.All(part => part.Length > 0 && part is not ("." or ".."));
    }
}
