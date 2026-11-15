using System.Globalization;
using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Finance;

/// <summary>
/// Owner-wide source-ledger overview; not a trial balance. Manager/location
/// permissions must never imply access to whole Academy financial figures.
/// </summary>
public static class FinanceOverviewEndpoints
{
    public static IEndpointRouteBuilder MapAdminFinanceOverview(
        this IEndpointRouteBuilder endpoints)
    {
        var routes=endpoints.MapGroup("/api/admin/finance/overview")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Private finance overview");

        routes.MapGet("", async (string? organization_id, string? month,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl="private, no-store";
            if (organization_id is not ("lefteria_fc" or "lefteria_fc_academy")
                || context.Request.Query["organization_id"].Count!=1
                || context.Request.Query["month"].Count!=1
                || month is null || month.Length!=7
                || !DateOnly.TryParseExact(month+"-01","yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out var selected)
                || selected.Year is < 2020 or > 2100)
                throw new WebAdminException(422,"finance_overview_invalid",
                    "Choose one authorized organization and a valid YYYY-MM.");

            var cookie=context.Request.Cookies[WebAdminServices.CookieName];
            var session=await auth.CurrentAsync(cookie,ct);
            if(session is null)
                throw new WebAdminException(401,"not_authenticated","Sign in again.");
            if(session.User.Role is not ("super_admin" or "accountant")
                || !AdminPermissions.Has(session.User.Role,"finance:read")
                || !session.User.Organizations.Any(o=>o.Id==organization_id))
                throw new WebAdminException(403,"finance_overview_denied",
                    "Organization-wide financial reports require an assigned accountant or Super Admin.");

            await using var db=await connections.OpenAsync(ct);
            await using var cmd=new NpgsqlCommand(
                "SELECT finance.read_admin_accounting_overview(@digest,@owner,@month)::text",db);
            cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie!));
            cmd.Parameters.AddWithValue("owner",organization_id);
            cmd.Parameters.AddWithValue("month",NpgsqlDbType.Date,selected);
            try
            {
                var json=await cmd.ExecuteScalarAsync(ct) as string;
                return json is null ? Results.Problem(statusCode:503)
                    : Results.Content(json,"application/json",Encoding.UTF8,200);
            }
            catch(PostgresException e) when(e.SqlState=="42501")
            {
                throw new WebAdminException(403,"finance_overview_denied",
                    "Financial access was revoked or is outside the assigned organization.");
            }
            catch(PostgresException e) when(e.SqlState is "22023" or "22007" or "22008")
            {
                throw new WebAdminException(422,"finance_overview_invalid",
                    "Invalid reporting period.");
            }
            catch(PostgresException e) when(e.SqlState is "42883" or "3F000")
            {
                throw new WebAdminException(503,"finance_overview_unavailable",
                    "The isolated finance overview migration is not enabled.");
            }
        }).WithName("ReadAdminFinanceOverview")
          .ExcludeFromDescription();
        return endpoints;
    }
}
