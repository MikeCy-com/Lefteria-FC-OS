using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Audiences;

/// <summary>Manager-only First Team source profile, separate from player
/// assignments and coach authorization; SQL function grants default-deny.</summary>
public static class AdminFirstTeamProfileEndpoints
{
    public sealed record ProfileWrite(
        [property:JsonPropertyName("expected_revision")] long ExpectedRevision,
        [property:JsonPropertyName("level")] string Level,
        [property:JsonPropertyName("description")] string Description,
        [property:JsonPropertyName("banner_url")] string BannerUrl);

    public static IEndpointRouteBuilder MapAdminFirstTeamProfiles(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/admin/football/first-team-profiles")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Football administration");
        group.MapGet("/{teamId}", async (string teamId,string? organization_id,
            WebAdminService admin,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Verify(admin,organization_id,context,ct);
            if(!ValidId(teamId))throw new WebAdminException(400,
                "team_profile_invalid","Select a valid First Team record.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT club.read_admin_first_team_profile(@digest,@id)::text",connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("id",teamId);
            return await Execute(command,ct);
        }).WithName("ReadAdminFirstTeamProfile");

        group.MapPut("/{teamId}", async (string teamId,string? organization_id,
            ProfileWrite payload,WebAdminService admin,
            WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Verify(admin,organization_id,context,ct);
            if(!ValidId(teamId)||payload.ExpectedRevision is <0 or >1000000000||
                payload.Level is null || payload.Level.Length is <1 or >100 ||
                payload.Level!=payload.Level.Trim()||
                !ValidText(payload.Description,2000)||
                !ValidText(payload.BannerUrl,2048)||
                payload.Level.Any(char.IsControl)||
                payload.BannerUrl.Length>0&&(
                  !Uri.TryCreate(payload.BannerUrl,UriKind.Absolute,out var banner)||
                  banner.Scheme!=Uri.UriSchemeHttps||banner.UserInfo.Length>0||
                  banner.IsLoopback))
                throw new WebAdminException(422,"team_profile_invalid",
                    "Invalid First Team level, description or banner.");
            var body=JsonSerializer.Serialize(new{
                level=payload.Level,description=payload.Description,
                banner_url=payload.BannerUrl
            });
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT club.write_admin_first_team_profile(@digest,@id,@revision,@body)::text",
                connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("id",teamId);
            command.Parameters.AddWithValue("revision",payload.ExpectedRevision);
            command.Parameters.AddWithValue("body",NpgsqlDbType.Jsonb,body);
            return await Execute(command,ct);
        }).WithName("WriteAdminFirstTeamProfile");
        return app;
    }

    private static bool ValidId(string? id) => id is {Length:>=1 and <=128}&&
        id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-');
    private static bool ValidText(string? s,int max) =>
        s is not null&&s.Length<=max&&!s.Any(char.IsControl);

    private static async Task<string> Verify(WebAdminService admin,
        string? owner,HttpContext context,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(owner!="lefteria_fc"||context.Request.Query["organization_id"].Count!=1)
            throw new WebAdminException(400,"invalid_organization",
                "Select the First Team organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await admin.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(session.User.Role is not ("manager" or "super_admin")||
            !AdminPermissions.Has(session.User.Role,"teams:write")||
            !session.User.Organizations.Any(o=>o.Id==owner))
            throw new WebAdminException(403,"team_profile_denied",
                "First Team profile access denied.");
        return cookie!;
    }

    private static async Task<IResult> Execute(NpgsqlCommand command,CancellationToken ct)
    {
        try{
            var json=await command.ExecuteScalarAsync(ct) as string;
            return json is null?Results.Problem(statusCode:503):
                Results.Content(json,"application/json",Encoding.UTF8,200);
        }
        catch(PostgresException ex) when(ex.SqlState is "23505" or "23514")
        {throw new WebAdminException(409,"team_profile_changed",
            "First Team profile was changed. Reload and try again.");}
        catch(PostgresException ex) when(ex.SqlState is "42501")
        {throw new WebAdminException(403,"team_profile_denied",
            "First Team record unavailable or permission revoked.");}
        catch(PostgresException ex) when(ex.SqlState is "22023")
        {throw new WebAdminException(422,"team_profile_invalid",
            "Invalid First Team profile fields.");}
        catch(PostgresException ex) when(ex.SqlState is "42883" or "3F000")
        {throw new WebAdminException(503,"team_profile_not_enabled",
            "First Team profile editing is not yet enabled.");}
    }
}
