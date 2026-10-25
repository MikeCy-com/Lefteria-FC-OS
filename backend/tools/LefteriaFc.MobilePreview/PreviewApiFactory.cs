using System.Net;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Identity.IntegrationTests;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

[assembly: WebApplicationFactoryContentRoot("LefteriaFc.Api", "../../../../../src/LefteriaFc.Api", "LefteriaFc.Api.csproj", "0")]

namespace LefteriaFc.MobilePreview;

// This executable alone changes DI. Product startup has no fixture OTP/SMS option.
internal sealed class PreviewApiFactory(FixtureConfiguration fixture, PhoneNumber phone,
    string signingKey, string otpKey) : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Never inherit appsettings, environment credentials, provider settings,
            // or endpoint configuration from the machine hosting this test tool.
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = fixture.AppConnectionString,
                ["MobileAuth:SigningKey"] = signingKey, ["MobileAuth:OtpKey"] = otpKey,
                ["MobileAuth:Issuer"] = "lefteria-mobile-preview",
                ["MobileAuth:Audience"] = "lefteria-mobile-preview-browser",
                ["Twilio:AccountSid"] = "", ["Twilio:AuthToken"] = "", ["Twilio:FromNumber"] = ""
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(new FixtureSmsSender(phone));
            services.RemoveAll<IOtpProtector>();
            services.AddSingleton<IOtpProtector, FixedFixtureOtpProtector>();
            services.RemoveAll<LefteriaDbContext>();
            services.AddScoped(_ => new LefteriaDbContext(new DbContextOptionsBuilder<LefteriaDbContext>()
                .UseNpgsql(fixture.AppConnectionString).Options));
            services.AddSingleton<IStartupFilter, FixtureRemoteIpFilter>();
            services.RemoveAll<ILoggerProvider>();
        });
    }
}

internal sealed class FixedFixtureOtpProtector(IOptions<MobileAuthOptions> options) : IOtpProtector
{
    internal const string Code = "123456";
    private readonly HmacOtpProtector realProtector = new(options);
    public string CreateSixDigitCode() => Code;
    public byte[] ComputeDigest(Guid challengeId, PhoneNumber phone, string code) =>
        realProtector.ComputeDigest(challengeId, phone, code);
}

internal sealed class FixtureSmsSender(PhoneNumber fixturePhone) : ISmsSender
{
    public Task<bool> SendOtpAsync(PhoneNumber phone, string code, MobileRole? detectedRole,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Do not capture/log codes or accept delivery for any other phone.
        return Task.FromResult(phone.Digits == fixturePhone.Digits && code == FixedFixtureOtpProtector.Code);
    }
}

internal sealed class FixtureRemoteIpFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, continuation) =>
        {
            // Fixed by this test host, never controlled by a request header.
            context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.250");
            return continuation(context);
        });
        next(app);
    };
}
