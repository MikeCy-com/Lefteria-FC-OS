using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class WebAdminConfigurationChecks
{
    private const string MobileUsername = "mobile_configuration_fixture";
    private const string AdminUsername = "admin_configuration_fixture";
    private static readonly string MobilePassword = new('m', 40);
    private static readonly string AdminPassword = new('a', 40);
    private static readonly string SigningKey = Key(1);
    private static readonly string OtpKey = Key(2);
    private static readonly string ThrottleKey = Key(3);

    public static void Run()
    {
        var checks = 0;
        foreach (var staleFields in new[] { false, true })
        {
            var configuration = Baseline();
            configuration["ConnectionStrings:Postgres"] = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1", Database = "configuration_only_no_connection",
                Username = MobileUsername, Password = MobilePassword
            }.ConnectionString;
            if (staleFields)
            {
                configuration["Database:Username"] = "stale_configuration_fixture";
                configuration["Database:Password"] = new string('s', 40);
            }
            Assert(configuration, true, "Distinct effective connection-string credentials were refused."); checks++;
            Assert(With(configuration, "WebAdmin:Username", MobileUsername), false,
                "An effective shared mobile username passed configuration validation."); checks++;
            Assert(With(configuration, "WebAdmin:Password", MobilePassword), false,
                "An effective shared mobile password passed configuration validation."); checks++;
            if (staleFields)
            {
                var distinctActual = With(configuration, "WebAdmin:Username", configuration["Database:Username"]);
                distinctActual["WebAdmin:Password"] = configuration["Database:Password"];
                Assert(distinctActual, true, "Stale fallback fields overrode the actual connection-string credentials."); checks++;
            }
        }

        var fallback = Baseline();
        fallback["Database:Username"] = MobileUsername;
        fallback["Database:Password"] = MobilePassword;
        Assert(fallback, true, "Distinct fallback credentials were refused."); checks++;
        Assert(With(fallback, "WebAdmin:Username", MobileUsername), false, "A fallback shared username passed validation."); checks++;
        Assert(With(fallback, "WebAdmin:Password", MobilePassword), false, "A fallback shared password passed validation."); checks++;
        Assert(With(fallback, "ConnectionStrings:Postgres", "not a connection string"), false,
            "An invalid effective connection string silently used stale fallback credentials."); checks++;

        foreach (var reused in new[] { SigningKey, OtpKey, SigningKey.Insert(8, "\n") })
        {
            Assert(With(fallback, "WebAdmin:ThrottleKey", reused), false, "A decoded mobile authentication key was reused by web administration.");
            checks++;
        }
        foreach (var key in new[] { "", "not-base64", Convert.ToBase64String(new byte[31]) })
        {
            Assert(With(fallback, "WebAdmin:ThrottleKey", key), false, "A missing or short administrator key passed validation.");
            checks++;
        }

        foreach (var origin in new[]
        {
            "http://configuration.lefteria.invalid:8443", "https://user@configuration.lefteria.invalid:8443",
            "https://configuration.lefteria.invalid:8443/admin", "https://configuration.lefteria.invalid:8443/?query=1",
            "https://configuration.lefteria.invalid:8443/#fragment", "//configuration.lefteria.invalid:8443", ""
        })
        {
            Assert(With(fallback, "WebAdmin:PublicOrigin", origin), false, "An untrusted administrator origin passed validation.");
            checks++;
        }
        Assert(new() { ["WebAdmin:Enabled"] = "false" }, true,
            "Disabled administrator authentication required credentials or attempted initialization."); checks++;

        Console.WriteLine($"PASS {checks} web-admin DI configuration cases; no database or provider connection used.");
    }

    private static Dictionary<string, string?> Baseline() => new(StringComparer.Ordinal)
    {
        ["WebAdmin:Enabled"] = "true", ["WebAdmin:PublicOrigin"] = "https://configuration.lefteria.invalid:8443",
        ["WebAdmin:ThrottleKey"] = ThrottleKey, ["WebAdmin:Host"] = "127.0.0.1", ["WebAdmin:Port"] = "5432",
        ["WebAdmin:Database"] = "configuration_only_no_connection", ["WebAdmin:Username"] = AdminUsername,
        ["WebAdmin:Password"] = AdminPassword, ["MobileAuth:SigningKey"] = SigningKey, ["MobileAuth:OtpKey"] = OtpKey
    };

    private static Dictionary<string, string?> With(Dictionary<string, string?> source, string key, string? value) =>
        new(source, StringComparer.Ordinal) { [key] = value };

    private static string Key(byte value) => Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray());

    private static void Assert(Dictionary<string, string?> values, bool expected, string message)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWebAdmin(configuration);
        using var provider = services.BuildServiceProvider();
        var accepted = true;
        try { _ = provider.GetRequiredService<IOptions<WebAdminOptions>>().Value; }
        catch (OptionsValidationException) { accepted = false; }
        Check.Equal(expected, accepted, message);
    }
}
