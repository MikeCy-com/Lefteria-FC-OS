using System.Text;
using System.Text.Json;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Audiences;

/// <summary>
/// Authenticated Academy-only original group profile metadata.
/// Protected roster membership and coach assignments remain separate.
/// </summary>
public static class AdminAcademyGroupProfileEndpoints
{
    public sealed record ProfileWrite(
        long ExpectedRevision, string AgeRange, string CoachName,
        string TrainingSchedule, string Description, int MaxPlayers,
        string Season, string BannerUrl, int DisplayOrder);

    public static IEndpointRouteBuilder MapAdminAcademyGroupProfiles(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/admin/football/academy-group-profiles")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Academy administration");
        group.MapGet("/{groupId}", async (
            string groupId, string? organization_id, WebAdminService admin,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            var cookie=await VerifyAdmin(admin,organization_id,context,ct);
            if (!ValidGroup(groupId)) throw new WebAdminException(
                400,"academy_profile_invalid","Select a valid Academy group.");
            await using var conn=await connections.OpenAsync(ct);
            await using var cmd=new NpgsqlCommand(
                "SELECT club.read_admin_academy_group_profile(@digest,@id)::text",conn);
            cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("id",groupId);
            return await Execute(cmd,ct);
        }).WithName("ReadAdminAcademyGroupProfile");

        group.MapPut("/{groupId}", async (
            string groupId, string? organization_id, ProfileWrite data,
            WebAdminService admin, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie=await VerifyAdmin(admin,organization_id,context,ct);
            if (!ValidGroup(groupId) || data.ExpectedRevision is < 0 or > 1000000000 ||
                !ValidText(data.AgeRange,80) || !ValidText(data.CoachName,120) ||
                !ValidText(data.TrainingSchedule,500) ||
                !ValidText(data.Description,2000) ||
                !ValidText(data.Season,40) || !ValidText(data.BannerUrl,2048) ||
                data.MaxPlayers is < 1 or > 200 || data.DisplayOrder is < 0 or > 10000 ||
                (data.BannerUrl.Length>0 && (!Uri.TryCreate(data.BannerUrl,UriKind.Absolute,out var banner) ||
                 banner.Scheme != Uri.UriSchemeHttps || banner.UserInfo.Length>0 ||
                 banner.IsLoopback)))
                throw new WebAdminException(422,"academy_profile_fields_invalid",
                    "Provide valid Academy group profile fields.");

            var body=JsonSerializer.Serialize(new
            {
                age_range=data.AgeRange,coach_name=data.CoachName,
                training_schedule=data.TrainingSchedule,description=data.Description,
                max_players=data.MaxPlayers,season=data.Season,
                banner_url=data.BannerUrl,display_order=data.DisplayOrder
            });
            await using var conn=await connections.OpenAsync(ct);
            await using var cmd=new NpgsqlCommand(
                "SELECT club.write_admin_academy_group_profile(@digest,@id,@revision,@data)::text",conn);
            cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("id",groupId);
            cmd.Parameters.AddWithValue("revision",data.ExpectedRevision);
            cmd.Parameters.AddWithValue("data",NpgsqlDbType.Jsonb,body);
            return await Execute(cmd,ct);
        }).WithName("WriteAdminAcademyGroupProfile");
        return app;
    }

    private static bool ValidGroup(string? id) => id is {Length:>=1 and <=128} &&
        id.All(c=>char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    private static bool ValidText(string? value,int max) =>
        value is not null && value.Length<=max && !value.Any(char.IsControl);

    private static async Task<string> VerifyAdmin(WebAdminService admin,
        string? owner,HttpContext context,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if (owner!="lefteria_fc_academy" ||
            context.Request.Query["organization_id"].Count!=1)
            throw new WebAdminException(400,"invalid_organization","Select the Academy organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await admin.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(session.User.Role is not ("manager" or "super_admin") ||
            !AdminPermissions.Has(session.User.Role,"academy:write") ||
            !session.User.Organizations.Any(x=>x.Id==owner))
            throw new WebAdminException(403,"not_authorized","Academy profile access denied.");
        return cookie!;
    }

    private static async Task<IResult> Execute(NpgsqlCommand cmd,CancellationToken ct)
    {
        try
        {
            var json=await cmd.ExecuteScalarAsync(ct) as string;
            return json is null ? Results.Problem(statusCode:503) :
                Results.Content(json,"application/json",Encoding.UTF8,200);
        }
        catch(PostgresException ex) when(ex.SqlState is "23505" or "23514")
        { throw new WebAdminException(409,"academy_profile_conflict",
            "The group changed or exceeds capacity. Refresh and retry."); }
        catch(PostgresException ex) when(ex.SqlState is "42501")
        { throw new WebAdminException(403,"academy_profile_denied",
            "Academy group is unavailable or access was revoked."); }
        catch(PostgresException ex) when(ex.SqlState is "22023")
        { throw new WebAdminException(422,"academy_profile_invalid",
            "Invalid Academy group details."); }
        catch(PostgresException ex) when(ex.SqlState is "42883" or "3F000")
        { throw new WebAdminException(503,"academy_profile_not_enabled",
            "Academy profile editing is not yet enabled."); }
    }
}
