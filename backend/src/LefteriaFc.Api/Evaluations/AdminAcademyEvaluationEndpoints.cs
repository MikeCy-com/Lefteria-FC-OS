using System.Text;
using System.Text.Json;
using NpgsqlTypes;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Evaluations;

/// <summary>Private, read-only Academy evaluations for verified managers.</summary>
public static class AdminAcademyEvaluationEndpoints
{
    public sealed record CreateEvaluationCommand(
        Guid RequestId,string? Summary,string? Notes,
        Dictionary<string,int>? Technical,Dictionary<string,int>? Tactical,
        Dictionary<string,int>? Physical,Dictionary<string,int>? Character,
        Dictionary<string,string>? Overall);
    private static bool ValidScores(Dictionary<string,int>? scores) =>
        scores is not null && scores.Count<=40 &&
        scores.All(x=>x.Key.Length is >=1 and <=50 && x.Value is >=0 and <=100);

    public static IEndpointRouteBuilder MapAdminAcademyEvaluations(
        this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/club/players")
            .WithTags("Academy evaluations")
            .RequireAuthorization(WebAdminServices.Policy);
        admin.MapGet("/{playerId}/evaluations", async (
            string playerId, string? organization_id,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            if (organization_id is not ("lefteria_fc" or "lefteria_fc_academy") ||
                context.Request.Query["organization_id"].Count != 1 ||
                playerId.Length is < 1 or > 128 ||
                playerId.Any(char.IsControl))
                throw new WebAdminException(400, "academy_evaluation_scope_invalid",
                    "Choose a valid assigned player.");
            var cookie = context.Request.Cookies[WebAdminServices.CookieName];
            var session = await auth.CurrentAsync(cookie, ct);
            if (session is null)
                throw new WebAdminException(401, "not_authenticated", "Sign in again.");
            if (session.User.Role is not ("manager" or "super_admin") ||
                !AdminPermissions.Has(session.User.Role, organization_id == "lefteria_fc" ? "teams:read" : "academy:read") ||
                !session.User.Organizations.Any(o => o.Id == organization_id))
                throw new WebAdminException(403, "academy_evaluation_access_denied",
                    "Only authorized club management may see evaluations.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT academy_private.read_admin_player_evaluations(@digest,@owner,@player)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
            command.Parameters.AddWithValue("owner", organization_id);
            command.Parameters.AddWithValue("player", playerId);
            try
            {
                var json = await command.ExecuteScalarAsync(ct) as string;
                return json is null ? Results.Problem(statusCode:503)
                    : Results.Content(json, "application/json", Encoding.UTF8, 200);
            }
            catch (PostgresException e) when (e.SqlState is
                "42501" or "22023" or "54000")
            {
                var status = e.SqlState switch
                {
                    "42501" => 403, "22023" => 422, _ => 413
                };
                throw new WebAdminException(status, "academy_evaluation_unavailable",
                    "The player or evaluation timeline is unavailable for this account.");
            }
        }).WithName("ReadAdminAcademyPlayerEvaluations");
        admin.MapPost("/{playerId}/evaluations", async (
            string playerId, string? organization_id, CreateEvaluationCommand body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            if (organization_id is not ("lefteria_fc" or "lefteria_fc_academy") ||
                context.Request.Query["organization_id"].Count != 1 ||
                playerId.Length is <1 or >128 || playerId.Any(char.IsControl))
                throw new WebAdminException(400,"evaluation_scope_invalid",
                    "Select a valid player and organization.");
            if (body.RequestId==Guid.Empty || string.IsNullOrWhiteSpace(body.Summary) ||
                body.Summary.Length>500 || body.Summary!=body.Summary.Trim() ||
                body.Notes?.Length>4000 ||
                !ValidScores(body.Technical) || !ValidScores(body.Tactical) ||
                !ValidScores(body.Physical) || !ValidScores(body.Character) ||
                body.Overall is null || body.Overall.Count>6 ||
                body.Overall.Any(x=>x.Key.Length is <1 or >50 || x.Value is null ||
                    x.Value.Length>1000))
                throw new WebAdminException(422,"evaluation_payload_invalid",
                    "Provide valid scores, notes and a request identifier.");
            var cookie=context.Request.Cookies[WebAdminServices.CookieName];
            var session=await auth.CurrentAsync(cookie,ct);
            if(session is null)
                throw new WebAdminException(401,"not_authenticated","Sign in again.");
            if(session.User.Role is not ("manager" or "super_admin") ||
                !AdminPermissions.Has(session.User.Role,
                    organization_id=="lefteria_fc"?"teams:write":"academy:write") ||
                !session.User.Organizations.Any(o=>o.Id==organization_id))
                throw new WebAdminException(403,"evaluation_write_denied",
                    "Only authorized club managers may write evaluations.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT academy_private.create_admin_player_evaluation(
                  @digest,@owner,@player,@request,@summary,@notes,@technical,
                  @tactical,@physical,@character,@overall)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie!));
            command.Parameters.AddWithValue("owner",organization_id);
            command.Parameters.AddWithValue("player",playerId);
            command.Parameters.AddWithValue("request",body.RequestId);
            command.Parameters.AddWithValue("summary",body.Summary!);
            command.Parameters.AddWithValue("notes",body.Notes??"");
            command.Parameters.AddWithValue("technical",NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(body.Technical));
            command.Parameters.AddWithValue("tactical",NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(body.Tactical));
            command.Parameters.AddWithValue("physical",NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(body.Physical));
            command.Parameters.AddWithValue("character",NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(body.Character));
            command.Parameters.AddWithValue("overall",NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(body.Overall));
            try
            {
                var json=await command.ExecuteScalarAsync(ct) as string;
                return json is null ? Results.Problem(statusCode:503)
                    : Results.Content(json,"application/json",Encoding.UTF8,200);
            }
            catch(PostgresException e) when(e.SqlState is
                "42501" or "22023" or "23505" or "54000")
            {
                var status=e.SqlState switch
                {
                    "42501"=>403,"22023"=>422,"23505"=>409,_=>413
                };
                throw new WebAdminException(status,"evaluation_create_unavailable",
                    "The player, request or authorization is no longer valid.");
            }
        }).WithName("CreateAdminPlayerEvaluation")
          .Accepts<CreateEvaluationCommand>("application/json");
        return endpoints;
    }
}
