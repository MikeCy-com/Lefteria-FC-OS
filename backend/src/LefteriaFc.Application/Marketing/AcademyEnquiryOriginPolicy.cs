namespace LefteriaFc.Application.Marketing;

/// <summary>
/// Additional CSRF-origin check for a future public Academy enquiry route.
/// The allowed origin is a server-side deployment configuration value; NEVER
/// derive it from caller Host / Forwarded / X-Forwarded-Host headers.
/// This is not authentication or a substitute for the DB rate limit.
/// </summary>
public static class AcademyEnquiryOriginPolicy
{
    public static bool IsAllowed(string? requestOrigin,string? configuredOrigin)
    {
        if(!TryCanonicalOrigin(requestOrigin,out var received) ||
           !TryCanonicalOrigin(configuredOrigin,out var expected))
            return false;
        return StringComparer.Ordinal.Equals(received,expected);
    }

    private static bool TryCanonicalOrigin(string? raw,out string? canonical)
    {
        canonical=null;
        if(string.IsNullOrWhiteSpace(raw) || raw.Length>512 ||
           raw.Any(char.IsControl) || raw.Any(char.IsWhiteSpace) ||
           !Uri.TryCreate(raw,UriKind.Absolute,out var origin) ||
           !string.IsNullOrEmpty(origin.UserInfo) ||
           origin.AbsolutePath!="/" ||
           !string.IsNullOrEmpty(origin.Query) ||
           !string.IsNullOrEmpty(origin.Fragment) ||
           origin.Host.Length==0)
            return false;

        // Production/preview require TLS. Local HTTP is allowed only for an
        // explicit loopback target during development or integration tests.
        if(!origin.Scheme.Equals(Uri.UriSchemeHttps,StringComparison.OrdinalIgnoreCase) &&
           !(origin.Scheme.Equals(Uri.UriSchemeHttp,StringComparison.OrdinalIgnoreCase)
              && origin.IsLoopback))
            return false;

        canonical=origin.GetComponents(UriComponents.SchemeAndServer,UriFormat.SafeUnescaped);
        return true;
    }
}
