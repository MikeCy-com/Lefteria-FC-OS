using System.Net;
using System.Text.Json;
using LefteriaFc.Identity.IntegrationTests;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Extensions;

namespace LefteriaFc.MobilePreview;

internal static class WebPlatformPreviewProxy
{
    private const int AuthMaxBodyBytes = 16 * 1024;
    private const int ClubMaxBodyBytes = 1024 * 1024;
    private static readonly string[] HopHeaders =
        ["Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade"];

    public static WebApplication Create(WebPlatformPreviewOptions options, HttpClient api, HttpClient web, WebAdminDatabaseFixture database)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        { Args = [], EnvironmentName = "Production", ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            // Both address families are loopback only. localhost also permits the
            // real Secure cookie browser policy; no cookie attributes are weakened.
            server.ListenLocalhost(options.Port);
            server.Limits.MaxRequestBodySize = ClubMaxBodyBytes;
        });
        var app = builder.Build();
        app.Run(context => HandleAsync(context, app, options, api, web, database));
        return app;
    }

    public static void AssertLoopbackListener(WebApplication app, WebPlatformPreviewOptions options)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null || addresses.Count != 1 || addresses.Single() != options.Origin.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("The web-platform preview must have only its explicit localhost listener.");
    }

    private static async Task HandleAsync(HttpContext context, WebApplication app, WebPlatformPreviewOptions options,
        HttpClient api, HttpClient web, WebAdminDatabaseFixture database)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        if (context.Request.Host.Host != "localhost" || context.Request.Host.Port != options.Port ||
            context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote))
        { context.Response.StatusCode = 403; return; }
        if (context.Request.Path.StartsWithSegments("/__fixture__"))
        {
            await StopAsync(context, app, options);
            return;
        }

        var isApi = context.Request.Path.StartsWithSegments("/api");
        var upstream = isApi ? api.BaseAddress! : options.WebOrigin;
        var target = new Uri(upstream.GetLeftPart(UriPartial.Authority) + context.Request.GetEncodedPathAndQuery());
        if (target.Scheme != upstream.Scheme || target.Host != upstream.Host || target.Port != upstream.Port)
        { context.Response.StatusCode = 400; return; }
        var targetPath = new PathString(Uri.UnescapeDataString(target.AbsolutePath));
        var isClub = isApi && targetPath == "/api/admin/club" &&
            (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsPut(context.Request.Method));
        var isAdmin = isApi && (targetPath.StartsWithSegments("/api/admin/auth") || isClub);
        if (isApi && !isAdmin && !targetPath.StartsWithSegments("/api/public") && !targetPath.StartsWithSegments("/api/health"))
        { context.Response.StatusCode = 404; return; }
        var mutation = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
        var maxBodyBytes = isClub && HttpMethods.IsPut(context.Request.Method) ? ClubMaxBodyBytes : AuthMaxBodyBytes;
        var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = maxBodyBytes;
        var localOrigin = options.Origin.GetLeftPart(UriPartial.Authority);
        // Never manufacture an accepted Origin for a foreign/missing browser
        // origin. The actual API still enforces its custom header and JSON policy.
        if (isAdmin && mutation && (context.Request.Headers.Origin.Count != 1 || context.Request.Headers.Origin[0] != localOrigin))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { detail = "The request origin is not allowed.", code = "request_origin_denied" });
            return;
        }
        if (context.Request.ContentLength > maxBodyBytes) { context.Response.StatusCode = 413; return; }

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
            // Retain empty JSON Content-Type for logout too; its real endpoint
            // intentionally has no request body.
            if (mutation || context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            {
                await using var body = new MemoryStream();
                var chunk = new byte[4096];
                int count;
                while ((count = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) != 0)
                {
                    if (body.Length + count > maxBodyBytes) { context.Response.StatusCode = 413; return; }
                    await body.WriteAsync(chunk.AsMemory(0, count), context.RequestAborted);
                }
                var bytes = body.ToArray();
                request.Content = new ByteArrayContent(bytes);
                if (isAdmin && targetPath.Equals(new PathString("/api/admin/auth/login"))) TrackUsernames(bytes, database);
            }
            var excluded = Excluded(context.Request.Headers.Connection.ToString());
            excluded.UnionWith(["Host", "Content-Length", "Authorization", "Expect", "Forwarded", "X-WebAdmin-Test-Client"]);
            if (isApi && !isAdmin) excluded.Add("Cookie");
            if (isAdmin && mutation) excluded.Add("Origin");
            foreach (var header in context.Request.Headers)
            {
                if (excluded.Contains(header.Key) || header.Key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)) continue;
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && request.Content is not null)
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            if (isApi) request.Headers.Add("X-WebAdmin-Test-Client", "249");
            if (isAdmin && mutation) request.Headers.Add("Origin", WebAdminApplicationFactory.Origin);
            using var response = await (isApi ? api : web).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            excluded = Excluded(string.Join(',', response.Headers.Connection));
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!excluded.Contains(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
            context.Response.Headers.CacheControl = "no-store";
            if (!isApi && response.Headers.Location is { IsAbsoluteUri: true } location &&
                location.GetLeftPart(UriPartial.Authority) == options.WebOrigin.GetLeftPart(UriPartial.Authority))
                context.Response.Headers.Location = localOrigin + location.PathAndQuery + location.Fragment;
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (BadHttpRequestException error) when (error.StatusCode == 413)
        { if (!context.Response.HasStarted) context.Response.StatusCode = 413; else context.Abort(); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
        catch (Exception)
        {
            if (context.Response.HasStarted) context.Abort();
            else
            {
                context.Response.Clear(); context.Response.StatusCode = 502;
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(new { detail = "Local preview upstream unavailable.", code = "preview_upstream_unavailable" });
            }
        }
    }

    private static void TrackUsernames(byte[] bytes, WebAdminDatabaseFixture database)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Name.Equals("username", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    lock (database) database.TrackUsername(property.Value.GetString()!);
        }
        catch (JsonException) { /* The real API rejects malformed JSON; no username is inferred. */ }
    }

    private static async Task StopAsync(HttpContext context, WebApplication app, WebPlatformPreviewOptions options)
    {
        if (context.Request.Path != "/__fixture__/stop") { context.Response.StatusCode = 404; return; }
        if (!HttpMethods.IsPost(context.Request.Method)) { context.Response.StatusCode = 405; return; }
        if (context.Request.Headers["X-Lefteria-Preview"].Count != 1 || context.Request.Headers["X-Lefteria-Preview"] != "stop" || context.Request.Headers.Origin.Count != 1 ||
            context.Request.Headers.Origin[0] != options.Origin.GetLeftPart(UriPartial.Authority))
        { context.Response.StatusCode = 403; return; }
        context.Response.StatusCode = 202;
        context.Response.OnCompleted(() => { app.Lifetime.StopApplication(); return Task.CompletedTask; });
        await context.Response.WriteAsJsonAsync(new { stopping = true });
    }

    private static HashSet<string> Excluded(string connection) =>
        new(HopHeaders.Concat(connection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)), StringComparer.OrdinalIgnoreCase);
}
