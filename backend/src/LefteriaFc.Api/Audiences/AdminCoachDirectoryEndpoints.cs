using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;

namespace LefteriaFc.Api.Audiences;

/// <summary>Manager-only, paged coach directory for organization-scoped
/// assignment management. Read-only; coach permissions are NOT extended.</summary>
public static class AdminCoachDirectoryEndpoints
{
    public static IEndpointRouteBuilder MapAdminCoaches(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/football/coaches",
            async (string? organization_id,int? offset,int? limit,
                WebAdminService admin,IAdminCoachDirectory directory,
                HttpContext context,CancellationToken token) =>
            {
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";
                if(organization_id is null || !PublicContentService.IsOrganization(organization_id) ||
                    context.Request.Query["organization_id"].Count!=1)
                    return Results.Json(new WebAdminError("Select exactly one organization.",
                        "invalid_organization"),statusCode:400);
                if(context.Request.Query["offset"].Count>1 ||
                    context.Request.Query["limit"].Count>1 ||
                    offset is <0 or >20000 || limit is <1 or >50)
                    return Results.Json(new WebAdminError("Invalid coach paging.",
                        "invalid_filters"),statusCode:400);
                var cookie=context.Request.Cookies[WebAdminServices.CookieName];
                var session=await admin.CurrentAsync(cookie,token);
                if(session is null)
                    return Results.Json(new WebAdminError("Sign in again.",
                        "not_authenticated"),statusCode:401);
                var capability=organization_id=="lefteria_fc"?"teams:write":"academy:write";
                if(!AdminPermissions.Has(session.User.Role,capability) ||
                    !session.User.Organizations.Any(o=>o.Id==organization_id))
                    return Results.Json(new WebAdminError("Coach directory access denied.",
                        "not_authorized"),statusCode:403);
                return Results.Ok(await directory.ReadAsync(
                    cookie!,organization_id,offset??0,limit??20,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Football administration")
            .WithName("GetAdminCoaches")
            .Produces<AdminCoachPageDto>(200)
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(503);
        return endpoints;
    }
}
