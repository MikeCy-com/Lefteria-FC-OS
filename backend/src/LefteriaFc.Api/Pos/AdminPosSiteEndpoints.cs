using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Pos;

/// <summary>
/// Canonical Academy POS sites, scoped site finance and reviewed terminal setup.
/// Never marks a sale paid, settles Payabl or exposes customer information.
/// The dedicated database role must have explicitly reviewed function grants.
/// </summary>
public static class AdminPosSiteEndpoints
{
    public sealed record EnsureAcademySite(Guid AcademyLocationId);
    public sealed record CreateTerminal(string? LocationId, string? Label);
    public sealed record AssignOperator(Guid AdminAccountId, bool Enabled);

    public static IEndpointRouteBuilder MapAdminPosSites(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/admin/pos/sites")
            .WithTags("POS sites and terminal configuration")
            .RequireAuthorization(WebAdminServices.Policy);

        routes.MapGet("", async (
            string? organization_id, WebAdminService auth,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authorized(auth, organization_id,
                context, false, false, false, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.read_admin_pos_sites(@digest,@owner)::text",connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            return await Query(command,ct);
        }).WithName("ReadAdminPosAuthorizedSites");

        routes.MapGet("/{locationId}/finance", async (
            string locationId, string? organization_id, WebAdminService auth,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            ValidLocation(locationId);
            var (cookie, owner) = await Authorized(auth, organization_id,
                context, false, false, true, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.read_admin_pos_site_finances(@digest,@owner,@location)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("location", locationId);
            return await Query(command,ct);
        }).WithName("ReadAdminPosExactSiteFinances");

        routes.MapPost("/academy-locations", async (
            string? organization_id, EnsureAcademySite input,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authorized(auth, organization_id,
                context, true, true, false, ct);
            if(owner!="lefteria_fc_academy" || input.AcademyLocationId==Guid.Empty)
                throw new WebAdminException(422,"pos_site_invalid",
                    "Select a canonical Academy location.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.ensure_admin_academy_pos_site(@digest,@location)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("location", input.AcademyLocationId);
            return await Query(command,ct);
        }).WithName("EnsureAdminCanonicalAcademyPosSite");

        routes.MapGet("/terminal-setup", async (
            string? organization_id, WebAdminService auth,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authorized(auth, organization_id,
                context, false, true, false, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.read_admin_terminal_setup(@digest,@owner)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            return await Query(command,ct);
        }).WithName("ReadAdminPosTerminalSetup");

        routes.MapPost("/terminals", async (
            string? organization_id, CreateTerminal input,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authorized(auth, organization_id,
                context, true, true, false, ct);
            ValidLocation(input.LocationId);
            if(string.IsNullOrWhiteSpace(input.Label) || input.Label.Length>140 ||
               input.Label!=input.Label.Trim() || input.Label.Any(char.IsControl))
                throw new WebAdminException(422,"pos_terminal_label_invalid",
                    "Enter a terminal label up to 140 characters.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.create_admin_terminal(@digest,@owner,@location,@label)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("location", input.LocationId!);
            command.Parameters.AddWithValue("label", input.Label);
            return await Query(command,ct);
        }).WithName("CreateAdminPosSiteTerminal");

        routes.MapPut("/terminals/{registerId}/operators", async (
            string registerId, string? organization_id, AssignOperator input,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            ValidLocation(registerId);
            var (cookie, owner) = await Authorized(auth, organization_id,
                context, true, true, false, ct);
            if(input.AdminAccountId==Guid.Empty)
                throw new WebAdminException(422,"pos_operator_invalid",
                    "Select an existing authorized operator.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.set_admin_terminal_operator(@digest,@owner,@register,@subject,@enabled)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("register", registerId);
            command.Parameters.AddWithValue("subject", input.AdminAccountId);
            command.Parameters.AddWithValue("enabled", input.Enabled);
            return await Query(command,ct);
        }).WithName("AssignAdminPosTerminalOperator");

        return endpoints;
    }

    private static void ValidLocation(string? value)
    {
        if(value is null || value.Length is < 1 or > 80 ||
           !value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
            throw new WebAdminException(422,"pos_location_invalid",
                "Select an authorized POS site or register.");
    }

    private static async Task<(string Cookie,string Owner)> Authorized(
        WebAdminService auth,string? organization,HttpContext context,
        bool write,bool superAdminOnly,bool finance,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(context.Request.Query["organization_id"].Count!=1 ||
           organization is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400,"pos_owner_invalid",
                "Select one valid organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await auth.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(session.User.Role is not ("manager" or "super_admin") ||
           (superAdminOnly && session.User.Role!="super_admin") ||
           !AdminPermissions.Has(session.User.Role,write?"shop:write":"shop:read") ||
           (finance && !AdminPermissions.Has(session.User.Role,"finance:read")) ||
           !session.User.Organizations.Any(o=>o.Id==organization))
            throw new WebAdminException(403,"pos_site_denied",
                "This POS site is not assigned to your account.");
        return (cookie!,organization);
    }

    private static async Task<IResult> Query(NpgsqlCommand command,CancellationToken ct)
    {
        try {
            var content=await command.ExecuteScalarAsync(ct) as string;
            return content is null ? Results.Problem(statusCode:503)
                : Results.Content(content,"application/json",Encoding.UTF8,200);
        } catch(PostgresException error) when(error.SqlState is
            "42501" or "22023" or "23505" or "23514" or "54000") {
            var status=error.SqlState switch {
                "42501"=>403,"22023"=>422,"23505"=>409,"23514"=>409,_=>413
            };
            throw new WebAdminException(status,"pos_site_operation_unavailable",
                "The selected location or current permissions have changed.");
        } catch(PostgresException error) when(error.SqlState is
            "42883" or "3F000") {
            throw new WebAdminException(503,"pos_site_not_configured",
                "POS sites are not yet enabled in this environment.");
        }
    }
}
