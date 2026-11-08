using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.Audiences;

/// <summary>Only a scoped, locked SECURITY DEFINER projection. The isolated
/// web-admin role receives no raw SELECT privilege on private staff tables.</summary>
public sealed class PostgresAdminCoachDirectory(
    WebAdminConnections connections,IWebAdminSecrets secrets):IAdminCoachDirectory
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AdminCoachPageDto> ReadAsync(string cookie,string owner,
        int offset,int limit,CancellationToken token)
    {
        if(!secrets.IsToken(cookie))
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400,"invalid_organization","Select an organization.");
        if(offset is <0 or >20000 || limit is <1 or >50)
            throw new WebAdminException(400,"invalid_filters","Invalid coach paging.");
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_coaches(@digest,@owner,@offset,@limit)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("offset",offset);
            command.Parameters.AddWithValue("limit",limit);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var saved=raw is null?null:JsonSerializer.Deserialize<AdminCoachPageDto>(raw,Json);
            if(saved is null || saved.OrganizationId!=owner || !saved.Ready ||
                saved.Offset!=offset || saved.Limit!=limit || saved.Total is <0 or >25000 ||
                saved.Coaches is null || saved.Coaches.Count>limit ||
                saved.Coaches.Count>saved.Total ||
                saved.Coaches.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()!=saved.Coaches.Count ||
                saved.Coaches.Any(c=>c.Id is null or {Length:0 or >128} ||
                    c.Name is null or {Length:0 or >200} ||
                    c.JobRole is not ("Head Coach" or "Assistant Coach" or
                        "Goalkeeper Coach" or "Fitness Coach" or "Youth Coach") ||
                    c.GroupIds is null || c.GroupIds.Count>150 ||
                    c.GroupIds.Any(id=>id is null or {Length:0 or >128}) ||
                    c.GroupIds.Distinct(StringComparer.Ordinal).Count()!=c.GroupIds.Count))
                throw new WebAdminException(503,"coach_directory_unavailable",
                    "Coach directory failed verification.");
            return saved;
        }
        catch(PostgresException e) when(e.SqlState=="P0001" &&
            e.MessageText=="admin_club_session_lock_missing")
        {throw new WebAdminException(401,"not_authenticated","Sign in again.");}
        catch(PostgresException e) when(e.SqlState=="P0001" &&
            e.MessageText=="admin_club_grant_lock_missing")
        {throw new WebAdminException(403,"not_authorized","Organization access changed.");}
        catch(PostgresException e) when(e.SqlState=="42501")
        {throw new WebAdminException(403,"not_authorized","Coach directory access denied.");}
        catch(PostgresException e) when(e.SqlState=="22023")
        {throw new WebAdminException(400,"invalid_filters","Invalid coach paging.");}
        catch(PostgresException e) when(e.SqlState is "55000" or "54000" or "57014" or "55P03" or "P0001")
        {throw new WebAdminException(503,"coach_directory_unavailable","Coach directory is unavailable.");}
    }
}
