using System.Net;

namespace LefteriaFc.Application.Marketing;

/// <summary>
/// Explicit launch gate for a future Academy enquiry HTTP route. Reading a
/// request does not activate the route; the host must intentionally map it in
/// a separately reviewed change. The caller must supply only the normalized
/// connection address AFTER strictly configured trusted-proxy processing,
/// never user-supplied X-Forwarded-For/Forwarded headers.
/// </summary>
public static class AcademyEnquiryActivationGate
{
    public static bool TryCreateFingerprint(
        string? enabledSetting,
        string? dedicatedKeyBase64,
        IPAddress? trustedRemoteAddress,
        out string? fingerprint)
    {
        fingerprint = null;
        // Opt-in is intentionally case-sensitive and fail-closed. No key is
        // decoded while the feature is off, nor is any provider contacted.
        if (!string.Equals(enabledSetting, "true", StringComparison.Ordinal)
            || trustedRemoteAddress is null
            || string.IsNullOrWhiteSpace(dedicatedKeyBase64))
            return false;

        byte[] key;
        try
        {
            key = Convert.FromBase64String(dedicatedKeyBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            if (key.Length != 32) return false;
            fingerprint = AcademyLeadFingerprint.Compute(trustedRemoteAddress, key);
            return true;
        }
        finally
        {
            // Do not retain secret copies in managed memory any longer than
            // necessary. The configuration source owns the original value.
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        }
    }
}
