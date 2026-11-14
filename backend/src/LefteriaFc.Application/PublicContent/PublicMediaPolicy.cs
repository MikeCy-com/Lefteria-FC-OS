using System.Text.RegularExpressions;

namespace LefteriaFc.Application.PublicContent;

// Original /api/uploads public folders (upload_delivery.py at 380b402). Private folders are never served here.
public static partial class PublicMediaPolicy
{
    public const string CacheControl = "public, max-age=2592000, immutable";

    private static readonly HashSet<string> Folders = new(StringComparer.Ordinal)
    {
        "players", "staff", "news", "partners", "sponsors", "gallery", "opponent", "groups", "academy",
        "products", "venues", "club", "teams", "events", "reels", "standings"
    };

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".webp"] = "image/webp", [".gif"] = "image/gif", [".avif"] = "image/avif"
    };

    public static string? ContentType(string? folder, string? fileName)
    {
        if (folder is null || fileName is null || !Folders.Contains(folder)) return null;
        if (fileName.Contains("..", StringComparison.Ordinal) || !FileName().IsMatch(fileName)) return null;
        return Types.GetValueOrDefault(Path.GetExtension(fileName));
    }

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} ._()+\-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex FileName();
}
