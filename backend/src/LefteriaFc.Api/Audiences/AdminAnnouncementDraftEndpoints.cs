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
        var get=endpoints.MapGet("/api/admin/communications/announcement-drafts",
            async (string? organization_id,WebAdminService admin,
                IAdminAnnouncementDraftStore store,HttpContext context,CancellationToken token)=>
            {
                var cookie=await AuthorizedCookie(organization_id,admin,context,token);
                return Results.Ok(await store.ReadAsync(cookie,organization_id!,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Communications administration")
            .WithName("GetAdminAnnouncementDrafts")
            .Produces<AdminAnnouncementDraftsDto>(200);
        var post=endpoints.MapPost("/api/admin/communications/announcement-drafts",
            async (string? organization_id,WebAdminService admin,
                IAdminAnnouncementDraftStore store,HttpContext context,CancellationToken token)=>
            {
                var cookie=await AuthorizedCookie(organization_id,admin,context,token);
                return Results.Ok(await store.CreateAsync(cookie,organization_id!,
                    await Parse(context,token),token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Communications administration")
            .WithName("CreateAdminAnnouncementDraft")
            .Accepts<AdminAnnouncementDraftCommand>("application/json")
            .Produces<AdminAnnouncementDraftDto>(200);
        foreach(var status in new[]{400,401,403,409,413,415,422,503})
        {
            get.Produces<WebAdminError>(status);
            post.Produces<WebAdminError>(status);
        }

        var publish=endpoints.MapPost("/api/admin/communications/announcement-drafts/{id}/publish",
            async (string id,string? organization_id,WebAdminService admin,
                IAdminAnnouncementDraftStore store,HttpContext context,CancellationToken token)=>
            {
                var cookie=await AuthorizedCookie(organization_id,admin,context,token);
                var body=await ParsePublish(context,token);
                return Results.Ok(await store.PublishAsync(cookie,organization_id!,
                    id,body.Version,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("App announcements administration")
            .WithName("PublishAdminAppAnnouncement")
            .Accepts<AdminAnnouncementPublishBody>("application/json")
            .Produces<AdminAppAnnouncementDto>(200);
        var published=endpoints.MapGet("/api/admin/communications/app-announcements",
            async (string? organization_id,WebAdminService admin,
                IAdminAnnouncementDraftStore store,HttpContext context,CancellationToken token)=>
            {
                var cookie=await AuthorizedCookie(organization_id,admin,context,token);
                return Results.Ok(await store.ReadPublishedAsync(cookie,organization_id!,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("App announcements administration")
            .WithName("GetAdminAppAnnouncements")
            .Produces<AdminAppAnnouncementsDto>(200);
        var remove=endpoints.MapDelete("/api/admin/communications/app-announcements/{id}",
            async (string id,string? organization_id,string? version,
                WebAdminService admin,IAdminAnnouncementDraftStore store,
                HttpContext context,CancellationToken token)=>
            {
                var cookie=await AuthorizedCookie(organization_id,admin,context,token);
                if(context.Request.Query["version"].Count!=1 ||
                    !long.TryParse(version,System.Globalization.NumberStyles.None,
                      System.Globalization.CultureInfo.InvariantCulture,out var expected)||
                    expected<1)
                    throw new WebAdminException(422,"invalid_app_announcement",
                        "Select the current announcement version.");
                return Results.Ok(await store.RemovePublishedAsync(
                    cookie,organization_id!,id,expected,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("App announcements administration")
            .WithName("RemoveAdminAppAnnouncement")
            .Produces<AdminAppAnnouncementRemovedDto>(200);
        var readers=endpoints.MapGet("/api/admin/communications/app-announcements/{id}/readers",
            async (string id,string? organization_id,WebAdminService admin,
                IAdminAnnouncementDraftStore store,HttpContext context,CancellationToken token)=>
            {
                var cookie=await AuthorizedCookie(organization_id,admin,context,token);
                return Results.Ok(await store.ReadPublishedReadersAsync(
                    cookie,organization_id!,id,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("App announcements administration")
            .WithName("GetAdminAppAnnouncementReaders")
            .Produces<AdminAppAnnouncementReadersDto>(200);
        foreach(var route in new[]{publish,published,remove,readers})
        {
            foreach(var status in new[]{400,401,403,409,413,415,422,503})
                route.Produces<WebAdminError>(status);
        }
        return endpoints;
    }
    private static async Task<string> AuthorizedCookie(string? organization,
        WebAdminService admin,HttpContext context,CancellationToken token)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(organization is null || !PublicContentService.IsOrganization(organization) ||
            context.Request.Query["organization_id"].Count!=1)
            throw new WebAdminException(400,"invalid_organization","Select one organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await admin.CurrentAsync(cookie,token);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        var capability=organization=="lefteria_fc"?"teams:write":"academy:write";
        if(!AdminPermissions.Has(session.User.Role,capability) ||
            !session.User.Organizations.Any(o=>o.Id==organization))
            throw new WebAdminException(403,"not_authorized","Draft access denied.");
        return cookie!;
    }
    private static async Task<AdminAnnouncementPublishBody> ParsePublish(
        HttpContext context,CancellationToken token)
    {
        try
        {
            using var doc=await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions{MaxDepth=4},token);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
            var fields=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in doc.RootElement.EnumerateObject())
                if(!fields.Add(property.Name)||property.Name!="version")
                    throw new JsonException();
            var body=doc.Deserialize<AdminAnnouncementPublishBody>(Json);
            if(body is null||body.Version<1)throw new JsonException();
            return body;
        }
        catch(JsonException)
        {
            throw new WebAdminException(400,"invalid_json",
                "Provide the current draft version for publication.");
        }
    }
    private static async Task<AdminAnnouncementDraftCommand> Parse(
        HttpContext context,CancellationToken token)
    {
        try
        {
            using var doc=await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions{MaxDepth=6},token);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in doc.RootElement.EnumerateObject())
                if(!keys.Add(property.Name))throw new JsonException();
            return doc.Deserialize<AdminAnnouncementDraftCommand>(Json)??throw new JsonException();
        }
        catch(JsonException)
        {throw new WebAdminException(400,"invalid_json","Provide a valid announcement draft.");}
    }
}
