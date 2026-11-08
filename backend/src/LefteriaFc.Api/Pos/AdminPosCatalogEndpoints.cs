using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.Pos;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;

namespace LefteriaFc.Api.Pos;

/// <summary>
/// No POS operators, register tokens, checkout, sales or mutation routes.
/// </summary>
public static class AdminPosCatalogEndpoints
{
    public static IEndpointRouteBuilder MapAdminPosCatalog(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/pos/catalog",
            async (string? organization_id, WebAdminService admin,
                   IAdminPosCatalogReader repository, HttpContext context, CancellationToken cancellationToken) =>
            {
                if (organization_id is null || !PublicContentService.IsOrganization(organization_id))
                    return Results.Json(new WebAdminError("Select an exact organization.", "invalid_organization"),
                        statusCode: 400);
                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, cancellationToken);
                if (session is null)
                    return Results.Json(new WebAdminError("Sign in again.", "not_authenticated"), statusCode: 401);
                if (!AdminPermissions.Has(session.User.Role, "shop:read") ||
                    !session.User.Organizations.Any(org => org.Id == organization_id))
                    return Results.Json(new WebAdminError("Access denied.", "not_authorized"), statusCode: 403);
                return Results.Ok(await repository.ReadAsync(cookie!, organization_id, cancellationToken));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithName("GetAdminPosCatalog")
            .WithTags("POS administration")
            .Produces<AdminPosCatalogDto>(200)
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(503);
        return endpoints;
    }
}
