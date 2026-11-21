using Microsoft.AspNetCore.Http;

namespace LefteriaFc.Api.Release;

/// <summary>
/// Opt-in Academy pilot feature boundary. Core Academy roster, invoices,
/// training/attendance, guardian access and manual finance remain available.
/// Paid checkout, POS and fan subscriptions require independent release gates.
/// </summary>
public static class AcademyMvpBoundary
{
    // A misspelled launch setting must stop startup, not silently enable
    // unverified merchant / checkout features.
    public static bool ValidMode(string? setting) =>
        setting is null or "true" or "false";

    public static bool Disabled(string? setting) => setting == "true";

    public static bool IsOptionalEndpoint(PathString path)
    {
        // Block only whole path segments. /api/admin/possessions etc. stay
        // unrelated; test both an exact prefix and nested mutations.
        if (path.StartsWithSegments("/api/admin/pos", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/admin/finance/payabl/payment-requests", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/payabl", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/mobile/hub/fan-subscription", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/mobile/hub/fan-learning", StringComparison.OrdinalIgnoreCase))
            return true;

        // Fee issuance, ledger readback and PDF invoices are core Academy
        // finance operations. Only the optional outbound SMS action is off.
        if (path.StartsWithSegments("/api/admin/finance/payabl/training-fees",
                StringComparison.OrdinalIgnoreCase, out var remaining))
        {
            var segments = (remaining.Value ?? string.Empty).Split('/',
                StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 2 &&
                   segments[1].Equals("sms", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
