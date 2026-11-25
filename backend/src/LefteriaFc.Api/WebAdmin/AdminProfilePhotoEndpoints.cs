using System.Buffers;
using System.Security.Cryptography;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.WebAdmin;

/// <summary>Private self-avatar, never a public media URL or another user's
/// photo. Uploads go through the same strict image decoder/EXIF-stripping
/// sanitizer used for mobile, but with admin cookie and Origin guards.</summary>
public static class AdminProfilePhotoEndpoints
{
    public static IEndpointRouteBuilder MapAdminProfilePhoto(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/auth/profile/photo",async (
            HttpContext context,WebAdminService service,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token) =>
        {
            NoStore(context);
            var digest=await SessionDigest(context,service,secrets,token);
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand(
                "SELECT identity.read_admin_own_profile_photo(@digest)",connection);
            command.Parameters.AddWithValue("digest",digest);
            byte[]? image;
            try
            {
                var raw=await command.ExecuteScalarAsync(token);
                image=raw is DBNull or null ? null : raw as byte[] ??
                    throw new WebAdminException(503,"admin_photo_storage_unavailable",
                        "The profile photo could not be retrieved.");
            }
            catch(PostgresException error) { throw SqlError(error); }
            if(image is null)return Results.NotFound();
            return Results.File(image,contentType:"image/jpeg",enableRangeProcessing:false);
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("ReadOwnAdminProfilePhoto")
        .WithTags("Web administration")
        .ExcludeFromDescription();

        endpoints.MapPost("/api/admin/auth/profile/photo",async (
            HttpContext context,WebAdminService service,WebAdminConnections connections,
            IWebAdminSecrets secrets,IAvatarImageSanitizer sanitizer,CancellationToken token) =>
        {
            NoStore(context);
            var digest=await SessionDigest(context,service,secrets,token);
            var (input,contentType)=await ReadSinglePhotoAsync(context.Request,token);
            SanitizedMobileAvatar photo;
            try
            {
                try { photo=sanitizer.Sanitize(input,contentType,token); }
                catch(MobileAuthException error)
                {
                    throw new WebAdminException(error.Kind switch
                    {
                        MobileAuthFaultKind.TooManyRequests=>429,
                        MobileAuthFaultKind.ServiceUnavailable=>503,
                        MobileAuthFaultKind.PayloadTooLarge=>413,
                        _=>422
                    },"admin_photo_invalid",
                      error.Kind==MobileAuthFaultKind.TooManyRequests
                      ? "Photo processing is busy. Try again shortly."
                      : "Provide a valid JPEG, PNG, GIF or WebP photo.");
                }
            }
            finally { CryptographicOperations.ZeroMemory(input); }
            try
            {
                await SaveAsync(digest,photo.JpegBytes,connections,token);
                return Results.NoContent();
            }
            finally { CryptographicOperations.ZeroMemory(photo.JpegBytes); }
        })
        .RequireAuthorization(WebAdminServices.Policy)
        // Custom admin guard checks exact Origin and X-Lefteria-Admin for
        // mutations; ASP.NET's automatic form antiforgery is not used here.
        .DisableAntiforgery()
        .WithName("WriteOwnAdminProfilePhoto")
        .WithTags("Web administration")
        .ExcludeFromDescription();

        endpoints.MapDelete("/api/admin/auth/profile/photo",async (
            HttpContext context,WebAdminService service,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token) =>
        {
            NoStore(context);
            var digest=await SessionDigest(context,service,secrets,token);
            await SaveAsync(digest,null,connections,token);
            return Results.NoContent();
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("RemoveOwnAdminProfilePhoto")
        .WithTags("Web administration")
        .ExcludeFromDescription();
        return endpoints;
    }

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        context.Response.Headers.XContentTypeOptions="nosniff";
        context.Response.Headers.ContentSecurityPolicy="sandbox";
    }

    private static async Task<string> SessionDigest(HttpContext context,
        WebAdminService service,IWebAdminSecrets secrets,CancellationToken token)
    {
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        if(cookie is null || !secrets.IsToken(cookie) ||
            await service.CurrentAsync(cookie,token) is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        return secrets.TokenDigest(cookie);
    }

    private static async Task SaveAsync(string digest,byte[]? image,
        WebAdminConnections connections,CancellationToken token)
    {
        await using var connection=await connections.OpenAsync(token);
        await using var command=new NpgsqlCommand(
            "SELECT identity.write_admin_own_profile_photo(@digest,@jpeg)",connection);
        command.Parameters.AddWithValue("digest",digest);
        command.Parameters.Add("jpeg",NpgsqlDbType.Bytea).Value=image is null?DBNull.Value:image;
        try
        {
            if(await command.ExecuteScalarAsync(token) is not bool saved || !saved)
                throw new WebAdminException(503,"admin_photo_storage_unavailable",
                    "The profile photo could not be saved.");
        }
        catch(PostgresException error) {throw SqlError(error);}
    }

    private static WebAdminException SqlError(PostgresException error)=>error.SqlState switch
    {
        "42501"=>new(403,"admin_photo_access_revoked","Profile access changed."),
        "22023"=>new(422,"admin_photo_invalid","Invalid profile photo."),
        "42883" or "3F000"=>new(503,"admin_photo_storage_unavailable",
            "Private profile photo storage is not enabled."),
        _=>new(503,"admin_photo_storage_unavailable","Profile photo storage is unavailable.")
    };

    internal static async Task<(byte[] Data,string? ContentType)> ReadSinglePhotoAsync(
        HttpRequest request,CancellationToken token)
    {
        if(request.ContentLength>MobileAvatarLimits.RequestBytes)
            throw new WebAdminException(413,"admin_photo_too_large","Photo exceeds 5 MiB.");
        if(request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is {IsReadOnly:false} feature)
            feature.MaxRequestBodySize=MobileAvatarLimits.RequestBytes;
        if(!MediaTypeHeaderValue.TryParse(request.ContentType,out var mediaType)||
            !string.Equals(mediaType.MediaType.Value,"multipart/form-data",StringComparison.OrdinalIgnoreCase))
            throw InvalidForm();
        var boundary=HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if(string.IsNullOrEmpty(boundary)||boundary.Length>70)throw InvalidForm();
        var reader=new MultipartReader(boundary,request.Body)
        {
            HeadersCountLimit=16,HeadersLengthLimit=4096,
            BodyLengthLimit=MobileAvatarLimits.InputBytes
        };
        try
        {
            var section=await reader.ReadNextSectionAsync(token);
            if(section is null ||
               !ContentDispositionHeaderValue.TryParse(section.ContentDisposition,out var disposition)||
               !string.Equals(disposition.DispositionType.Value,"form-data",StringComparison.OrdinalIgnoreCase)||
               HeaderUtilities.RemoveQuotes(disposition.Name).Value!="file"||
               (!disposition.FileName.HasValue&&!disposition.FileNameStar.HasValue))
                throw InvalidForm();
            using var output=new MemoryStream();
            var buffer=ArrayPool<byte>.Shared.Rent(64*1024);
            try
            {
                int read;
                while((read=await section.Body.ReadAsync(buffer.AsMemory(),token))!=0)
                {
                    if(output.Length+read>MobileAvatarLimits.InputBytes)
                        throw new WebAdminException(413,"admin_photo_too_large","Photo exceeds 5 MiB.");
                    await output.WriteAsync(buffer.AsMemory(0,read),token);
                }
            }
            finally{ArrayPool<byte>.Shared.Return(buffer,clearArray:true);}
            if(output.Length==0 || await reader.ReadNextSectionAsync(token) is not null)
                throw InvalidForm();
            return(output.ToArray(),section.ContentType);
        }
        catch(BadHttpRequestException error) when(error.StatusCode==StatusCodes.Status413PayloadTooLarge)
        {throw new WebAdminException(413,"admin_photo_too_large","Photo exceeds 5 MiB.");}
        catch(InvalidDataException) {throw InvalidForm();}
        catch(IOException) {throw InvalidForm();}
    }

    private static WebAdminException InvalidForm()=>
        new(422,"admin_photo_form_invalid","Select exactly one image file.");
}
