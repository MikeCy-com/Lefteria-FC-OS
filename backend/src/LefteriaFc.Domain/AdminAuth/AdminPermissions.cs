namespace LefteriaFc.Domain.AdminAuth;

// Original GitHub main 380b402: backend/routes/admin_users.py ROLE_PERMISSIONS.
// Organization and assigned-team authorization are independent of capabilities.
public static class AdminPermissions
{
    private static readonly IReadOnlyList<string> None = Array.AsReadOnly(Array.Empty<string>());
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Roles =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<string>>(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["super_admin"] = Permissions("*:*"),
                ["manager"] = Permissions(
                    "teams:*", "academy:*", "fixtures:*", "results:*", "live_score:*", "match_reports:*",
                    "events:*", "attendance:*", "news:*", "sponsors:*", "gallery:*", "messages:*", "wall:*",
                    "finance:*", "shop:*", "facilities:*", "training:*", "marketing:*",
                    "staff:read", "staff:write", "staff:archive", "staff:compliance", "settings:read", "settings:write"),
                ["editor"] = Permissions("news:*", "sponsors:*", "gallery:*", "marketing:read", "teams:read", "academy:read", "fixtures:read"),
                ["marketing"] = Permissions("social:write"),
                ["coach"] = Permissions("teams:read_assigned", "academy:read_assigned", "training:read_assigned",
                    "training:write_assigned", "attendance:read_assigned", "attendance:write_assigned", "messages:write_assigned", "fixtures:read"),
                ["matchday_editor"] = Permissions("live_score:*", "match_reports:*", "fixtures:read", "teams:read")
            });

    public static bool IsKnownRole(string? role) => role is not null && Roles.ContainsKey(role);

    public static IReadOnlyList<string> ForRole(string? role) =>
        role is not null && Roles.TryGetValue(role, out var permissions) ? permissions : None;

    public static bool Has(string? role, string permission)
    {
        if (string.IsNullOrEmpty(permission)) return false;
        var permissions = ForRole(role);
        if (permissions.Contains("*:*") || permissions.Contains(permission)) return true;
        var separator = permission.IndexOf(':');
        var resource = separator < 0 ? permission : permission[..separator];
        return permissions.Contains(resource + ":*");
    }

    private static IReadOnlyList<string> Permissions(params string[] values) =>
        Array.AsReadOnly(values.Order(StringComparer.Ordinal).ToArray());
}
