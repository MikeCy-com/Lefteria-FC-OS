using System.Net;
using System.Text.Json;
using LefteriaFc.Identity.IntegrationTests;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.MobilePreview;

internal static class WebPlatformPreviewProgram
{
    private enum Phase { Arguments, FixtureLoad, FixtureServer, AuthMetadata, AuthServer, Factory, Seed, Listener, Smoke, Shutdown }

    public static async Task<int> RunAsync(string[] args)
    {
        WebApplication? app = null;
        WebAdminApplicationFactory? factory = null;
        WebAdminDatabaseFixture? database = null;
        WebPlatformPreviewClubFixture? clubs = null;
        var exitCode = 0;
        var phase = Phase.Arguments;
        try
        {
            var options = WebPlatformPreviewOptions.Parse(args);
            var path = Environment.GetEnvironmentVariable("LEFTERIA_TEST_FIXTURE");
            if (string.IsNullOrWhiteSpace(path))
                throw new PreviewUsageException("LEFTERIA_TEST_FIXTURE must name the guarded disposable identity fixture JSON.");
            phase = Phase.FixtureLoad;
            var fixture = await FixtureConfiguration.LoadAsync(path);
            phase = Phase.FixtureServer;
            await fixture.VerifyServerAsync();
            phase = Phase.AuthMetadata;
            var metadata = JsonSerializer.Deserialize<AuthMetadata>(await File.ReadAllTextAsync(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException();
            if (string.IsNullOrWhiteSpace(metadata.AdminAuthUsername) || metadata.AdminAuthUsername == fixture.AdminUsername ||
                metadata.AdminAuthUsername == fixture.AppUsername) throw new InvalidOperationException();
            FixtureConfiguration.ValidateConnection(metadata.AdminAuthConnectionString, metadata.AdminAuthUsername);
            var auth = new NpgsqlConnectionStringBuilder(metadata.AdminAuthConnectionString);
            var owner = new NpgsqlConnectionStringBuilder(fixture.AdminConnectionString);
            var mobile = new NpgsqlConnectionStringBuilder(fixture.AppConnectionString);
            if (auth.Password is not { Length: >= 32 } || auth.Password == owner.Password || auth.Password == mobile.Password)
                throw new InvalidOperationException();
            phase = Phase.AuthServer;
            await VerifyAuthServerAsync(metadata.AdminAuthConnectionString!, metadata.AdminAuthUsername);

            phase = Phase.Factory;
            factory = new WebAdminApplicationFactory(fixture, metadata.AdminAuthConnectionString!, metadata.AdminAuthUsername);
            using (var scope = factory.Services.CreateScope())
            {
                FixtureConfiguration.ValidateConnection(scope.ServiceProvider.GetRequiredService<LefteriaDbContext>()
                    .Database.GetConnectionString(), fixture.AppUsername);
                await using var connection = await scope.ServiceProvider.GetRequiredService<WebAdminConnections>().OpenAsync(CancellationToken.None);
                FixtureConfiguration.ValidateConnection(connection.ConnectionString, metadata.AdminAuthUsername);
            }
            database = new WebAdminDatabaseFixture(fixture, factory);
            database.TrackClient(249);
            phase = Phase.Seed;
            clubs = new WebPlatformPreviewClubFixture(fixture);
            await clubs.SeedAsync();
            var account = await database.SeedAsync(organizations: ["lefteria_fc", "lefteria_fc_academy"]);
            using var api = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri(WebAdminApplicationFactory.Origin), HandleCookies = false, AllowAutoRedirect = false });
            using var web = new HttpClient(new SocketsHttpHandler
            {
                UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(3)
            }) { Timeout = TimeSpan.FromSeconds(30) };
            app = WebPlatformPreviewProxy.Create(options, api, web, database);
            phase = Phase.Listener;
            await app.StartAsync();
            WebPlatformPreviewProxy.AssertLoopbackListener(app, options);
            Console.WriteLine($"Admin preview URL: {options.Origin.GetLeftPart(UriPartial.Authority)}/admin/login");
            Console.WriteLine($"Fictional username: {account.Username}");
            Console.WriteLine($"Fictional password: {WebAdminDatabaseFixture.Password}");
            Console.WriteLine("The fictional manager has both organizations. No SMS/provider call is enabled.");
            Console.WriteLine($"Club editor URL: {options.Origin.GetLeftPart(UriPartial.Authority)}/admin/club");
            Console.WriteLine("Both fictional public club profiles start at version zero. Saves affect this disposable preview only.");
            if (options.Smoke)
            {
                phase = Phase.Smoke;
                await WebPlatformPreviewSmoke.RunAsync(options, account, database);
                await app.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Console.WriteLine("PASS local web-platform real-cookie API smoke; browser cookie storage still requires browser verification.");
            }
            else await app.WaitForShutdownAsync();
            phase = Phase.Shutdown;
            if (factory.Sms.Calls != 0) throw new InvalidOperationException();
        }
        catch (PreviewUsageException error) { Console.Error.WriteLine(error.Message); exitCode = 2; }
        catch (PreviewSmokeException error) { Console.Error.WriteLine("Web-platform preview smoke failed: " + error.Message); exitCode = 1; }
        catch (Exception)
        { Console.Error.WriteLine($"Web-platform preview failed during {phase}. Check the guarded disposable fixture, applied schema/grants and free loopback port."); exitCode = 1; }
        finally
        {
            try
            {
                if (app is not null) { await app.StopAsync(TimeSpan.FromSeconds(10)); await app.DisposeAsync(); }
                if (factory is not null) await factory.DisposeAsync();
            }
            catch (Exception) { Console.Error.WriteLine("Web-platform preview shutdown failed."); exitCode = 1; }
            if (clubs is not null)
            {
                try { await clubs.CleanupAsync(); }
                catch (Exception) { Console.Error.WriteLine("Exact fictional public-club cleanup failed; inspect the disposable fixture before reuse."); exitCode = 1; }
            }
            if (database is not null)
            {
                try { await database.CleanupAsync(); }
                catch (Exception) { Console.Error.WriteLine("Exact fictional admin cleanup failed; inspect the disposable fixture before reuse."); exitCode = 1; }
            }
        }
        return exitCode;
    }

    internal static async Task VerifyAuthServerAsync(string connectionString, string username)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT current_database()=@database AND current_user=@username AND host(inet_server_addr())='127.0.0.1'
              AND inet_server_port()=@port AND NOT (r.rolinherit OR r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls)
              AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member=r.oid OR roleid=r.oid)
              AND NOT EXISTS (SELECT 1 FROM pg_database WHERE datdba=r.oid)
              AND NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner=r.oid)
              AND NOT EXISTS (SELECT 1 FROM pg_class WHERE relowner=r.oid AND relpersistence<>'t')
            FROM pg_roles r WHERE rolname=current_user
            """, connection);
        command.Parameters.AddWithValue("database", FixtureConfiguration.ExpectedDatabase);
        command.Parameters.AddWithValue("username", username); command.Parameters.AddWithValue("port", FixtureConfiguration.ExpectedPort);
        if (await command.ExecuteScalarAsync() is not true) throw new InvalidOperationException();
    }

    private sealed class AuthMetadata
    {
        public string? AdminAuthUsername { get; init; }
        public string? AdminAuthConnectionString { get; init; }
    }
}
