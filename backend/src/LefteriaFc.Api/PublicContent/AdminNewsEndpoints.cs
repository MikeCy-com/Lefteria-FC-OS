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

        return endpoints;
    }

    private static IResult Error(int status, string code) =>
        Results.Json(new WebAdminError("The requested website news is unavailable.", code), statusCode: status);
}
