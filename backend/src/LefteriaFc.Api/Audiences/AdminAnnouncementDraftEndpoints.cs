using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;

namespace LefteriaFc.Api.Audiences;

public static class AdminAnnouncementDraftEndpoints
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        NumberHandling=JsonNumberHandling.Strict,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
        MaxDepth=6
    };
    public static IEndpointRouteBuilder MapAdminAnnouncementDrafts(this IEndpointRouteBuilder endpoints)
    {
        var route=endpoints.MapMethods("/api/admin/communications/announcement-drafts",
            ["GET","POST"],async (string? organization_id,WebAdminService admin,
                IAdminAnnouncementDraftStore store,HttpContext context,CancellationToken token)=>
            {
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";
                if(organization_id is null ||
                    !PublicContentService.IsOrganization(organization_id) ||
                    context.Request.Query["organization_id"].Count!=1)
                    throw new WebAdminException(400,"invalid_organization","Select one organization.");
                var cookie=context.Request.Cookies[WebAdminServices.CookieName];
                var session=await admin.CurrentAsync(cookie,token);
                if(session is null)
                    throw new WebAdminException(401,"not_authenticated","Sign in again.");
                // This is an editorial operation, not implicit chat or mobile access.
                var capability=organization_id=="lefteria_fc"?"teams:write":"academy:write";
                if(!AdminPermissions.Has(session.User.Role,capability) ||
                    !session.User.Organizations.Any(o=>o.Id==organization_id))
                    throw new WebAdminException(403,"not_authorized","Draft access denied.");
                if(context.Request.Method=="GET")
                    return Results.Ok(await store.ReadAsync(cookie!,organization_id,token));
                AdminAnnouncementDraftCommand body;
                try
                {
                    using var doc=await JsonDocument.ParseAsync(context.Request.Body,
                        new JsonDocumentOptions{MaxDepth=6},token);
                    if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
                    var keys=new HashSet<string>(StringComparer.Ordinal);
                    foreach(var property in doc.RootElement.EnumerateObject())
                        if(!keys.Add(property.Name))throw new JsonException();
                    body=doc.Deserialize<AdminAnnouncementDraftCommand>(Json)??throw new JsonException();
                }
                catch(JsonException)
                {throw new WebAdminException(400,"invalid_json","Provide a valid announcement draft.");}
                return Results.Ok(await store.CreateAsync(cookie!,organization_id,body,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Communications administration")
            .WithName("AdminAnnouncementDrafts");
        route.Produces<AdminAnnouncementDraftDto>(200)
            .Produces<AdminAnnouncementDraftsDto>(200);
        foreach(var status in new[]{400,401,403,409,413,415,422,503})
            route.Produces<WebAdminError>(status);
        return endpoints;
    }
}
