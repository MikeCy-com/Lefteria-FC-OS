using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Pos;

/// <summary>
/// Dedicated manager-only register discovery and shift controls.
/// No sale checkout, payment capture, refund execution or public POS access.
/// Requires explicitly approved SQL EXECUTE grants to the independent
/// web-admin role; absent grants, the operations fail closed.
/// </summary>
public static class AdminPosCashierEndpoints
{
    public sealed record OpenShiftRequest(Guid RequestId, long OpeningCashCents);
    public sealed record CloseShiftRequest(Guid RequestId, long CountedCashCents);

    public static IEndpointRouteBuilder MapAdminPosCashier(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/pos/cashier")
            .WithTags("POS cashier administration")
            .RequireAuthorization(WebAdminServices.Policy);

        group.MapGet("/registers", async (string? organization_id,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos.read_admin_cashier_registers(@digest,@owner)::text", connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            return await Query(command, ct);
        }).WithName("ReadAdminPosCashierRegisters");

        group.MapPost("/registers/{registerId}/shifts", async (string registerId,
            string? organization_id, OpenShiftRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (!ValidRegister(registerId) || body.RequestId == Guid.Empty ||
                body.OpeningCashCents is < 0 or > 100000000)
                throw new WebAdminException(400, "pos_shift_invalid",
                    "Invalid register or opening cash.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos.open_admin_cashier_shift(@digest,@owner,@register,@opening,@request)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("register", registerId);
            command.Parameters.AddWithValue("opening", body.OpeningCashCents);
            command.Parameters.AddWithValue("request", body.RequestId);
            return await Query(command, ct);
        }).WithName("OpenAdminPosCashierShift");

        group.MapPost("/shifts/{shiftId:guid}/close", async (Guid shiftId,
            string? organization_id, CloseShiftRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (shiftId == Guid.Empty || body.RequestId == Guid.Empty ||
                body.CountedCashCents is < 0 or > 100000000)
                throw new WebAdminException(400, "pos_shift_invalid",
                    "Invalid shift or counted cash.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos.close_admin_cashier_shift(@digest,@owner,@shift,@counted,@request)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("shift", shiftId);
            command.Parameters.AddWithValue("counted", body.CountedCashCents);
            command.Parameters.AddWithValue("request", body.RequestId);
            return await Query(command, ct);
        }).WithName("CloseAdminPosCashierShift");
        return endpoints;
    }

    private static bool ValidRegister(string value) =>
        value.Length is >= 1 and <= 80 &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    private static async Task<(string Cookie, string Owner)> Authority(
        WebAdminService auth, string? organization, HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.Pragma = "no-cache";
        if (context.Request.Query["organization_id"].Count != 1 ||
            organization is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400, "pos_owner_invalid",
                "Select one valid organization.");
        var cookie = context.Request.Cookies[WebAdminServices.CookieName];
        var session = await auth.CurrentAsync(cookie, ct);
        if (session is null)
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (session.User.Role is not ("manager" or "super_admin") ||
            !AdminPermissions.Has(session.User.Role, "shop:write") ||
            !session.User.Organizations.Any(o => o.Id == organization))
            throw new WebAdminException(403, "pos_cashier_access_denied",
                "Cashier management requires an authorized organization and role.");
        return (cookie!, organization);
    }

    private static async Task<IResult> Query(NpgsqlCommand command, CancellationToken ct)
    {
        try
        {
            var result = await command.ExecuteScalarAsync(ct) as string;
            return result is null ? Results.Problem(statusCode: 503)
                : Results.Content(result, "application/json", Encoding.UTF8, 200);
        }
        catch (PostgresException ex) when (ex.SqlState is
            "42501" or "22023" or "23505" or "23514" or "55000" or "54000")
        {
            var status = ex.SqlState switch
            {
                "42501" => 403,
                "22023" => 422,
                "23505" => 409,
                "23514" => 409,
                "54000" => 413,
                _ => 409
            };
            throw new WebAdminException(status, "pos_cashier_operation_unavailable",
                "The cashier operation could not be completed. Refresh register status.");
        }
        catch (PostgresException ex) when (ex.SqlState is "42883" or "3F000")
        {
            throw new WebAdminException(503, "pos_cashier_not_configured",
                "Cashier operations are not enabled in this environment.");
        }
    }
}
