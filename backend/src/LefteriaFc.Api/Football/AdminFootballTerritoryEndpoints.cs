using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Football;

/// <summary>
/// Private football catalog administration. The independently credentialed
/// database function revalidates exact city/location/team grants on every
/// read and write, rather than trusting the UI's organization filter.
/// </summary>
public static class AdminFootballTerritoryEndpoints
{
    public sealed record FootballCreate(
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("data")] JsonElement Data);

    public static IEndpointRouteBuilder MapAdminFootballTerritories(
        this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/admin/football/territories")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Football territories");

        routes.MapGet("", async (
            string? organization_id, WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await Verify(auth, organization_id, context, false, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT club.read_admin_football_directory(@digest,@owner)::text", connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", organization_id!);
            return await Execute(command, ct);
        }).WithName("ReadAdminFootballTerritories")
          .ExcludeFromDescription();

        routes.MapPost("", async (
            string? organization_id, FootballCreate body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await Verify(auth, organization_id, context, true, ct);
            if (body.Kind is not ("city" or "location" or "team_scope" or
                "group_assignment" or "opponent" or "playing_field" or
                "training_base" or "match_draft") ||
                body.Data.ValueKind != JsonValueKind.Object ||
                body.Data.EnumerateObject().Count() > 12)
                throw new WebAdminException(422, "football_catalog_invalid",
                    "Select an available catalogue operation and provide its details.");

            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT club.create_admin_football_resource(@digest,@owner,@kind,@data)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", organization_id!);
            command.Parameters.AddWithValue("kind", body.Kind);
            command.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb,
                body.Data.GetRawText());
            return await Execute(command, ct);
        }).WithName("CreateAdminFootballTerritoryResource")
          .Accepts<FootballCreate>("application/json")
          .ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<string> Verify(
        WebAdminService auth, string? owner, HttpContext context,
        bool write, CancellationToken ct)
    {
        if (owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
            context.Request.Query["organization_id"].Count != 1)
            throw new WebAdminException(400, "football_organization_invalid",
                "Select exactly one verified organization.");

        var cookie = context.Request.Cookies[WebAdminServices.CookieName];
        var session = await auth.CurrentAsync(cookie, ct);
        if (session is null)
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");

        var permission = (owner == "lefteria_fc" ? "teams" : "academy") +
            (write ? ":write" : ":read");
        if (session.User.Role is not ("manager" or "super_admin") ||
            !AdminPermissions.Has(session.User.Role, permission) ||
            !session.User.Organizations.Any(x => x.Id == owner))
            throw new WebAdminException(403, "football_catalog_denied",
                "You do not have permission for this organization.");
        // The database independently checks city/location/team grants.
        return cookie!;
    }

    private static async Task<IResult> Execute(NpgsqlCommand command, CancellationToken ct)
    {
        try
        {
            var json = await command.ExecuteScalarAsync(ct) as string;
            return json is null ? Results.Problem(statusCode: 503) :
                Results.Content(json, "application/json", Encoding.UTF8, 200);
        }
        catch (PostgresException e) when (e.SqlState is "42501")
        {
            throw new WebAdminException(403, "football_catalog_denied",
                "The territory or permission is unavailable.");
        }
        catch (PostgresException e) when (e.SqlState is "23505" or "23503" or "23514")
        {
            throw new WebAdminException(409, "football_catalog_conflict",
                "The catalog entry has changed or conflicts with another city, location, group or team.");
        }
        catch (PostgresException e) when (e.SqlState is "22023" or "22P02" or "22007" or "22008" or "22001")
        {
            throw new WebAdminException(422, "football_catalog_invalid",
                "Check the provided names, dates, locations and Google Maps links.");
        }
        catch (PostgresException e) when (e.SqlState is "42883" or "3F000")
        {
            throw new WebAdminException(503, "football_catalog_not_enabled",
                "Football catalog administration has not been enabled in this environment.");
        }
    }
}
