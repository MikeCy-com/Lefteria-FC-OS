using LefteriaFc.Domain.AdminAuth;

internal static class AdminPermissionChecks
{
    public static IEnumerable<(string Name, Action Check)> Checks()
    {
        yield return ("Unknown CMS roles never inherit administrator access", () =>
        {
            foreach (var role in new string?[] { null, "", "admin", "Super_admin", "management", "parent" })
                Require(!AdminPermissions.IsKnownRole(role) && AdminPermissions.ForRole(role).Count == 0 && !AdminPermissions.Has(role, "news:write"));
        });
        yield return ("CMS manager cannot manage users or permanently delete staff", () =>
        {
            Require(AdminPermissions.Has("manager", "news:write"));
            Require(AdminPermissions.Has("manager", "staff:archive"));
            Require(!AdminPermissions.Has("manager", "users:write"));
            Require(!AdminPermissions.Has("manager", "staff:delete"));
            Require(AdminPermissions.Has("super_admin", "users:write"));
        });
        yield return ("CMS coach assignments do not become organization-wide capabilities", () =>
        {
            Require(AdminPermissions.Has("coach", "training:write_assigned"));
            Require(!AdminPermissions.Has("coach", "training:write"));
            Require(!AdminPermissions.Has("coach", "teams:read"));
            Require(!AdminPermissions.Has("coach", "finance:read"));
        });
        yield return ("CMS editorial, marketing and match-day boundaries remain distinct", () =>
        {
            Require(AdminPermissions.Has("editor", "news:write") && AdminPermissions.Has("editor", "marketing:read"));
            Require(!AdminPermissions.Has("editor", "marketing:write") && !AdminPermissions.Has("editor", "settings:write"));
            Require(AdminPermissions.Has("marketing", "social:write") &&
                AdminPermissions.Has("marketing", "news:write") &&
                AdminPermissions.Has("marketing", "sponsors:write") &&
                AdminPermissions.Has("marketing", "gallery:read") &&
                AdminPermissions.Has("marketing", "marketing:write"));
            Require(!AdminPermissions.Has("marketing", "finance:read") &&
                !AdminPermissions.Has("marketing", "academy:read") &&
                !AdminPermissions.Has("marketing", "users:write"));
            Require(AdminPermissions.Has("matchday_editor", "match_reports:write") && !AdminPermissions.Has("matchday_editor", "teams:write"));
        });
        yield return ("Accountant is finance-only and never inherits organization or child access", () =>
        {
            Require(AdminPermissions.IsKnownRole("accountant"));
            Require(AdminPermissions.Has("accountant", "finance:read"));
            Require(AdminPermissions.Has("accountant", "finance:write"));
            foreach (var capability in new[]
            {
                "news:read", "news:write", "academy:read", "academy:write",
                "teams:read", "staff:read", "users:write", "marketing:read",
                "shop:write", "messages:read", "training:write_assigned"
            })
                Require(!AdminPermissions.Has("accountant", capability));
        });
        yield return ("Cashier can operate only an explicitly assigned terminal", () =>
        {
            Require(AdminPermissions.IsKnownRole("cashier"));
            Require(AdminPermissions.Has("cashier", "shop:operate"));
            foreach (var capability in new[]
            {
                "shop:read", "shop:write", "finance:read", "finance:write",
                "academy:read", "academy:write", "teams:read", "teams:write",
                "users:read", "users:write", "staff:write", "settings:read",
                "news:read", "messages:read", "training:read_assigned",
                "fixtures:read"
            })
                Require(!AdminPermissions.Has("cashier", capability));
        });
        yield return ("CMS permission projections cannot mutate the shared role matrix", () =>
        {
            var values = (IList<string>)AdminPermissions.ForRole("manager");
            Require(values.IsReadOnly);
            try { values.Add("users:*"); }
            catch (NotSupportedException) { Require(!AdminPermissions.Has("manager", "users:write")); return; }
            throw new InvalidOperationException("CMS permissions must be immutable.");
        });
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("CMS source permission boundary changed.");
    }
}
