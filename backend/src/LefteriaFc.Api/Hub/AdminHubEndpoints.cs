using System.Text.Json;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// Lists limited metadata for actual stored HUB editorial drafts. No private
/// body, media, fan billing data, public publishing or raw table SELECT.
/// A fresh web-admin session and exact org grant are rechecked in SQL.
/// </summary>
public sealed record HubDraftCommand(
    string Kind, string Title, string Body,
    bool AudienceParent, bool AudiencePlayer, short? AgeMin, short? AgeMax);

public static class AdminHubEndpoints
{
    public static IEndpointRouteBuilder MapAdminHub(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/hub/editorial",
            async (string? organization_id, WebAdminService admin, WebAdminConnections connections,
                   IWebAdminSecrets secrets, HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");

                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null) return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    !AdminPermissions.Has(session.User.Role, "news:read"))
                    return Failure(403, "not_authorized");
                if (string.IsNullOrWhiteSpace(cookie) || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");

                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.read_admin_editorial(@digest,@owner)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
                    command.Parameters.AddWithValue("owner", organization_id);
                    var raw = await command.ExecuteScalarAsync(token) as string;
                    if (raw is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(raw);
                    if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                        !parsed.RootElement.TryGetProperty("organization_id", out var owner) ||
                        owner.GetString() != organization_id ||
                        !parsed.RootElement.TryGetProperty("items", out var items) ||
                        items.ValueKind != JsonValueKind.Array ||
                        items.GetArrayLength() > 100)
                        return Failure(503, "hub_unavailable");
                    return Results.Content(raw, "application/json");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("GetAdminHubEditorial")
            .Produces(200, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(503);

        endpoints.MapPost("/api/admin/hub/editorial",
            async (string? organization_id, HubDraftCommand draft, WebAdminService admin,
                   WebAdminConnections connections, IWebAdminSecrets secrets,
                   HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");
                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    !AdminPermissions.Has(session.User.Role, "news:write"))
                    return Failure(403, "not_authorized");

                if (draft.Kind is not ("article" or "drill" or "video" or "visual" or "course") ||
                    string.IsNullOrEmpty(draft.Title) || draft.Title.Length > 180 ||
                    draft.Title != draft.Title.Trim() || draft.Title.Any(char.IsControl) ||
                    string.IsNullOrEmpty(draft.Body) || draft.Body.Length > 20000 ||
                    draft.Body != draft.Body.Trim() || draft.Body.Any(char.IsControl) ||
                    !(draft.AudienceParent || draft.AudiencePlayer) ||
                    (draft.AgeMin is null) != (draft.AgeMax is null) ||
                    (draft.AgeMin is not null && (draft.AgeMin < 4 || draft.AgeMax > 99 || draft.AgeMax < draft.AgeMin)))
                    return Failure(422, "invalid_hub_draft");
                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.create_admin_draft(@digest,@owner,@id,@revision_id,@kind,@title,@body,@parent,@player,@age_min,@age_max)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
                    command.Parameters.AddWithValue("owner", organization_id);
                    var id = Guid.NewGuid();
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("revision_id", Guid.NewGuid());
                    command.Parameters.AddWithValue("kind", draft.Kind);
                    command.Parameters.AddWithValue("title", draft.Title);
                    command.Parameters.AddWithValue("body", draft.Body);
                    command.Parameters.AddWithValue("parent", draft.AudienceParent);
                    command.Parameters.AddWithValue("player", draft.AudiencePlayer);
                    command.Parameters.AddWithValue("age_min", NpgsqlDbType.Smallint,
                        (object?)draft.AgeMin ?? DBNull.Value);
                    command.Parameters.AddWithValue("age_max", NpgsqlDbType.Smallint,
                        (object?)draft.AgeMax ?? DBNull.Value);
                    var raw = await command.ExecuteScalarAsync(token) as string;
                    if (raw is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(raw);
                    var root = parsed.RootElement;
                    if (!root.TryGetProperty("id", out var returnedId) ||
                        returnedId.GetGuid() != id ||
                        !root.TryGetProperty("organization_id", out var owner) ||
                        owner.GetString() != organization_id ||
                        !root.TryGetProperty("status", out var status) ||
                        status.GetString() != "ai_draft")
                        return Failure(503, "hub_unavailable");
                    return Results.Content(raw, "application/json", statusCode: 201);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (PostgresException ex) when (ex.SqlState is "23505" or "23503")
                { return Failure(409, "hub_draft_conflict"); }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                { return Failure(403, "not_authorized"); }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("CreateAdminHubDraft")
            .Produces(201, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(409)
            .Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        return endpoints;
    }

    private static IResult Failure(int status, string code) =>
        Results.Json(new WebAdminError("The requested HUB content is unavailable.", code), statusCode: status);
}