using System.Buffers;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using LefteriaFc.Application.MobileAuth;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace LefteriaFc.Api.Identity;

// OpenAPI-only wire shape. The handler manually streams this one file to
// bounded memory, so form binding never writes raw uploads to temporary files.
public sealed record MobileAvatarUploadBody
{
    [JsonPropertyName("file")]
    public required IFormFile File { get; init; }
}

public static partial class MobileIdentityEndpoints
{
    private static void MapMobileAvatar(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/mobile/profile/avatar", (HttpContext context, MobileAuthService service,
                IAvatarImageSanitizer sanitizer, CancellationToken token) =>
                Execute(() => UploadAvatarAsync(context, service, sanitizer, token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile profile")
            .DisableAntiforgery() // Bearer-only endpoint; no cookie credentials or automatic form binding.
            .Accepts<MobileAvatarUploadBody>("multipart/form-data")
            .Produces<MobileAvatarResult>().Produces(StatusCodes.Status413PayloadTooLarge)
            .WithName("UploadMobileAvatar").WithMobileErrors();

        foreach (var method in new[] { "GET", "HEAD" })
            endpoints.MapMethods("/api/uploads/avatars/{fileName}", [method], DownloadAvatarAsync)
                .WithTags("Mobile profile").WithName(method == "GET" ? "GetMobileAvatar" : "HeadMobileAvatar")
                .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
                .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<MobileAvatarResult> UploadAvatarAsync(HttpContext context, MobileAuthService service,
        IAvatarImageSanitizer sanitizer, CancellationToken cancellationToken)
    {
        try
        {
            var session = MobilePrincipal.Read(context.User);
            await service.AuthorizeAvatarUploadAsync(session, cancellationToken);
            var (bytes, contentType) = await ReadAvatarAsync(context.Request, cancellationToken);
            SanitizedMobileAvatar sanitized;
            try { sanitized = sanitizer.Sanitize(bytes, contentType, cancellationToken); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            return await service.UpdateAvatarAsync(session, sanitized, cancellationToken);
        }
        catch (DbException) { throw AvatarStorageUnavailable(); }
        catch (DbUpdateException) { throw AvatarStorageUnavailable(); }
    }

    private static async Task<(byte[] Bytes, string? ContentType)> ReadAvatarAsync(HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MobileAvatarLimits.RequestBytes) throw AvatarTooLarge();
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
            feature.MaxRequestBodySize = MobileAvatarLimits.RequestBytes;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType) ||
            !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw InvalidAvatarForm();
        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary) || boundary.Length > 70) throw InvalidAvatarForm();
        using var bounded = new AvatarReadStream(request.Body, MobileAvatarLimits.RequestBytes);
        var reader = new MultipartReader(boundary, bounded)
        {
            HeadersCountLimit = 16, HeadersLengthLimit = 4096, BodyLengthLimit = MobileAvatarLimits.RequestBytes
        };
        try
        {
            var section = await reader.ReadNextSectionAsync(cancellationToken);
            if (section is null || !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase) ||
                HeaderUtilities.RemoveQuotes(disposition.Name).Value != "file" ||
                (!disposition.FileName.HasValue && !disposition.FileNameStar.HasValue)) throw InvalidAvatarForm();
            using var memory = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                int read;
                while ((read = await section.Body.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
                {
                    if (memory.Length + read > MobileAvatarLimits.InputBytes) throw AvatarTooLarge();
                    await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
            if (memory.Length == 0 || await reader.ReadNextSectionAsync(cancellationToken) is not null) throw InvalidAvatarForm();
            return (memory.ToArray(), section.ContentType);
        }
        catch (InvalidDataException) { throw InvalidAvatarForm(); }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            throw AvatarTooLarge();
        }
        catch (IOException) { throw InvalidAvatarForm(); }
    }

    private static async Task DownloadAvatarAsync(string fileName, HttpContext context, IMobileAuthRepository repository,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentSecurityPolicy = "sandbox";
        context.Response.Headers.CacheControl = "no-store";
        if (!MobileAvatarLimits.IsFileName(fileName)) { context.Response.StatusCode = 404; return; }
        MobileAvatarContent? avatar;
        try { avatar = await repository.GetPublicAvatarAsync(fileName, cancellationToken); }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            context.Response.StatusCode = 503;
            return;
        }
        if (avatar is null) { context.Response.StatusCode = 404; return; }
        context.Response.StatusCode = 200;
        context.Response.ContentType = "image/jpeg";
        context.Response.ContentLength = avatar.JpegBytes.Length;
        context.Response.Headers.CacheControl = "public, max-age=2592000, immutable";
        context.Response.Headers.ContentDisposition = "inline; filename=\"avatar.jpg\"";
        if (!HttpMethods.IsHead(context.Request.Method))
            await context.Response.Body.WriteAsync(avatar.JpegBytes, cancellationToken);
    }

    private static MobileAuthException InvalidAvatarForm() => new(MobileAuthFaultKind.BadRequest,
        MobileAuthErrorCode.InvalidAvatar, "Provide exactly one image file in the file field.");
    private static MobileAuthException AvatarTooLarge() => new(MobileAuthFaultKind.PayloadTooLarge,
        MobileAuthErrorCode.AvatarTooLarge, "The image must be no larger than 5 MiB.");
    private static MobileAuthException AvatarStorageUnavailable() => new(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.AvatarStorageUnavailable, "The avatar could not be saved. Try again shortly.");

    private sealed class AvatarReadStream(Stream inner, long maximum) : Stream
    {
        private long consumed;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => consumed; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Count(read);
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            Count(read);
            return read;
        }
        private void Count(int read) { consumed += read; if (consumed > maximum) throw AvatarTooLarge(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
