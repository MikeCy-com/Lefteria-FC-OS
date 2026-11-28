using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.Audiences;

/// <summary>Only the narrow audited roster projection is executable by
/// admin-auth. The caller cannot SELECT, UPDATE or enumerate raw roster data.</summary>
public sealed class PostgresAdminRosterPlayerDirectory(
    WebAdminConnections connections,IWebAdminSecrets secrets):IAdminRosterPlayerDirectory
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AdminRosterPlayersPageDto> ReadAsync(
        string cookie,string organizationId,int offset,int limit,CancellationToken token,string? search=null,string? filterGroup=null)
    {
        if(!secrets.IsToken(cookie))
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(organizationId is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400,"invalid_organization","Select exactly one organization.");
        if(offset<0 || offset>20000 || limit<1 || limit>50 || !ValidSearch(search) ||
            (filterGroup is not null && (filterGroup.Length is <1 or >128 || filterGroup.Any(char.IsControl))))
            throw new WebAdminException(400,"invalid_filters","The roster page is invalid.");
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand(search is not null || filterGroup is not null ?
                "SELECT club.search_admin_roster_players(@digest,@owner,@offset,@limit,@search,@filter_group)::text" : """
                SELECT club.read_admin_roster_players(
                  @digest,@owner,@offset,@limit)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organizationId);
            command.Parameters.AddWithValue("offset",offset);
            command.Parameters.AddWithValue("limit",limit);
            if(search is not null || filterGroup is not null)
            {
                command.Parameters.AddWithValue("search",search??string.Empty);
                command.Parameters.Add("filter_group",NpgsqlTypes.NpgsqlDbType.Text).Value=(object?)filterGroup??DBNull.Value;
            }
            var raw=await command.ExecuteScalarAsync(token) as string;
            var result=raw is null ? null : JsonSerializer.Deserialize<AdminRosterPlayersPageDto>(raw,Json);
            if(result is null || result.OrganizationId!=organizationId ||
               !result.Ready || result.Offset!=offset || result.Limit!=limit ||
               result.Total<0 || result.Total>25000 || result.Players is null ||
               result.Players.Count>limit || result.Players.Count>result.Total ||
               result.Players.Any(p => p.Id is null or {Length:0 or >128} ||
                   p.Name is null or {Length:0 or >200} ||
                   p.TeamId is {Length:>128} || p.AcademyGroupIds is null ||
                   p.AcademyGroupIds.Count>150 ||
                   p.AcademyGroupIds.Any(id=>id is null or {Length:0 or >128}) ||
                   (organizationId=="lefteria_fc" && p.AcademyGroupIds.Count>0) ||
                   (organizationId=="lefteria_fc_academy" && p.TeamId is not null)))
                throw new WebAdminException(503,"roster_players_unavailable",
                    "The private roster directory failed verification.");
            return result;
        }
        catch(PostgresException e) when(e.SqlState=="P0001" &&
           e.MessageText=="admin_club_session_lock_missing")
        {throw new WebAdminException(401,"not_authenticated","Sign in again.");}
        catch(PostgresException e) when(e.SqlState=="P0001" &&
           e.MessageText=="admin_club_grant_lock_missing")
        {throw new WebAdminException(403,"not_authorized","Organization access changed.");}
        catch(PostgresException e) when(e.SqlState=="42501")
        {throw new WebAdminException(403,"not_authorized","Roster access denied.");}
        catch(PostgresException e) when(e.SqlState=="22023")
        {throw new WebAdminException(400,"invalid_filters","The roster page is invalid.");}
        catch(PostgresException e) when(e.SqlState is "55000" or "54000" or "57014" or "55P03" or "P0001")
        {throw new WebAdminException(503,"roster_players_unavailable","Roster is unavailable.");}
    }
    public async Task<AdminGroupRosterPageDto> ReadGroupAsync(
        string cookie,string organizationId,string groupId,int offset,int limit,CancellationToken token)
    {
        if(!secrets.IsToken(cookie))
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(organizationId is not ("lefteria_fc" or "lefteria_fc_academy") ||
            string.IsNullOrWhiteSpace(groupId) || groupId.Length>128 ||
            groupId.Any(char.IsControl) || offset<0 || offset>20000 ||
            limit<1 || limit>50 || !ValidSearch(search))
            throw new WebAdminException(400,"invalid_group_scope",
                "Select one verified group and valid page.");
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand(search is not null ?
                "SELECT club.search_admin_group_roster(@digest,@owner,@group_id,@offset,@limit,@search)::text" : """
                SELECT club.read_admin_group_roster(
                  @digest,@owner,@group_id,@offset,@limit)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organizationId);
            command.Parameters.AddWithValue("group_id",groupId);
            command.Parameters.AddWithValue("offset",offset);
            command.Parameters.AddWithValue("limit",limit);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var result=raw is null ? null : JsonSerializer.Deserialize<AdminGroupRosterPageDto>(raw,Json);
            if(result is null || result.OrganizationId!=organizationId ||
               result.GroupId!=groupId || !result.Ready ||
               result.Offset!=offset || result.Limit!=limit ||
               result.Total<0 || result.Total>25000 || result.Players is null ||
               result.Players.Count>limit || result.Players.Count>result.Total ||
               result.Players.Any(p => p.Id is null or {Length:0 or >128} ||
                   p.Name is null or {Length:0 or >200} ||
                   p.TeamId is {Length:>128} || p.AcademyGroupIds is null ||
                   p.AcademyGroupIds.Count>150 ||
                   p.AcademyGroupIds.Any(id=>id is null or {Length:0 or >128}) ||
                   (organizationId=="lefteria_fc" &&
                     (p.TeamId!=groupId || p.AcademyGroupIds.Count>0)) ||
                   (organizationId=="lefteria_fc_academy" &&
                     (p.TeamId is not null || !p.AcademyGroupIds.Contains(groupId)))))
                throw new WebAdminException(503,"group_roster_unavailable",
                    "The selected group roster failed verification.");
            return result;
        }
        catch(PostgresException e) when(e.SqlState=="P0001" &&
           e.MessageText=="admin_club_session_lock_missing")
        {throw new WebAdminException(401,"not_authenticated","Sign in again.");}
        catch(PostgresException e) when(e.SqlState=="P0001" &&
           e.MessageText=="admin_club_grant_lock_missing")
        {throw new WebAdminException(403,"not_authorized","Organization access changed.");}
        catch(PostgresException e) when(e.SqlState=="42501")
        {throw new WebAdminException(403,"not_authorized","Roster access denied.");}
        catch(PostgresException e) when(e.SqlState=="22023")
        {throw new WebAdminException(400,"invalid_group_scope","The group is not available in this organization.");}
        catch(PostgresException e) when(e.SqlState is "55000" or "54000" or "57014" or "55P03" or "P0001")
        {throw new WebAdminException(503,"group_roster_unavailable","Roster is unavailable.");}
    }
    private static bool ValidSearch(string? search)=>search is null||
        (search.Length<=100&&search==search.Trim()&&!search.Any(char.IsControl));
}
