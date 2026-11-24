using System.Text.Json;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.Audiences;

/// <summary>
/// Executes one reviewed security-definer projection using the restricted
/// web-admin DB role. Does not grant the runtime raw staff/roster SELECT.
/// </summary>
public sealed class PostgresAdminCoachAssignedGroupsReader(
    WebAdminConnections connections,IWebAdminSecrets secrets)
    : IAdminCoachAssignedGroupsReader
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AdminCoachAssignedGroupsDto> ReadAsync(
        string adminCookie,string organizationId,CancellationToken token)
    {
        if(!secrets.IsToken(adminCookie))
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(organizationId is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400,"invalid_organization","Select an organization.");
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand(
                "SELECT club.read_admin_coach_assigned_groups(@digest,@owner)::text",connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(adminCookie));
            command.Parameters.AddWithValue("owner",organizationId);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var result=raw is null?null:JsonSerializer.Deserialize<AdminCoachAssignedGroupsDto>(raw,Json);
            if(result is null || result.OrganizationId!=organizationId || !result.Ready ||
                result.Groups is null || result.Groups.Count>150 ||
                result.Groups.Any(g=>g is null || string.IsNullOrWhiteSpace(g.Id) || g.Id.Length>128 ||
                    string.IsNullOrWhiteSpace(g.Name) || g.Name.Length>200 ||
                    g.Kind!=(organizationId=="lefteria_fc"?"team":"academy_group")) ||
                result.Groups.Select(g=>g.Id).Distinct(StringComparer.Ordinal).Count()!=result.Groups.Count)
                throw new WebAdminException(503,"assigned_groups_unavailable",
                    "Assigned group directory could not be verified.");
            return result;
        }
        catch(JsonException)
        { throw new WebAdminException(503,"assigned_groups_unavailable",
            "Assigned groups returned invalid data."); }
        catch(PostgresException e) when (e.SqlState=="P0001" &&
            e.MessageText=="admin_club_session_lock_missing")
        { throw new WebAdminException(401,"not_authenticated","Sign in again."); }
        catch(PostgresException e) when(e.SqlState=="42501" ||
            (e.SqlState=="P0001" && e.MessageText=="admin_club_grant_lock_missing"))
        { throw new WebAdminException(403,"not_authorized","Assigned group access denied."); }
        catch(PostgresException e) when(e.SqlState is "55000" or "54000" or "57014" or "55P03" or "P0001")
        { throw new WebAdminException(503,"assigned_groups_unavailable",
            "Assigned groups are not currently available."); }
    }
}
