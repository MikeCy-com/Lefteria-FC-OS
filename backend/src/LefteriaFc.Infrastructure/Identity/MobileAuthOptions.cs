namespace LefteriaFc.Infrastructure.Identity;

public sealed class MobileAuthOptions
{
    public string SigningKey { get; set; } = "";
    public string OtpKey { get; set; } = "";
    public string Issuer { get; set; } = "lefteria-fc-os";
    public string Audience { get; set; } = "lefteria-fc-os-mobile";

    public static bool IsStrongKey(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > 180) return false;
        Span<byte> decoded = stackalloc byte[128];
        return Convert.TryFromBase64String(encoded, decoded, out var count) && count >= 32;
    }
}

public sealed class TwilioSmsOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string FromNumber { get; set; } = "";
    public Dictionary<string, string> RoleFromNumbers { get; set; } = new(StringComparer.Ordinal);
}
