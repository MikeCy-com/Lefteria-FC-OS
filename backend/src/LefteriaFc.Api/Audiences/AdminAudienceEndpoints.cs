using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;

namespace LefteriaFc.Api.Audiences;

/// <summary>Read-only dynamic team/Academy audience selector. Does not
/// grant access to private chat, child details, sending or notifications.</summary>
public static class AdminAudienceEndpoints
{
    public static IEndpointRouteBuilder MapAdminAudiences(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/football/audiences",
            async (string? organization_id, WebAdminService admin,
                IAdminAudienceDirectory directory, HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null || !PublicContentService.IsOrganization(organization_id))
                    return Results.Json(new WebAdminError("Select one organization.","invalid_organization"),statusCode:400);
                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie,token);
                if (session is null)
                    return Results.Json(new WebAdminError("Sign in again.","not_authenticated"),statusCode:401);
                var capability = organization_id=="lefteria_fc" ? "teams:read" : "academy:read";
                if (!AdminPermissions.Has(session.User.Role,capability) ||
                    !session.User.Organizations.Any(org => org.Id==organization_id))
                    return Results.Json(new WebAdminError("Access denied.","not_authorized"),statusCode:403);
                return Results.Ok(await directory.ReadAsync(cookie!,organization_id,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Football administration")
            .WithName("GetAdminAudienceDirectory")
            .Produces<AdminAudienceDirectoryDto>(200)
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(503);
        return endpoints;
    }
}
