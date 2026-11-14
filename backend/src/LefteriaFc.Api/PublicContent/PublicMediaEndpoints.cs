using LefteriaFc.Application.PublicContent;

namespace LefteriaFc.Api.PublicContent;

public static class PublicMediaEndpoints
{
    public static IEndpointRouteBuilder MapPublicMedia(this IEndpointRouteBuilder endpoints)
    {
        foreach (var method in new[] { "GET", "HEAD" })
            endpoints.MapMethods("/api/uploads/{folder}/{fileName}", [method], Deliver).AllowAnonymous()
                .WithTags("Public website").WithName(method == "GET" ? "GetPublicMedia" : "HeadPublicMedia")
                .Produces(StatusCodes.Status200OK, contentType: "image/jpeg").Produces(StatusCodes.Status404NotFound);
        return endpoints;
    }

    private static IResult Deliver(string folder, string fileName, IConfiguration configuration, HttpContext context)
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentSecurityPolicy = "sandbox";
        var type = PublicMediaPolicy.ContentType(folder, fileName);
        var root = configuration["PublicMedia:Root"];
        if (type is null || string.IsNullOrWhiteSpace(root)) return NotFound(context);
        var baseDirectory = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(baseDirectory, folder, fileName));
        if (!path.StartsWith(baseDirectory, StringComparison.Ordinal) || !File.Exists(path)) return NotFound(context);
        context.Response.Headers.CacheControl = PublicMediaPolicy.CacheControl;
        return Results.File(path, type, enableRangeProcessing: true, lastModified: File.GetLastWriteTimeUtc(path));
    }

    private static IResult NotFound(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(new PublicContentError("Το αρχείο δεν βρέθηκε.", "media_not_found"), statusCode: 404);
    }
}
