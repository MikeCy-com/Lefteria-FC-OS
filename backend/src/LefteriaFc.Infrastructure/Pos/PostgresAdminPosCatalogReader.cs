using System.Text.Json;
using LefteriaFc.Application.Pos;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.Pos;

/// <summary>
/// Restricted admin-auth role has EXECUTE on one reviewed definer function
/// only. It has no direct pos table or column privileges, ever.
/// </summary>
public sealed class PostgresAdminPosCatalogReader(WebAdminConnections connections, IWebAdminSecrets secrets)
    : IAdminPosCatalogReader
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AdminPosCatalogDto> ReadAsync(string opaqueCookie, string organizationId, CancellationToken cancellationToken)
    {
        if (!secrets.IsToken(opaqueCookie))
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (organizationId is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400, "invalid_organization", "Choose a known organization.");

        try
        {
            await using var connection = await connections.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT pos.read_admin_catalog(@digest,@organization)::text", connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(opaqueCookie));
            command.Parameters.AddWithValue("organization", organizationId);
            var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
            var result = payload is null ? null : JsonSerializer.Deserialize<AdminPosCatalogDto>(payload, Json);
            if (result is null || result.OrganizationId != organizationId || result.Categories is null || result.Products is null)
                throw new WebAdminException(503, "pos_catalog_unavailable", "The private POS catalog is unavailable.");
            return result;
        }
        catch (PostgresException error) when (error.SqlState is "42501" or "P0001")
        { throw new WebAdminException(403, "not_authorized", "The requested catalog is not authorized."); }
        catch (PostgresException error) when (error.SqlState is "54000" or "57014" or "55P03")
        { throw new WebAdminException(503, "pos_catalog_unavailable", "The private POS catalog is unavailable."); }
    }
}
