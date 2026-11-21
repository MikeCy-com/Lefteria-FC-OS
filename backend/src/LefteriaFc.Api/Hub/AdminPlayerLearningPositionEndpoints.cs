using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Hub;

public sealed record AdminPlayerLearningPositionChange(
    [property:JsonPropertyName("expected_roster_position")] string ExpectedRosterPosition,
    [property:JsonPropertyName("expected_version")] long ExpectedVersion,
    [property:JsonPropertyName("specialization")] string Specialization,
    [property:JsonPropertyName("reason")] string Reason);

/// <summary>
/// Independent, supervised approval of football learning specialization.
/// Does not modify the imported roster or bypass quiz publication review.
/// </summary>
public static class AdminPlayerLearningPositionEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlayerLearningPositions(
        this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/admin/hub/players/{playerId}/learning-position")
            .WithTags("HUB administration")
            .RequireAuthorization(WebAdminServices.Policy)
            .ExcludeFromDescription();

        group.MapGet("",async(string playerId,string? organization_id,
            HttpContext context,WebAdminService admin,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token)=>
        {
            NoStore(context);
            var auth=await AuthorizeAsync(playerId,organization_id,context,admin,secrets,token);
            if(auth.Error is { } error)return error;
            return await ExecuteAsync("read",auth.Digest!,organization_id!,playerId,
                null,connections,token);
        });

        group.MapPut("",async(string playerId,string? organization_id,
            AdminPlayerLearningPositionChange body,HttpContext context,
            WebAdminService admin,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token)=>
        {
            NoStore(context);
            var auth=await AuthorizeAsync(playerId,organization_id,context,admin,secrets,token);
            if(auth.Error is { } error)return error;
            if(body is null || body.ExpectedVersion<0 || body.ExpectedVersion>=long.MaxValue-1 ||
               body.ExpectedRosterPosition is not ("Goalkeeper" or "Defender" or "Midfielder" or "Forward") ||
               body.Specialization is not ("all" or "goalkeeper" or "defender" or "fullback" or
                   "midfielder" or "winger" or "striker") ||
               body.Reason is null || body.Reason.Length is <8 or >250 ||
               body.Reason!=body.Reason.Trim() || body.Reason.Any(char.IsControl))
                return Failure(422,"invalid_learning_position");
            return await ExecuteAsync("set",auth.Digest!,organization_id!,playerId,
                body,connections,token);
        });
        return endpoints;
    }

    private static async Task<(string? Digest,IResult? Error)> AuthorizeAsync(
        string? playerId,string? organization,HttpContext context,
        WebAdminService admin,IWebAdminSecrets secrets,CancellationToken token)
    {
        if(organization is null || !PublicContentService.IsOrganization(organization) ||
           context.Request.Query["organization_id"].Count!=1 ||
           playerId is null || playerId.Length is <1 or >128 ||
           playerId.Any(char.IsControl))
            return (null,Failure(400,"invalid_learning_scope"));
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await admin.CurrentAsync(cookie,token);
        if(session is null || !secrets.IsToken(cookie))
            return (null,Failure(401,"not_authenticated"));
        if(session.User.Role!="super_admin" ||
           !session.User.Organizations.Any(o=>o.Id==organization) ||
           !AdminPermissions.Has(session.User.Role,"news:write"))
            return (null,Failure(403,"not_authorized"));
        return (secrets.TokenDigest(cookie!),null);
    }

    private static async Task<IResult> ExecuteAsync(
        string action,string digest,string owner,string player,
        AdminPlayerLearningPositionChange? body,
        WebAdminConnections connections,CancellationToken token)
    {
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var cmd=new NpgsqlCommand(action=="read"
                ? "SELECT hub.read_admin_player_learning_position(@digest,@owner,@player)::text"
                : "SELECT hub.set_admin_player_learning_position(@digest,@owner,@player,@expected_roster,@expected_version,@next,@reason)::text",
                connection);
            cmd.Parameters.AddWithValue("digest",digest);
            cmd.Parameters.AddWithValue("owner",owner);
            cmd.Parameters.AddWithValue("player",player);
            if(body is not null){
                cmd.Parameters.AddWithValue("expected_roster",body.ExpectedRosterPosition);
                cmd.Parameters.AddWithValue("expected_version",body.ExpectedVersion);
                cmd.Parameters.AddWithValue("next",body.Specialization);
                cmd.Parameters.AddWithValue("reason",body.Reason);
            }
            var raw=await cmd.ExecuteScalarAsync(token) as string;
            if(raw is null)return Failure(503,"learning_position_unavailable");
            using var parsed=JsonDocument.Parse(raw);
            var result=parsed.RootElement;
            if(result.ValueKind!=JsonValueKind.Object ||
               !result.TryGetProperty("organization_id",out var org) ||
               org.GetString()!=owner ||
               !result.TryGetProperty("player_id",out var id) ||
               id.GetString()!=player ||
               !result.TryGetProperty("version",out var ver) ||
               !ver.TryGetInt64(out var version) || version<0 ||
               !result.TryGetProperty("roster_position",out var roster) ||
               roster.ValueKind!=JsonValueKind.String ||
               roster.GetString() is not ("Goalkeeper" or "Defender" or "Midfielder" or "Forward") ||
               !result.TryGetProperty("needs_reverification",out var stale) ||
               stale.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Failure(503,"learning_position_unavailable");
            return Results.Content(raw,"application/json",statusCode:200);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(PostgresException ex)
        {
            return ex.SqlState switch {
                "42501"=>Failure(403,"not_authorized"),
                "P0002"=>Failure(404,"learning_player_not_found"),
                "40001"=>Failure(409,"learning_position_changed"),
                "22023" or "23514"=>Failure(422,"invalid_learning_position"),
                _=>Failure(503,"learning_position_unavailable")
            };
        }
        catch(Exception){return Failure(503,"learning_position_unavailable");}
    }

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
    }
    private static IResult Failure(int status,string code)=>
        Results.Json(new WebAdminError("Learning position operation unavailable.",code),
            statusCode:status);
}
