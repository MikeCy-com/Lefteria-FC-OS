using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Extensions;

namespace LefteriaFc.MobilePreview;

internal static class PreviewProxy
{
    internal const long MaxBodyBytes = 32 * 1024 * 1024;
    private static readonly string[] HopHeaders =
        ["Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade"];

    public static WebApplication Create(PreviewOptions options, HttpClient api, HttpClient web)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [], EnvironmentName = "Production", ContentRootPath = AppContext.BaseDirectory
        });
        // Remove ambient Kestrel:Endpoints, ASPNETCORE_URLS, appsettings and env
        // providers before Kestrel options are materialized. Only Listen below binds.
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Loopback, options.Port);
            server.Limits.MaxRequestBodySize = MaxBodyBytes;
        });
        var app = builder.Build();
        app.Run(context => HandleAsync(context, app, options, api, web));
        return app;
    }

    public static void AssertSingleLoopbackListener(WebApplication app, PreviewOptions options)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null || addresses.Count != 1 ||
            !string.Equals(addresses.Single(), options.Origin.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
            throw new InvalidOperationException("The preview must have exactly one explicit loopback listener.");
    }

    private static async Task HandleAsync(HttpContext context, WebApplication app,
        PreviewOptions options, HttpClient api, HttpClient web)
    {
        // Reject DNS-rebinding hosts, including requests carrying a nonlocal Host.
        if (context.Request.Host.Host != "127.0.0.1" || context.Request.Host.Port != options.Port ||
            context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (context.Request.Path.StartsWithSegments("/__fixture__"))
        {
            if (context.Request.Path != "/__fixture__/stop")
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            else if (!HttpMethods.IsPost(context.Request.Method))
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            else if (context.Request.Headers.Origin.Count > 0 &&
                     context.Request.Headers.Origin.ToString() != options.Origin.GetLeftPart(UriPartial.Authority))
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            else
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                context.Response.Headers.CacheControl = "no-store";
                context.Response.OnCompleted(() => { app.Lifetime.StopApplication(); return Task.CompletedTask; });
                await context.Response.WriteAsJsonAsync(new { stopping = true });
            }
            return;
        }
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var isApi = context.Request.Path.StartsWithSegments("/api");
        var client = isApi ? api : web;
        var origin = isApi ? api.BaseAddress! : options.WebOrigin;
        // Concatenating onto a fixed authority prevents //path from replacing the host.
        var target = new Uri(origin.GetLeftPart(UriPartial.Authority) + context.Request.GetEncodedPathAndQuery());
        if (target.Scheme != origin.Scheme || target.Host != origin.Host || target.Port != origin.Port)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentLength is > 0)
                request.Content.Headers.ContentLength = context.Request.ContentLength;
        }
        var excluded = Excluded(context.Request.Headers.Connection.ToString());
        excluded.UnionWith(["Host", "Content-Length", "Cookie", "Expect", "Forwarded"]);
        foreach (var header in context.Request.Headers)
        {
            if (excluded.Contains(header.Key) || header.Key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) ||
                (!isApi && header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))) continue;
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && request.Content is not null)
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            excluded = Excluded(string.Join(',', response.Headers.Connection));
            excluded.Add("Set-Cookie");
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!excluded.Contains(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
            // Keep local Node redirects inside the one browser origin.
            if (!isApi && response.Headers.Location is { IsAbsoluteUri: true } location &&
                location.GetLeftPart(UriPartial.Authority) == options.WebOrigin.GetLeftPart(UriPartial.Authority))
                context.Response.Headers.Location = options.Origin.GetLeftPart(UriPartial.Authority) + location.PathAndQuery + location.Fragment;
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            else context.Abort();
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
        catch (Exception)
        {
            if (context.Response.HasStarted) context.Abort();
            else
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(new { error = "Local preview upstream unavailable." });
            }
        }
    }

    private static HashSet<string> Excluded(string connection) =>
        new(HopHeaders.Concat(connection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            StringComparer.OrdinalIgnoreCase);
}
