using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace LefteriaFc.Application.MobileAuth;

public static partial class MobileAvatarLimits
{
    public const int InputBytes = 5 * 1024 * 1024;
    public const int RequestBytes = InputBytes + 64 * 1024;
    public const long InputPixels = 16_000_000;
    public const int InputSide = 8192;
    public const int OutputSide = 1024;
    public const int OutputBytes = 1024 * 1024;
    public static bool IsFileName(string? fileName) => fileName is not null && FileNamePattern().IsMatch(fileName);
    public static string Url(string fileName) => "/api/uploads/avatars/" + fileName;
    [GeneratedRegex(@"\A[0-9a-f]{32}_[0-9a-f]{64}\.jpg\z", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();
}

public sealed record SanitizedMobileAvatar(byte[] JpegBytes, int Width, int Height);
public sealed record MobileAvatarContent(byte[] JpegBytes);
public sealed record MobileAvatarResult([property: JsonPropertyName("avatar_url")] string AvatarUrl);

public interface IAvatarImageSanitizer
{
    // Decodes only a bounded first frame. Never persists the original bytes/name.
    SanitizedMobileAvatar Sanitize(ReadOnlyMemory<byte> input, string? declaredContentType,
        CancellationToken cancellationToken);
}
