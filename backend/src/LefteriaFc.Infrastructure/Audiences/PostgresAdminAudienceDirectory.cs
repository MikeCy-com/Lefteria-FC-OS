using System.Text.Json;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.Audiences;

/// <summary>Admin-auth role EXECUTEs exactly one reviewed roster projection;
/// it never receives direct player, team, membership or private source SELECT.</summary>
public sealed class PostgresAdminAudienceDirectory(
    WebAdminConnections connections, IWebAdminSecrets secrets) : IAdminAudienceDirectory
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AdminAudienceDirectoryDto> ReadAsync(
        string adminCookie, string organizationId, CancellationToken token)
    {
        if (!secrets.IsToken(adminCookie))
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if (organizationId is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400,"invalid_organization","Select an exact organization.");
        try
        {
            await using var connection = await connections.OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "SELECT club.read_admin_audiences(@digest,@owner)::text",connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(adminCookie));
            command.Parameters.AddWithValue("owner",organizationId);
            var raw = await command.ExecuteScalarAsync(token) as string;
            var directory = raw is null ? null : JsonSerializer.Deserialize<AdminAudienceDirectoryDto>(raw,Json);
            if (directory is null || directory.OrganizationId != organizationId ||
                !directory.Ready || directory.Groups is null ||
                directory.Groups.Count>150 || directory.UnassignedPlayers<0 ||
                directory.Groups.Any(g => g.Id.Length>128 || g.Name.Length>200 ||
                    g.Kind != (organizationId=="lefteria_fc" ? "team" : "academy_group") ||
                    g.ActivePlayers<0))
                throw new WebAdminException(503,"audiences_unavailable","Roster audiences are not verified.");
            return directory;
        }
        catch (PostgresException e) when (e.SqlState == "P0001" &&
            e.MessageText=="admin_club_session_lock_missing")
        { throw new WebAdminException(401,"not_authenticated","Sign in again."); }
        catch (PostgresException e) when (e.SqlState == "P0001" &&
            e.MessageText=="admin_club_grant_lock_missing")
        { throw new WebAdminException(403,"not_authorized","Organization assignment changed."); }
        catch (PostgresException e) when (e.SqlState=="42501" ||
            (e.SqlState=="P0001" && e.MessageText=="audience_scope_denied"))
        { throw new WebAdminException(403,"not_authorized","Roster directory is not authorized."); }
        catch (PostgresException e) when (e.SqlState is "55000" or "54000" or "57014" or "55P03" or "P0001")
        { throw new WebAdminException(503,"audiences_unavailable","Roster groups are unavailable."); }
    }
}
