using System.Globalization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Identity.IntegrationTests;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace LefteriaFc.MobilePreview;

internal sealed record WorkspaceSettings(string PublicOrigin, string SigningKey, string OtpKey, string ThrottleKey,
    string FoundingSetupCode, string KnownProxy);

// Workspace-only DI, mirroring PreviewApiFactory: fixed fixture OTP for the
// persisted fictional phones only. Product startup has no such option.
internal sealed class WorkspaceApiFactory(FixtureConfiguration fixture, string adminAuthConnectionString,
    IReadOnlyCollection<PhoneNumber> phones, WorkspaceSettings settings) : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var mobile = new NpgsqlConnectionStringBuilder(fixture.AppConnectionString);
        var auth = new NpgsqlConnectionStringBuilder(adminAuthConnectionString);
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Never inherit machine configuration, provider credentials or endpoints.
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = fixture.AppConnectionString,
                ["Database:Host"] = mobile.Host, ["Database:Name"] = mobile.Database,
                ["Database:Username"] = mobile.Username, ["Database:Password"] = mobile.Password,
                ["MobileAuth:SigningKey"] = settings.SigningKey, ["MobileAuth:OtpKey"] = settings.OtpKey,
                ["WebAdmin:Enabled"] = "true", ["WebAdmin:PublicOrigin"] = settings.PublicOrigin,
                ["WebAdmin:ThrottleKey"] = settings.ThrottleKey, ["WebAdmin:FoundingSetupCode"] = settings.FoundingSetupCode,
                ["WebAdmin:Host"] = auth.Host, ["WebAdmin:Port"] = auth.Port.ToString(CultureInfo.InvariantCulture),
                ["WebAdmin:Database"] = auth.Database, ["WebAdmin:Username"] = auth.Username, ["WebAdmin:Password"] = auth.Password,
                ["Http:KnownProxies:0"] = settings.KnownProxy,
                ["LegacyMobileBridge:Enabled"] = "false", ["LegacyMobileBridge:SigningSecret"] = "", ["LegacyMobileBridge:FingerprintKey"] = "",
                ["Twilio:AccountSid"] = "", ["Twilio:AuthToken"] = "", ["Twilio:FromNumber"] = "",
                ["TwilioPaymentSms:Enabled"] = "false", ["Payabl:Enabled"] = "false", ["Payabl:Sandbox"] = "true",
                ["WebPush:Enabled"] = "false", ["LEFTERIA_AI_ENABLED"] = "false",
                ["PublicMedia:Root"] = Environment.GetEnvironmentVariable("LEFTERIA_PUBLIC_MEDIA_ROOT"),
                ["Logging:LogLevel:Default"] = "Warning"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(new FixtureSmsSender(phones));
            services.RemoveAll<IOtpProtector>();
            services.AddSingleton<IOtpProtector, FixedFixtureOtpProtector>();
            services.RemoveAll<LefteriaDbContext>();
            services.AddScoped(_ => new LefteriaDbContext(new DbContextOptionsBuilder<LefteriaDbContext>()
                .UseNpgsql(fixture.AppConnectionString).Options));
        });
    }
}
