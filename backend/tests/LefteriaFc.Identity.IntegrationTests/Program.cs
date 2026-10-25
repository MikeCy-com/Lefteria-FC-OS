using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class TestRunner
{
    public static async Task<int> Main(string[] args)
    {
        var fixturePath = args.Length == 2 && args[0] == "--fixture"
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
        var checks = new IdentityHttpChecks(factory, database).Cases;
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
                    Console.Error.WriteLine($"FAIL {name}: {exception.GetType().Name}: {exception.Message}");
                }
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
        Console.WriteLine($"{passed}/{checks.Count} HTTP/PostgreSQL identity checks passed; {failures} failures.");
        return failures == 0 ? 0 : 1;
    }
}
