using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;

namespace LefteriaFc.Api.PublicContent;

/// <summary>
/// Admin-only view of already published, safe news projection. No drafts,
/// private source evidence, publishing rights or applicant/guardian data.
/// Every read rechecks a fresh cookie session and exact owner grant.
/// </summary>
public static class AdminNewsEndpoints
{
    public static IEndpointRouteBuilder MapAdminNews(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/website/news",
            async (string? organization_id, int? offset, int? limit,
                   WebAdminService admin, PublicContentService content,
                   HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";

                if (organization_id is null || !PublicContentService.IsOrganization(organization_id))
                    return Error(400, "invalid_organization");
                if (offset is < 0 or > 20000 || limit is < 1 or > 50)
                    return Error(400, "invalid_filters");

                WebAdminSessionDto? session;
                try
                {
                    session = await admin.CurrentAsync(
                        context.Request.Cookies[WebAdminServices.CookieName], token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { return Error(503, "news_unavailable"); }

                if (session is null) return Error(401, "not_authenticated");
                if (!AdminPermissions.Has(session.User.Role, "news:read") ||
                    !session.User.Organizations.Any(org => org.Id == organization_id))
                    return Error(403, "not_authorized");

                try
                {
                    // The public news repository only exposes its allowlisted
                    // published DTO, not imported evidence, edits or source.
                    var articles = await content.GetNewsAsync(organization_id, token);
                    if (articles is null) return Error(503, "news_unavailable");
                    var page = articles.Skip(offset ?? 0).Take(limit ?? 20).ToArray();
                    return Results.Ok(new PublicNewsResult(page, articles.Count, offset ?? 0, limit ?? 20));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { return Error(503, "news_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Website administration")
            .WithName("GetAdminWebsiteNews")
            .Produces<PublicNewsResult>(200)
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(503);

        var articles = endpoints.MapGroup("/api/admin/website/news/{id}")
            .WithTags("Website administration")
            .RequireAuthorization(WebAdminServices.Policy);

        var read = articles.MapGet("", async (
            string id, string? organization_id, IAdminNewsRepository repository,
            HttpContext context, CancellationToken token) =>
        {
            var owner = Organization(organization_id);
            return Results.Ok(await repository.ReadAsync(Session(context), owner, id, token));
        })
        .WithName("GetAdminWebsiteNewsArticle")
        .Produces<AdminNewsArticleDto>(200);
        Errors(read);

        var update = articles.MapPut("", async (
            string id, string? organization_id, IAdminNewsRepository repository,
            HttpContext context, CancellationToken token) =>
        {
            var owner = Organization(organization_id);
            AdminNewsUpdateCommand? command;
            try
            {
                using var document = await JsonDocument.ParseAsync(
                    context.Request.Body, new JsonDocumentOptions { MaxDepth = 32 }, token);
                RejectDuplicateKeys(document.RootElement);
                command = document.Deserialize<AdminNewsUpdateCommand>(
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                        NumberHandling = JsonNumberHandling.Strict,
                        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                        MaxDepth = 32
                    });
            }
            catch (JsonException) { throw AdminNewsEditable.Invalid(); }
            if (command is null) throw AdminNewsEditable.Invalid();
            return Results.Ok(await repository.UpdateAsync(Session(context), owner, id, command, token));
        })
        .WithName("UpdateAdminWebsiteNewsArticle")
        .Accepts<AdminNewsUpdateCommand>("application/json")
        .Produces<AdminNewsArticleDto>(200);
        Errors(update);

        return endpoints;
    }

    private static void Errors(RouteHandlerBuilder endpoint)
    {
        foreach (var status in new[] { 400, 401, 403, 404, 409, 413, 415, 422, 503 })
            endpoint.Produces<WebAdminError>(status);
    }

    private static string Session(HttpContext context) =>
        context.Request.Cookies[WebAdminServices.CookieName] ?? "";

    private static string Organization(string? owner) =>
        owner is not null && PublicContentService.IsOrganization(owner)
            ? owner : throw new WebAdminException(400, "invalid_organization", "Choose a known organization.");

    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new JsonException();
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateKeys(child);
    }

    private static IResult Error(int status, string code) =>
        Results.Json(new WebAdminError("The requested website news is unavailable.", code), statusCode: status);
}
