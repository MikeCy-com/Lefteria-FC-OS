using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Football;

/// <summary>
/// Explicit delegated territory grants for active, organization-authorized
/// manager/coach/staff accounts. Only super admins may grant or revoke.
/// Also exposes aggregate roster counts scoped by current database grants.
/// </summary>
public static class AdminFootballTerritoryAccessEndpoints
{
    public sealed record GrantCommand(
        [property: JsonPropertyName("admin_account_id")] Guid AdminAccountId,
        [property: JsonPropertyName("scope_id")] Guid ScopeId,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("enabled")] bool Enabled);

    public static IEndpointRouteBuilder MapAdminFootballTerritoryAccess(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/football/territories")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Football territory access");

        group.MapGet("/access", async (
            string? organization_id, WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await AdminFootballTerritoryEndpoints.Verify(
                auth, organization_id, context, false, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT club.read_admin_football_access(@digest,@owner)::text",connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", organization_id!);
            return await AdminFootballTerritoryEndpoints.Execute(command,ct);
        }).WithName("ReadAdminFootballTerritoryAccess")
          .ExcludeFromDescription();

        group.MapPut("/access", async (
            string? organization_id, GrantCommand input, WebAdminService auth,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            var cookie = await AdminFootballTerritoryEndpoints.Verify(
                auth, organization_id, context, true, ct);
            if (input.AdminAccountId == Guid.Empty || input.ScopeId == Guid.Empty ||
                input.Kind is not ("city" or "location" or "team") ||
                input.Role is not ("city_manager" or "location_manager" or
                    "team_manager" or "coach" or "staff"))
                throw new WebAdminException(422, "football_grant_invalid",
                    "Select a valid account, territory and access role.");

            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT club.write_admin_football_access(" +
                "@digest,@owner,@target,@kind,@scope,@role,@enabled)::text",connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", organization_id!);
            command.Parameters.AddWithValue("target", input.AdminAccountId);
            command.Parameters.AddWithValue("kind", input.Kind);
            command.Parameters.AddWithValue("scope", input.ScopeId);
            command.Parameters.AddWithValue("role", input.Role);
            command.Parameters.AddWithValue("enabled", input.Enabled);
            return await AdminFootballTerritoryEndpoints.Execute(command,ct);
        }).WithName("WriteAdminFootballTerritoryAccess")
          .Accepts<GrantCommand>("application/json")
          .ExcludeFromDescription();

        group.MapGet("/metrics", async (
            string? organization_id, WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await AdminFootballTerritoryEndpoints.Verify(
                auth, organization_id, context, false, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT club.read_admin_football_location_metrics(@digest,@owner)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", organization_id!);
            return await AdminFootballTerritoryEndpoints.Execute(command,ct);
        }).WithName("ReadAdminFootballTerritoryMetrics")
          .ExcludeFromDescription();

        return endpoints;
    }
}
