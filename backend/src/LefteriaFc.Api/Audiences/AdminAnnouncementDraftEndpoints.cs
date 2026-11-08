using System.Security.Cryptography;
using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
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

        var upload=endpoints.MapPost("/api/admin/communications/announcement-drafts/{id}/media",
            async (string id,string? organization_id,WebAdminService admin,
                IAdminAnnouncementDraftStore store,IAvatarImageSanitizer sanitizer,
                HttpContext context,CancellationToken token)=>
            {
                var cookie=await AuthorizedCookie(organization_id,admin,context,token);
                var media=await ParseMedia(context,sanitizer,token);
                try {
                    return Results.Ok(await store.AddMediaAsync(cookie,organization_id!,
                        id,media.Version,media.Kind,media.FileName,media.ContentType,
                        media.Bytes,token));
                }
                finally {CryptographicOperations.ZeroMemory(media.Bytes);}
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("App announcements administration")
            .WithName("UploadAdminAppAnnouncementMedia")
            .Accepts<AdminAnnouncementMediaUploadBody>("application/json")
            .Produces<AdminAnnouncementMediaMutationDto>(200);
        var removeMedia=endpoints.MapDelete(
            "/api/admin/communications/announcement-drafts/{id}/media/{mediaId:guid}",
            async (string id,Guid mediaId,string? organization_id,WebAdminService admin,
                IAdminAnnouncementDraftStore store,HttpContext context,CancellationToken token)=>
            {
                var cookie=await AuthorizedCookie(organization_id,admin,context,token);
                var body=await ParsePublish(context,token);
                return Results.Ok(await store.RemoveMediaAsync(
                    cookie,organization_id!,id,body.Version,mediaId,token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("App announcements administration")
            .WithName("RemoveAdminAppAnnouncementMedia")
            .Accepts<AdminAnnouncementPublishBody>("application/json")
            .Produces<AdminAnnouncementMediaMutationDto>(200);
        foreach(var route in new[]{upload,removeMedia})
            foreach(var status in new[]{400,401,403,409,413,415,422,429,503})
                route.Produces<WebAdminError>(status);

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
    private sealed record CleanMedia(long Version,string Kind,string FileName,
        string ContentType,byte[] Bytes);
    private static async Task<CleanMedia> ParseMedia(HttpContext context,
        IAvatarImageSanitizer sanitizer,CancellationToken token)
    {
        AdminAnnouncementMediaUploadBody body;
        try {
            using var doc=await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions{MaxDepth=3},token);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var prop in doc.RootElement.EnumerateObject())
                if(!keys.Add(prop.Name)||
                    prop.Name is not ("kind" or "file_name" or "content_type" or "base64" or "version"))
                    throw new JsonException();
            body=doc.Deserialize<AdminAnnouncementMediaUploadBody>(Json)??throw new JsonException();
        }
        catch(JsonException) { throw InvalidMedia(); }
        if(body.Version<1||body.Base64 is null||body.Base64.Length is <8 or >4194304||
           body.Kind is not ("banner" or "attachment"))
            throw InvalidMedia();
        byte[] raw;
        try { raw=Convert.FromBase64String(body.Base64); }
        catch(FormatException){throw InvalidMedia();}
        try
        {
            if(body.Kind=="banner")
            {
                if(body.ContentType is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif")
                    ||raw.Length is <4 or >3145728)throw InvalidMedia();
                try
                {
                    var safe=sanitizer.Sanitize(raw,body.ContentType,token);
                    return new CleanMedia(body.Version,"banner","banner.jpg","image/jpeg",safe.JpegBytes);
                }
                catch(MobileAuthException error)
                {
                    throw new WebAdminException(
                        error.Kind==MobileAuthFaultKind.TooManyRequests?429:
                        error.Kind==MobileAuthFaultKind.ServiceUnavailable?503:422,
                        "invalid_announcement_media","The banner could not be sanitized.");
                }
            }
            if(body.ContentType!="application/pdf"||
                body.FileName is null||body.FileName.Length>96||
                !System.Text.RegularExpressions.Regex.IsMatch(body.FileName,
                    @"\A[A-Za-z0-9][A-Za-z0-9 ._()-]{0,91}\.pdf\z",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant|
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)||
                raw.Length is <8 or >2097152||
                !raw.AsSpan(0,5).SequenceEqual("%PDF-"u8))
                throw InvalidMedia();
            // Tight, unambiguous PDF framing; never inline PDFs or treat
            // this lightweight validation as malware scanning.
            var last=raw.Length;
            while(last>5&&raw[last-1] is 9 or 10 or 13 or 32)last--;
            if(last<10||!raw.AsSpan(last-5,5).SequenceEqual("%%EOF"u8))
                throw InvalidMedia();
            var cleaned=raw.AsSpan(0,last).ToArray();
            return new CleanMedia(body.Version,"attachment",body.FileName,
                "application/pdf",cleaned);
        }
        finally {CryptographicOperations.ZeroMemory(raw);}
    }
    private static WebAdminException InvalidMedia()=>new(422,
        "invalid_announcement_media","Provide a valid bounded banner image or PDF attachment.");

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
