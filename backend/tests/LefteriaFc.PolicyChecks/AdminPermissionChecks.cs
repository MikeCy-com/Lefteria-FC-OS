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
            Require(AdminPermissions.Has("marketing", "social:write") && !AdminPermissions.Has("marketing", "news:write"));
            Require(AdminPermissions.Has("matchday_editor", "match_reports:write") && !AdminPermissions.Has("matchday_editor", "teams:write"));
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
