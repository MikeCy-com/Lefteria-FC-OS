using System.Data.Common;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Api.WebAdmin;

public sealed record WebAdminError(string Detail, string Code);

public static class WebAdminRequestGuards
{
    public static IApplicationBuilder UseWebAdminGuards(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/api/admin")) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                var options = context.RequestServices.GetRequiredService<IOptions<WebAdminOptions>>().Value;
                if (!TrustedMutation(context.Request, options))
                {
                    await Reject(context, 403, "request_origin_denied", "The request origin is not allowed.");
                    return;
                }
                if (!context.Request.HasJsonContentType())
                {
                    await Reject(context, 415, "json_required", "A JSON request is required.");
                    return;
                }
                // Safe full public history/description are independently bounded;
                // credential and session endpoints keep their original small cap.
                var limit = HttpMethods.IsPut(context.Request.Method) && context.Request.Path == "/api/admin/club" ? 1024 * 1024 : 16 * 1024;
                if (context.Request.ContentLength > limit)
                {
                    await Reject(context, 413, "request_too_large", "The request is too large.");
                    return;
                }
                // Bound unknown-length bodies before Minimal API model binding as well.
                await using var buffered = new MemoryStream();
                var chunk = new byte[4096];
                int count;
                while ((count = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) != 0)
                {
                    if (buffered.Length + count > limit)
                    {
                        await Reject(context, 413, "request_too_large", "The request is too large.");
                        return;
                    }
                    await buffered.WriteAsync(chunk.AsMemory(0, count), context.RequestAborted);
                }
                buffered.Position = 0;
                var original = context.Request.Body;
                context.Request.Body = buffered;
                try { await next(context); }
                finally { context.Request.Body = original; }
            }
            else await next(context);
        }
        catch (WebAdminException error) when (!context.Response.HasStarted)
        { await Reject(context, error.Status, error.Code, error.Message); }
        catch (Exception error) when (error is DbException or TimeoutException && !context.Response.HasStarted)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("WebAdmin")
                .LogWarning("Admin authentication storage unavailable ({ExceptionType})", error.GetType().Name);
            await Reject(context, 503, "admin_auth_unavailable", "Admin authentication is temporarily unavailable.");
        }
    });

    private static bool TrustedMutation(HttpRequest request, WebAdminOptions options)
    {
        var origins = request.Headers.Origin;
        if (request.Headers["X-Lefteria-Admin"].Count != 1 || request.Headers["X-Lefteria-Admin"] != "1" || origins.Count != 1 ||
            !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin) || origin.UserInfo.Length != 0 ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 ||
            !Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var expected) ||
            !string.Equals(origin.GetLeftPart(UriPartial.Authority), expected.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) return false;
        var site = request.Headers["Sec-Fetch-Site"];
        return site.Count == 0 || site.Count == 1 && site[0] is "same-origin" or "none";
    }

    private static Task Reject(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new WebAdminError(detail, code));
    }
}
