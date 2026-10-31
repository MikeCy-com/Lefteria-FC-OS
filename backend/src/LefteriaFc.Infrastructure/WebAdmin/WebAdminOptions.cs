using LefteriaFc.Application.WebAdmin;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Infrastructure.WebAdmin;

public sealed class WebAdminOptions
{
    public bool Enabled { get; set; }
    public string PublicOrigin { get; set; } = "";
    public string ThrottleKey { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public static bool IsValid(WebAdminOptions options)
    {
        if (!options.Enabled) return true;
        if (!Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            origin.UserInfo.Length > 0 || origin.AbsolutePath != "/" || origin.Query.Length > 0 || origin.Fragment.Length > 0 ||
            options.PublicOrigin.TrimEnd('/') != origin.GetLeftPart(UriPartial.Authority).TrimEnd('/')) return false;
        try { if (Convert.FromBase64String(options.ThrottleKey).Length < 32) return false; }
        catch (FormatException) { return false; }
        return !string.IsNullOrWhiteSpace(options.Host) && options.Port is > 0 and <= 65535 &&
            !string.IsNullOrWhiteSpace(options.Database) && !string.IsNullOrWhiteSpace(options.Username) && options.Password.Length >= 32;
    }
}

public sealed class WebAdminSettings(IOptions<WebAdminOptions> options) : IWebAdminSettings
{
    public bool Enabled => options.Value.Enabled;
}
