using System.Net;
using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed class WebAdminApplicationFactory : WebApplicationFactory<global::Program>
{
    public const string Origin = "https://admin-fixture.lefteria.invalid";
    public const string CookieName = "__Host-lefteria-admin";
    private const string Issuer = "lefteria-identity-http-tests";
    private const string Audience = "lefteria-identity-http-test-client";
    private readonly FixtureConfiguration fixture;
    private readonly NpgsqlConnectionStringBuilder adminAuth;
    private readonly NpgsqlConnectionStringBuilder mobile;

    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string OtpKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string ThrottleKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public RejectingWebAdminSmsSender Sms { get; } = new();

    public WebAdminApplicationFactory(FixtureConfiguration fixture, string adminAuthConnectionString, string adminAuthUsername)
    {
        FixtureConfiguration.ValidateConnection(adminAuthConnectionString, adminAuthUsername);
        this.fixture = fixture;
        adminAuth = new(adminAuthConnectionString);
        mobile = new(fixture.AppConnectionString);
        if (adminAuthUsername == fixture.AppUsername || adminAuthUsername == fixture.AdminUsername ||
            adminAuth.Password is not { Length: >= 32 } || adminAuth.Password == mobile.Password)
            throw new InvalidOperationException("The disposable web-admin role and credentials must be independent.");
    }

    public string Fingerprint(string purpose, string value) => Convert.ToHexString(HMACSHA256.HashData(
        Convert.FromBase64String(ThrottleKey), Encoding.UTF8.GetBytes("lefteria-web-admin-v1\n" + purpose + "\n" + value))).ToLowerInvariant();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.AppConnectionString,
            ["Database:Host"] = mobile.Host, ["Database:Port"] = mobile.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Database:Database"] = mobile.Database, ["Database:Username"] = mobile.Username, ["Database:Password"] = mobile.Password,
            ["MobileAuth:SigningKey"] = SigningKey, ["MobileAuth:OtpKey"] = OtpKey,
            ["MobileAuth:Issuer"] = Issuer, ["MobileAuth:Audience"] = Audience,
            ["LegacyMobileBridge:Enabled"] = "false", ["LegacyMobileBridge:SigningSecret"] = "", ["LegacyMobileBridge:FingerprintKey"] = "",
            ["Twilio:AccountSid"] = "", ["Twilio:AuthToken"] = "", ["Twilio:FromNumber"] = "",
            ["WebAdmin:Enabled"] = "true", ["WebAdmin:PublicOrigin"] = Origin, ["WebAdmin:ThrottleKey"] = ThrottleKey,
            ["WebAdmin:Host"] = adminAuth.Host, ["WebAdmin:Port"] = adminAuth.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["WebAdmin:Database"] = adminAuth.Database, ["WebAdmin:Username"] = adminAuth.Username, ["WebAdmin:Password"] = adminAuth.Password
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(Sms);
            services.RemoveAll<LefteriaDbContext>();
            services.AddScoped(_ => new LefteriaDbContext(new DbContextOptionsBuilder<LefteriaDbContext>()
                .UseNpgsql(fixture.AppConnectionString).Options));
            services.AddSingleton<IStartupFilter, TrustedWebAdminRemoteIpFilter>();
        });
    }
}

internal sealed class TrustedWebAdminRemoteIpFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, continuation) =>
        {
            if (!int.TryParse(context.Request.Headers["X-WebAdmin-Test-Client"], out var client) || client is < 1 or > 250)
                throw new InvalidOperationException("A trusted web-admin test client identifier is required.");
            context.Connection.RemoteIpAddress = IPAddress.Parse($"198.51.100.{client}");
            return continuation(context);
        });
        next(app);
    };
}

internal sealed class RejectingWebAdminSmsSender : ISmsSender
{
    private int calls;
    public int Calls => Volatile.Read(ref calls);
    public Task<bool> SendOtpAsync(PhoneNumber phone, string code, MobileRole? detectedRole, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        throw new InvalidOperationException("Web-admin authentication must never invoke SMS in this fixture.");
    }
}
