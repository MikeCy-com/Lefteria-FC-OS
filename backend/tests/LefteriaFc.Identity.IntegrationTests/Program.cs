using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class TestRunner
{
    public static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(["--public-fixture-clock-policy"], StringComparer.Ordinal))
        {
            try { PublicFixtureClockChecks.Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine("FAIL public fixture clock: " + error.Message); return 1; }
        }
        if (args.SequenceEqual(["--public-sections-policy"], StringComparer.Ordinal))
        {
            try { PublicSectionsPolicyChecks.Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine("FAIL public sections projection: " + error.GetType().Name); return 1; }
        }
        if (args.SequenceEqual(["--web-admin-configuration-policy"], StringComparer.Ordinal))
        {
            try { WebAdminConfigurationChecks.Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine("FAIL web admin configuration policy: " + error.GetType().Name); return 1; }
        }
        if (args.Length == 2 && args[0] == "--web-admin-fixture")
        {
            try
            {
                var adminFixture = await FixtureConfiguration.LoadAsync(args[1]);
                await adminFixture.VerifyServerAsync();
                await WebAdminHttpChecks.RunAsync(args[1], adminFixture);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("FAIL web-admin fixture: " + error.GetType().Name); return 1; }
        }
        if (args.SequenceEqual(["--web-admin-password-policy"], StringComparer.Ordinal))
        {
            try { await WebAdminPasswordChecks.RunAsync(); return 0; }
            catch (Exception error) { Console.Error.WriteLine("FAIL web admin password policy: " + error.GetType().Name + ": " + error.Message); return 1; }
        }
        if(args.SequenceEqual(["--legacy-token-policy"],StringComparer.Ordinal))
            return await IdentityHttpChecks.RunLegacyPolicyChecksAsync();
        if(args.SequenceEqual(["--sms-adapter-policy"],StringComparer.Ordinal))
        {
            try
            {
                await TwilioAdapterChecks.ConfiguredRequestAsync();
                await TwilioAdapterChecks.UnconfiguredDoesNotCallHttpAsync();
                Console.WriteLine("PASS isolated Twilio adapter checks; no provider network used.");
                return 0;
            }
            catch(Exception)
            {
                Console.Error.WriteLine("FAIL isolated Twilio adapter policy.");
                return 1;
            }
        }
        var sectionsOnly = args.Length == 2 && args[0] == "--public-sections-fixture";
        var fixturePath = args.Length == 2 && (args[0] == "--fixture" || sectionsOnly)
            ? args[1] : args.Length == 0 ? Environment.GetEnvironmentVariable("LEFTERIA_TEST_FIXTURE") : null;
        if (string.IsNullOrWhiteSpace(fixturePath))
        {
            Console.Error.WriteLine("Usage: identity integration checks --fixture <disposable identity-db.json>");
            return 2;
        }

        FixtureConfiguration fixture;
        try
        {
            fixture = await FixtureConfiguration.LoadAsync(fixturePath);
            await fixture.VerifyServerAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Fixture guard refused execution: {exception.GetType().Name}: {exception.Message}");
            return 2;
        }

        var factory = new IdentityApplicationFactory(fixture);
        var database = new TestDatabase(fixture, factory.OtpKey);
        var failures = 0;
        var passed = 0;
        var checks = new IdentityHttpChecks(factory, database).Cases
            .Where(check => !sectionsOnly || check.Name.StartsWith("Versioned public sections", StringComparison.Ordinal)).ToArray();
        try
        {
            // Validate the actual DI connection before any request can touch a database.
            using var scope = factory.Services.CreateScope();
            var configured = scope.ServiceProvider.GetRequiredService<LefteriaDbContext>();
            FixtureConfiguration.ValidateConnection(configured.Database.GetConnectionString(), fixture.AppUsername);
            foreach (var (name, check) in checks)
            {
                try
                {
                    await check();
                    passed++;
                    Console.WriteLine($"PASS {name}");
                }
                catch (Exception exception)
                {
                    failures++;
                    var reason=exception is LefteriaFc.Application.Roster.RosterImportApplyException refusal?$" ({refusal.Code})":"";
                    Console.Error.WriteLine($"FAIL {name}: {exception.GetType().Name}: {exception.Message}{reason}");
                }
            }
            try { if (!sectionsOnly) await WebAdminHttpChecks.RunAsync(fixturePath, fixture); }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine("FAIL web-admin HTTP/PostgreSQL checks: " + error.GetType().Name);
            }
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine($"FAIL HTTP host setup: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            await factory.DisposeAsync();
            try { await database.CleanupAsync(); }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL exact-owned-row cleanup: {exception.GetType().Name}: {exception.Message}");
            }
        }
        Console.WriteLine($"{passed}/{checks.Length} HTTP/PostgreSQL identity checks passed; {failures} failures.");
        return failures == 0 ? 0 : 1;
    }
}
