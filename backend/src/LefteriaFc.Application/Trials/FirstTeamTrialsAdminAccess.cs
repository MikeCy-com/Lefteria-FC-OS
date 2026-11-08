using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;

namespace LefteriaFc.Application.Trials;

/// <summary>
/// An authorization precondition for a future DB-backed trials admin API.
/// A fresh authenticated session and its current organization assignments
/// must be verified by the endpoint's repository before calling this policy.
/// This check alone never authorizes a database operation or public form.
/// </summary>
public static class FirstTeamTrialsAdminAccess
{
    private const string Organization = "lefteria_fc";

    public static bool CanRead(WebAdminIdentityDto? user, string? selectedOrganization)
    {
        if (user is null || !string.Equals(selectedOrganization, Organization, StringComparison.Ordinal))
            return false;
        if (!AdminPermissions.IsKnownRole(user.Role))
            return false;
        if (!user.Organizations.Any(org => org.Id == Organization))
            return false;
        // The source allows both settings managers and read-only marketing
        // editors to see recruitment status; no write capability is inferred.
        return AdminPermissions.Has(user.Role, "settings:read") ||
               AdminPermissions.Has(user.Role, "marketing:read");
    }

    public static bool CanEdit(WebAdminIdentityDto? user, string? selectedOrganization) => false;
}
