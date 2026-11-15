using System.Text;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.WebAdmin;

/// <summary>
/// Owner-locked read-only finance totals; not a profit-and-loss statement.
/// The function independently rechecks the active session and exact owner grant.
/// Organization-wide reporting is accountant or super-admin only; territory
/// managers must use the separate location-scoped reporting workflow.
/// </summary>
public static class AdminFinanceOverviewEndpoints
{
    public static IEndpointRouteBuilder MapAdminFinanceOverview(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/finance/overview", async (
            string? organization_id, HttpContext context, WebAdminService service,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            if (context.Request.Query["organization_id"].Count != 1 ||
                organization_id is null || !PublicContentService.IsOrganization(organization_id))
                throw new WebAdminException(400, "invalid_organization",
                    "Select exactly one valid organization.");

            var cookie = context.Request.Cookies[WebAdminServices.CookieName];
            var session = await service.CurrentAsync(cookie, ct);
            if (session is null)
                throw new WebAdminException(401, "not_authenticated", "Sign in again.");
            if (session.User.Role is not ("super_admin" or "accountant") ||
                !AdminPermissions.Has(session.User.Role, "finance:read") ||
                !session.User.Organizations.Any(o => o.Id == organization_id))
                throw new WebAdminException(403, "not_authorized",
                    "Organization-wide financial reporting requires explicit accountant or super-admin access.");

            await using var db = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT finance.read_admin_finance_overview(@digest,@owner)::text", db);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
            command.Parameters.AddWithValue("owner", organization_id);
            var result = await command.ExecuteScalarAsync(ct) as string;
            return result is null ? Results.Problem(statusCode: 503) :
                Results.Content(result, "application/json", Encoding.UTF8, 200);
        })
        .WithName("ReadAdminFinanceOverview")
        .WithTags("Web administration")
        .RequireAuthorization(WebAdminServices.Policy)
        .ExcludeFromDescription();

        app.MapGet("/api/admin/finance/billable-academy-players", async (
            string? organization_id, string? search, HttpContext context,
            WebAdminService service, WebAdminConnections connections,
            IWebAdminSecrets secrets, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            if (context.Request.Query["organization_id"].Count != 1 ||
                context.Request.Query["search"].Count > 1 ||
                organization_id != "lefteria_fc_academy" ||
                search is { Length: > 100 } ||
                (search?.Any(char.IsControl) ?? false))
                throw new WebAdminException(400, "invalid_filters", "Invalid Academy finance selector.");
            var cookie = context.Request.Cookies[WebAdminServices.CookieName];
            var session = await service.CurrentAsync(cookie, ct);
            if (session is null)
                throw new WebAdminException(401, "not_authenticated", "Sign in again.");
            if (session.User.Role is not ("super_admin" or "accountant") ||
                !AdminPermissions.Has(session.User.Role, "finance:read") ||
                !session.User.Organizations.Any(o => o.Id == organization_id))
                throw new WebAdminException(403, "not_authorized",
                    "Finance-only Academy lookup denied.");

            await using var db = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT finance.read_admin_billable_players(@digest,@owner,@search)::text", db);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
            command.Parameters.AddWithValue("owner", organization_id);
            command.Parameters.AddWithValue("search", search ?? "");
            var result = await command.ExecuteScalarAsync(ct) as string;
            return result is null ? Results.Problem(statusCode: 503) :
                Results.Content(result, "application/json", Encoding.UTF8, 200);
        })
        .WithName("ReadAdminFinanceBillableAcademyPlayers")
        .WithTags("Web administration")
        .RequireAuthorization(WebAdminServices.Policy)
        .ExcludeFromDescription();

        return app;
    }
}
