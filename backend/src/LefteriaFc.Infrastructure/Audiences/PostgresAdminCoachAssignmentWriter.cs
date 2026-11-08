using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Infrastructure.Audiences;

/// <summary>Only exact scoped, audited coach assignment SQL. The web-admin
/// role cannot directly modify staff/team/Academy relationship tables.</summary>
public sealed class PostgresAdminCoachAssignmentWriter(
    WebAdminConnections connections,IWebAdminSecrets secrets):IAdminCoachAssignmentWriter
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AdminCoachAssignmentDto> SaveAsync(
        string cookie,string owner,AdminCoachAssignmentCommand request,CancellationToken token)
    {
        if(!secrets.IsToken(cookie))
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400,"invalid_organization","Select an organization.");
        static bool ValidIds(string[]? ids) =>
            ids is {Length:<=20} &&
            ids.All(id=>id is {Length:>=1 and <=128} &&
                id.All(c=>char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) &&
            ids.Distinct(StringComparer.Ordinal).Count()==ids.Length;
        if(request.StaffId is not {Length:>=1 and <=128} ||
            !request.StaffId.All(c=>char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ||
            !ValidIds(request.ExpectedGroupIds) || !ValidIds(request.GroupIds))
            throw new WebAdminException(422,"invalid_coach_assignment","Invalid coach assignment.");

        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.write_admin_coach_assignment(
                  @digest,@owner,@staff,@expected,@desired)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("staff",request.StaffId);
            command.Parameters.Add("expected",NpgsqlDbType.Jsonb).Value=
                JsonSerializer.Serialize(request.ExpectedGroupIds);
            command.Parameters.Add("desired",NpgsqlDbType.Jsonb).Value=
                JsonSerializer.Serialize(request.GroupIds);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var saved=raw is null?null:JsonSerializer.Deserialize<AdminCoachAssignmentDto>(raw,Json);
            if(saved is null || saved.OrganizationId!=owner || saved.StaffId!=request.StaffId ||
                saved.GroupIds is null || saved.GroupIds.Count!=request.GroupIds!.Length ||
                !saved.GroupIds.Order(StringComparer.Ordinal)
                    .SequenceEqual(request.GroupIds.Order(StringComparer.Ordinal)))
                throw new WebAdminException(503,"coach_assignment_unavailable",
                    "Saved coach assignment could not be verified.");
            return saved;
        }
        catch(PostgresException e) when(e.SqlState=="P0001" &&
            e.MessageText=="admin_club_session_lock_missing")
        {throw new WebAdminException(401,"not_authenticated","Sign in again.");}
        catch(PostgresException e) when(e.SqlState=="P0001" &&
            e.MessageText=="admin_club_grant_lock_missing")
        {throw new WebAdminException(403,"not_authorized","Organization access changed.");}
        catch(PostgresException e) when(e.SqlState=="42501")
        {throw new WebAdminException(403,"not_authorized","Coach assignment denied.");}
        catch(PostgresException e) when(e.SqlState=="40001" || e.SqlState=="23505")
        {throw new WebAdminException(409,"coach_assignment_conflict","Coach groups changed. Refresh and retry.");}
        catch(PostgresException e) when(e.SqlState=="22023" || e.SqlState=="23503")
        {throw new WebAdminException(422,"invalid_coach_assignment","Invalid coach or group.");}
        catch(PostgresException e) when(e.SqlState is "55000" or "57014" or "55P03" or "P0001")
        {throw new WebAdminException(503,"coach_assignment_unavailable","Coach assignments are unavailable.");}
    }
}
