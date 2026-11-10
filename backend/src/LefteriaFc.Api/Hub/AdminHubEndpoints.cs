using System.Text.Json;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// Lists limited metadata for actual stored HUB editorial drafts. No private
/// body, media, fan billing data, public publishing or raw table SELECT.
/// A fresh web-admin session and exact org grant are rechecked in SQL.
/// </summary>
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

        return endpoints;
    }

    private static IResult Failure(int status, string code) =>
        Results.Json(new WebAdminError("The requested HUB content is unavailable.", code), statusCode: status);
}
