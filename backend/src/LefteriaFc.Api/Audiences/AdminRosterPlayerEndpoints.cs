using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;

namespace LefteriaFc.Api.Audiences;

/// <summary>
/// Manager-only, paginated active roster picker. Display names are private;
/// it is not a public API or a coach's unrestricted player directory.
/// </summary>
public sealed record RosterSearchRequest(string OrganizationId,int Offset,int Limit,string Search,
    string? ExactGroupId=null,string? FilterGroupId=null);

public static class AdminRosterPlayerEndpoints
{
    public static IEndpointRouteBuilder MapAdminRosterPlayers(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/football/players",
            async (string? organization_id,int? offset,int? limit,
                WebAdminService admin,IAdminRosterPlayerDirectory directory,
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
                    return Results.Json(new WebAdminError("Invalid roster paging.",
                        "invalid_filters"),statusCode:400);

                var cookie=context.Request.Cookies[WebAdminServices.CookieName];
                var session=await admin.CurrentAsync(cookie,token);
                if(session is null)
                    return Results.Json(new WebAdminError("Sign in again.",
                        "not_authenticated"),statusCode:401);
                var capability=organization_id=="lefteria_fc"?"teams:write":"academy:write";
                if(!AdminPermissions.Has(session.User.Role,capability) ||
                   !session.User.Organizations.Any(org=>org.Id==organization_id))
                    return Results.Json(new WebAdminError("Roster access denied.",
                        "not_authorized"),statusCode:403);
                return Results.Ok(await directory.ReadAsync(
                    cookie!,organization_id,offset??0,limit??20,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Football administration")
            .WithName("GetAdminRosterPlayers")
            .Produces<AdminRosterPlayersPageDto>(200)
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(503);
        // Group workspace reads its exact own roster. The old organization-wide
        // picker remains available only after explicitly selecting assignment mode.
        endpoints.MapGet("/api/admin/football/group-players",
            async (string? organization_id,string? group_id,int? offset,int? limit,
                WebAdminService admin,IAdminRosterPlayerDirectory directory,
                HttpContext context,CancellationToken token) =>
            {
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";
                if(organization_id is null || !PublicContentService.IsOrganization(organization_id) ||
                    context.Request.Query["organization_id"].Count!=1 ||
                    group_id is not {Length:>0 and <=128} ||
                    group_id.Any(char.IsControl) || context.Request.Query["group_id"].Count!=1)
                    return Results.Json(new WebAdminError("Select one verified group.",
                        "invalid_group_scope"),statusCode:400);
                if(context.Request.Query["offset"].Count>1 ||
                   context.Request.Query["limit"].Count>1 ||
                   offset is <0 or >20000 || limit is <1 or >50)
                    return Results.Json(new WebAdminError("Invalid roster paging.",
                        "invalid_filters"),statusCode:400);
                var cookie=context.Request.Cookies[WebAdminServices.CookieName];
                var session=await admin.CurrentAsync(cookie,token);
                if(session is null)
                    return Results.Json(new WebAdminError("Sign in again.",
                        "not_authenticated"),statusCode:401);
                var capability=organization_id=="lefteria_fc"?"teams:write":"academy:write";
                if(!AdminPermissions.Has(session.User.Role,capability) ||
                   !session.User.Organizations.Any(org=>org.Id==organization_id))
                    return Results.Json(new WebAdminError("Roster access denied.",
                        "not_authorized"),statusCode:403);
                return Results.Ok(await directory.ReadGroupAsync(
                    cookie!,organization_id,group_id,offset??0,limit??20,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Football administration")
            .WithName("GetExactAdminGroupPlayers")
            .ExcludeFromDescription();
        // Use a private JSON body rather than placing a child's name in the
        // URL or HTTP access log. Existing GET contracts remain unchanged.
        endpoints.MapPost("/api/admin/football/players/search",
            async (RosterSearchRequest body,WebAdminService admin,IAdminRosterPlayerDirectory directory,
                HttpContext context,CancellationToken token) =>
            {
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";
                if(body.OrganizationId is not ("lefteria_fc" or "lefteria_fc_academy") ||
                   body.Search is null || body.Search.Length>100 ||
                   body.Search!=body.Search.Trim() || body.Search.Any(char.IsControl) ||
                   body.Offset is <0 or >20000 || body.Limit is <1 or >50 ||
                   (body.ExactGroupId is not null &&
                     (body.ExactGroupId.Length is <1 or >128 || body.ExactGroupId.Any(char.IsControl))) ||
                   (body.FilterGroupId is not null &&
                     (body.FilterGroupId.Length is <1 or >128 || body.FilterGroupId.Any(char.IsControl))) ||
                   (body.ExactGroupId is not null && body.FilterGroupId is not null))
                    return Results.Json(new WebAdminError("Invalid roster search.","invalid_filters"),statusCode:400);
                var cookie=context.Request.Cookies[WebAdminServices.CookieName];
                var session=await admin.CurrentAsync(cookie,token);
                if(session is null)
                    return Results.Json(new WebAdminError("Sign in again.","not_authenticated"),statusCode:401);
                var capability=body.OrganizationId=="lefteria_fc"?"teams:write":"academy:write";
                if(session.User.Role is not ("manager" or "super_admin") ||
                   !AdminPermissions.Has(session.User.Role,capability) ||
                   !session.User.Organizations.Any(org=>org.Id==body.OrganizationId))
                    return Results.Json(new WebAdminError("Roster access denied.","not_authorized"),statusCode:403);
                if(body.ExactGroupId is not null)
                    return Results.Ok(await directory.ReadGroupAsync(cookie!,body.OrganizationId,
                        body.ExactGroupId,body.Offset,body.Limit,token,body.Search));
                return Results.Ok(await directory.ReadAsync(cookie!,body.OrganizationId,body.Offset,
                    body.Limit,token,body.Search,body.FilterGroupId));
            }).RequireAuthorization(WebAdminServices.Policy).WithTags("Football administration")
            .WithName("SearchAdminRosterPlayers").ExcludeFromDescription();
        return endpoints;
    }
}
