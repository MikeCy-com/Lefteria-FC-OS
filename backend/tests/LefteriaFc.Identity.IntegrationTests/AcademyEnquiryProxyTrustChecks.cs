using System.Net;
using LefteriaFc.Application.Marketing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Identity.IntegrationTests;

// The product's source-of-client-IP behavior must be safe before a future
// public Academy /join route can use an HMAC throttle fingerprint.
// No production proxy is contacted; this verifies actual ASP.NET middleware.
internal static class AcademyEnquiryProxyTrustChecks
{
    private sealed record Snapshot(string Address, string Scheme, string Host);

    public static async Task RunAsync()
    {
        var edge = IPAddress.Parse("192.0.2.25");
        var realClient = IPAddress.Parse("203.0.113.44");
        var untrustedPeer = IPAddress.Parse("198.51.100.88");

        using var logger = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownProxies.Clear();
        options.KnownProxies.Add(edge);

        async Task<Snapshot> ProbeAsync(IPAddress remote, string forwardedFor)
        {
            Snapshot? snapshot = null;
            RequestDelegate next = context =>
            {
                snapshot = new Snapshot(
                    context.Connection.RemoteIpAddress?.ToString() ?? "missing",
                    context.Request.Scheme, context.Request.Host.Value);
                return Task.CompletedTask;
            };
            var middleware = new ForwardedHeadersMiddleware(next, logger, Options.Create(options));
            var context = new DefaultHttpContext();
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("os-preview.lefteria.invalid");
            context.Connection.RemoteIpAddress = remote;
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-Host"] = "spoofed.invalid";
            await middleware.Invoke(context);
            return snapshot ?? throw new InvalidOperationException("Forwarded-header middleware skipped its next stage.");
        }

        var trusted = await ProbeAsync(edge, realClient.ToString());
        Check.Equal(realClient.ToString(), trusted.Address,
            "An explicitly known edge proxy did not forward the client IP.");
        Check.Equal("https", trusted.Scheme, "The trusted proxy scheme was not applied.");
        Check.Equal("os-preview.lefteria.invalid", trusted.Host,
            "An unconfigured forwarded host unexpectedly changed the request host.");

        var forged = await ProbeAsync(edge, untrustedPeer + ", " + realClient);
        Check.Equal(realClient.ToString(), forged.Address,
            "An attacker-controlled earlier forwarding hop overrode the trusted one-hop limit.");

        var direct = await ProbeAsync(untrustedPeer, realClient.ToString());
        Check.Equal(untrustedPeer.ToString(), direct.Address,
            "A direct untrusted caller forged the Academy throttle identity.");
        Check.Equal("http", direct.Scheme,
            "An untrusted caller forged the request scheme.");
        Check.Equal("os-preview.lefteria.invalid", direct.Host,
            "An untrusted caller forged the request host.");

        var key = Enumerable.Repeat((byte)0x6d, 32).ToArray();
        var verified = AcademyLeadFingerprint.Compute(IPAddress.Parse(forged.Address), key);
        var injected = AcademyLeadFingerprint.Compute(untrustedPeer, key);
        Check.True(verified != injected,
            "The HMAC throttle fingerprint used an untrusted forwarding hop.");
    }
}
