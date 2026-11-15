using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Evaluations;

/// <summary>
/// Super-admin-only private development goals. Goals belong to the permanent
/// player, never a temporary group/season; SQL revalidates every request.
/// No delegated manager/coach/mobile/guardian access until geographic and actor grants are implemented.
/// </summary>
public static class AdminPlayerDevelopmentGoalsEndpoints
{
    public sealed record CreateGoal(
        Guid RequestId, string Title, string Description, string Category,
        DateOnly? TargetDate, Guid? SourceEvaluationId);

    public sealed record UpdateGoal(
        Guid RequestId, int ExpectedVersion, int Progress,
        bool Archive, string Note);

    public static IEndpointRouteBuilder MapAdminPlayerDevelopmentGoals(
        this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/club/players/{playerId}/development-goals")
            .WithTags("Private player development goals")
            .RequireAuthorization(WebAdminServices.Policy)
            .ExcludeFromDescription();

        admin.MapGet("/", async (
            string playerId, string? organization_id,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await VerifyAsync(playerId, organization_id, false,
                auth, context, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT club.read_admin_player_development_goals(
                  @digest,@owner,@player)::text
                """, connection);
            AddScope(command, cookie, organization_id!, playerId, secrets);
            return await ExecuteAsync(command, ct);
        }).WithName("ReadAdminPlayerDevelopmentGoals");

        admin.MapPost("/", async (
            string playerId, string? organization_id, CreateGoal body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            if (body.RequestId == Guid.Empty ||
                string.IsNullOrWhiteSpace(body.Title) ||
                body.Title != body.Title.Trim() || body.Title.Length > 200 ||
                body.Title.Any(char.IsControl) ||
                body.Description is null || body.Description.Length > 2000 ||
                !ValidText(body.Description) ||
                body.Category is not ("general" or "physical" or "technical" or
                    "tactical" or "mental" or "social") ||
                body.SourceEvaluationId == Guid.Empty)
                throw new WebAdminException(422, "development_goal_payload_invalid",
                    "Enter a valid goal title, category and optional assessment.");

            var cookie = await VerifyAsync(playerId, organization_id, true,
                auth, context, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT club.create_admin_player_development_goal(
                  @digest,@owner,@player,@request,@title,@description,
                  @category,@target,@evaluation)::text
                """, connection);
            AddScope(command, cookie, organization_id!, playerId, secrets);
            command.Parameters.AddWithValue("request", body.RequestId);
            command.Parameters.AddWithValue("title", body.Title);
            command.Parameters.AddWithValue("description", body.Description);
            command.Parameters.AddWithValue("category", body.Category);
            command.Parameters.AddWithValue("target", NpgsqlDbType.Date,
                (object?)body.TargetDate ?? DBNull.Value);
            command.Parameters.AddWithValue("evaluation", NpgsqlDbType.Uuid,
                (object?)body.SourceEvaluationId ?? DBNull.Value);
            return await ExecuteAsync(command, ct, 201);
        }).WithName("CreateAdminPlayerDevelopmentGoal")
            .Accepts<CreateGoal>("application/json");

        admin.MapPatch("/{goalId:guid}", async (
            string playerId, Guid goalId, string? organization_id, UpdateGoal body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            if (goalId == Guid.Empty || body.RequestId == Guid.Empty ||
                body.ExpectedVersion is < 1 or > 9999 ||
                body.Progress is < 0 or > 100 || body.Progress % 10 != 0 ||
                body.Note is null || body.Note.Length > 500 ||
                !ValidText(body.Note))
                throw new WebAdminException(422, "development_goal_progress_invalid",
                    "Progress must be in ten-percent steps with a valid version.");
            var cookie = await VerifyAsync(playerId, organization_id, true,
                auth, context, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT club.update_admin_player_development_goal(
                  @digest,@owner,@player,@goal,@request,
                  @version,@progress,@archive,@note)::text
                """, connection);
            AddScope(command, cookie, organization_id!, playerId, secrets);
            command.Parameters.AddWithValue("goal", goalId);
            command.Parameters.AddWithValue("request", body.RequestId);
            command.Parameters.AddWithValue("version", body.ExpectedVersion);
            command.Parameters.AddWithValue("progress", body.Progress);
            command.Parameters.AddWithValue("archive", body.Archive);
            command.Parameters.AddWithValue("note", body.Note);
            return await ExecuteAsync(command, ct);
        }).WithName("UpdateAdminPlayerDevelopmentGoal")
            .Accepts<UpdateGoal>("application/json");
        return endpoints;
    }

    private static bool ValidText(string value) =>
        value.All(c => !char.IsControl(c) || c is '\n' or '\r' or '\t');

    private static async Task<string> VerifyAsync(
        string playerId, string? owner, bool write,
        WebAdminService auth, HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.Pragma = "no-cache";
        if (owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
            context.Request.Query["organization_id"].Count != 1 ||
            playerId.Length is < 1 or > 128 || playerId.Any(char.IsControl))
            throw new WebAdminException(400, "development_goal_scope_invalid",
                "Select a valid player and organization.");
        var cookie = context.Request.Cookies[WebAdminServices.CookieName];
        var session = await auth.CurrentAsync(cookie, ct);
        if (session is null)
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        var permission = owner == "lefteria_fc"
            ? (write ? "teams:write" : "teams:read")
            : (write ? "academy:write" : "academy:read");
        if (session.User.Role != "super_admin" ||
            !AdminPermissions.Has(session.User.Role, permission) ||
            !session.User.Organizations.Any(o => o.Id == owner))
            throw new WebAdminException(403, "development_goal_access_denied",
                "You cannot access this player's development goals.");
        return cookie!;
    }

    private static void AddScope(
        NpgsqlCommand command, string cookie, string owner, string player,
        IWebAdminSecrets secrets)
    {
        command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("player", player);
    }

    private static async Task<IResult> ExecuteAsync(
        NpgsqlCommand command, CancellationToken ct, int successStatus = 200)
    {
        try
        {
            var json = await command.ExecuteScalarAsync(ct) as string;
            return json is null ? Results.Problem(statusCode: 503)
                : Results.Content(json, "application/json", Encoding.UTF8, successStatus);
        }
        catch (PostgresException error) when (error.SqlState is
            "42501" or "22023" or "23505" or "54000")
        {
            var status = error.SqlState switch
            {
                "42501" => 403,
                "22023" => 422,
                "23505" => 409,
                _ => 413
            };
            throw new WebAdminException(status, "development_goal_unavailable",
                "The goal, player, request or authorization is no longer valid.");
        }
    }
}