using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.WebAdmin;

/// <summary>Explicit human provisioning; not inferred from names or phone
/// contacts and never an implicit login, role or territory grant.</summary>
public sealed record NativeCoachCreateCommand(
    Guid? RequestId,string? OrganizationId,string? Name,
    string? JobRole,string? GroupId,Guid? AccountId);

public static class AdminNativeCoachEndpoints
{
    private static readonly HashSet<string> AllowedRoles=new(StringComparer.Ordinal)
    {
        "Head Coach","Assistant Coach","Goalkeeper Coach","Fitness Coach","Youth Coach"
    };

    public static IEndpointRouteBuilder MapAdminNativeCoach(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/staff/coaches",async(
            NativeCoachCreateCommand request,HttpContext context,
            WebAdminService service,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token)=>
        {
            context.Response.Headers.CacheControl="private, no-store";
            context.Response.Headers.Pragma="no-cache";
            var cookie=context.Request.Cookies[WebAdminServices.CookieName];
            var session=await service.CurrentAsync(cookie,token);
            if(session is null)
                throw new WebAdminException(401,"not_authenticated","Sign in again.");
            if(session.User.Role!="super_admin")
                throw new WebAdminException(403,"not_authorized","Super Admin required.");

            var owner=request.OrganizationId;
            var name=request.Name?.Trim();
            if(owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
                !session.User.Organizations.Any(o=>o.Id==owner))
                throw new WebAdminException(403,"not_authorized",
                    "This organization is not assigned to this administrator.");
            if(request.RequestId is null || request.RequestId==Guid.Empty ||
               name is not {Length:>=2 and <=200} ||
               name.Any(char.IsControl) ||
               !AllowedRoles.Contains(request.JobRole??"") ||
               request.GroupId is null ||
               !Regex.IsMatch(request.GroupId,
                    @"\A[A-Za-z0-9_-]{1,128}\z",RegexOptions.CultureInvariant))
                throw new WebAdminException(422,"native_coach_invalid",
                    "Choose an existing team or Academy group, coach title and valid name.");

            await using var connection=await connections.OpenAsync(token);
            await using var cmd=new NpgsqlCommand("""
                SELECT club.create_admin_native_coach(
                  @digest,@owner,@request,@name,@job,@group,@account)::text
                """,connection);
            cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie!));
            cmd.Parameters.AddWithValue("owner",owner);
            cmd.Parameters.AddWithValue("request",request.RequestId.Value);
            cmd.Parameters.AddWithValue("name",name);
            cmd.Parameters.AddWithValue("job",request.JobRole!);
            cmd.Parameters.AddWithValue("group",request.GroupId);
            cmd.Parameters.Add("account",NpgsqlDbType.Uuid).Value=
                request.AccountId is Guid accountId?(object)accountId:DBNull.Value;
            try
            {
                var raw=await cmd.ExecuteScalarAsync(token) as string;
                if(string.IsNullOrEmpty(raw))
                    throw new WebAdminException(503,"native_coach_unconfirmed",
                        "Coach registration was not confirmed.");
                using var doc=JsonDocument.Parse(raw);
                var saved=doc.RootElement;
                if(saved.GetProperty("organization_id").GetString()!=owner ||
                    saved.GetProperty("request_id").GetGuid()!=request.RequestId ||
                    saved.GetProperty("name").GetString()!=name ||
                    saved.GetProperty("job_role").GetString()!=request.JobRole ||
                    saved.GetProperty("group_id").GetString()!=request.GroupId ||
                    (saved.GetProperty("account_id").ValueKind==JsonValueKind.Null) !=
                        (request.AccountId is null) ||
                    (request.AccountId is Guid desired &&
                        saved.GetProperty("account_id").GetGuid()!=desired) ||
                    !Regex.IsMatch(saved.GetProperty("staff_id").GetString()??"",
                        @"\Astf_admin_[0-9a-f]{32}\z",RegexOptions.CultureInvariant))
                    throw new WebAdminException(503,"native_coach_unconfirmed",
                        "Unexpected coach registration confirmation.");
                return Results.Content(raw,"application/json",statusCode:201);
            }
            catch(PostgresException e) when(e.SqlState=="P0001" &&
                e.MessageText=="admin_club_session_lock_missing")
            {throw new WebAdminException(401,"not_authenticated","Sign in again.");}
            catch(PostgresException e) when(e.SqlState=="P0001" &&
                e.MessageText=="admin_club_grant_lock_missing")
            {throw new WebAdminException(403,"not_authorized","Organization access changed.");}
            catch(PostgresException e) when(e.SqlState=="23505")
            {throw new WebAdminException(409,"native_coach_conflict",
                "This registration was already used with different information.");}
            catch(PostgresException e) when(e.SqlState=="23514")
            {throw new WebAdminException(409,"native_coach_group_unassigned",
                "The group must belong to a verified team or Academy location.");}
            catch(PostgresException e) when(e.SqlState=="42501")
            {throw new WebAdminException(403,"native_coach_denied",
                "Account or coach organization authorization was denied.");}
            catch(PostgresException e) when(e.SqlState is "22023" or "23502" or "23503")
            {throw new WebAdminException(422,"native_coach_invalid",
                "Invalid coach, site, or existing identity reference.");}
            catch(PostgresException e) when(e.SqlState is "55000" or "42883" or "3F000")
            {throw new WebAdminException(503,"native_coach_unavailable",
                "Verified staff registry is not ready or safe provisioning is unavailable.");}
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("CreateAdminNativeCoach")
        .WithTags("Web administration")
        .Accepts<NativeCoachCreateCommand>("application/json")
        .ExcludeFromDescription();
        return endpoints;
    }
}
