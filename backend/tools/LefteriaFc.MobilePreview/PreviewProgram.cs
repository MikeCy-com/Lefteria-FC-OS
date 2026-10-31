using System.Net;
using System.Security.Cryptography;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using LefteriaFc.Identity.IntegrationTests;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.MobilePreview;

internal static class PreviewProgram
{
    public static async Task<int> Main(string[] args)
    {
        TestDatabase? database = null;
        PreviewApiFactory? factory = null;
        WebApplication? app = null;
        var exitCode = 0;
        try
        {
            var options = PreviewOptions.Parse(args);
            var fixturePath = Environment.GetEnvironmentVariable("LEFTERIA_TEST_FIXTURE");
            if (string.IsNullOrWhiteSpace(fixturePath))
                throw new PreviewUsageException("LEFTERIA_TEST_FIXTURE must name the guarded disposable identity fixture JSON.");
            var fixture = await FixtureConfiguration.LoadAsync(fixturePath);
            await fixture.VerifyServerAsync();
            var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var otpKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            database = new TestDatabase(fixture, otpKey);
            database.TrackClient(250);
            await database.AssertRestrictedApplicationRoleAsync();
            CalendarDatabaseFixture? calendar = null;
            PhoneNumber phone;
            IReadOnlyCollection<PhoneNumber> previewPhones;
            if (options.Calendar)
            {
                calendar = await database.SeedPreviewCalendarAsync();
                phone = calendar.Accounts.PlayerPhone;
                previewPhones = [phone, calendar.Accounts.ParentPhone, calendar.Accounts.StaffPhone];
            }
            else
            {
                phone = database.NewPhone();
                previewPhones = [phone];
                var adminId = await database.SeedAdminAsync(OrganizationIds.Club, OrganizationIds.Academy);
                foreach (var role in Enum.GetValues<MobileRole>())
                    await database.SeedIdentityAsync(phone, role, "Δοκιμαστικός Χρήστης",
                        role == MobileRole.Management ? adminId : null);
            }
            await SeedFanDetailsAsync(database, phone);
            factory = new PreviewApiFactory(fixture, previewPhones, signingKey, otpKey);
            using (var scope = factory.Services.CreateScope())
            {
                var configured = scope.ServiceProvider.GetRequiredService<LefteriaDbContext>();
                FixtureConfiguration.ValidateConnection(configured.Database.GetConnectionString(), fixture.AppUsername);
            }
            using var api = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost"), HandleCookies = false, AllowAutoRedirect = false
            });
            using var web = new HttpClient(new SocketsHttpHandler
            {
                UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(3)
            }) { Timeout = TimeSpan.FromSeconds(30) };
            app = PreviewProxy.Create(options, api, web);
            await app.StartAsync();
            PreviewProxy.AssertSingleLoopbackListener(app, options);
            Console.WriteLine($"Preview URL: {options.Origin.GetLeftPart(UriPartial.Authority)}/app/login");
            if (calendar is null) Console.WriteLine($"Fictional phone: {phone.WireValue}");
            else
            {
                Console.WriteLine($"Fictional player/fan phone: {phone.WireValue}");
                Console.WriteLine($"Fictional parent phone: {calendar.Accounts.ParentPhone.WireValue}");
                Console.WriteLine($"Fictional coach/management phone: {calendar.Accounts.StaffPhone.WireValue}");
                Console.WriteLine("Fictional calendar month: 2026-10 (December has the pagination scenario).");
            }
            Console.WriteLine($"Fixture code: {FixedFixtureOtpProtector.Code} (this fictional fixture only; no SMS is sent).");
            if (options.Smoke)
            {
                await PreviewSmoke.RunAsync(options, phone, calendar);
                await app.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Console.WriteLine("PASS local authenticated preview smoke.");
            }
            else await app.WaitForShutdownAsync();
        }
        catch (PreviewUsageException exception)
        {
            // Only our argument errors are printed; external errors may contain secrets.
            Console.Error.WriteLine(exception.Message);
            exitCode = 2;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Local preview failed. Check the guarded fixture, applied schema, and free loopback port.");
            exitCode = 1;
        }
        finally
        {
            try
            {
                if (app is not null)
                {
                    await app.StopAsync(TimeSpan.FromSeconds(10));
                    await app.DisposeAsync();
                }
                if (factory is not null) await factory.DisposeAsync();
            }
            catch (Exception)
            {
                Console.Error.WriteLine("Local preview host shutdown failed.");
                exitCode = 1;
            }
            if (database is not null)
            {
                try { await database.CleanupAsync(); }
                catch (Exception)
                {
                    Console.Error.WriteLine("Exact preview-row cleanup failed; inspect the disposable fixture before reuse.");
                    exitCode = 1;
                }
            }
        }
        return exitCode;
    }

    private static async Task SeedFanDetailsAsync(TestDatabase database, PhoneNumber phone)
    {
        await using var context = database.CreateContext();
        var fan = await context.MobileIdentities.SingleAsync(row => row.NormalizedPhone == phone.Digits && row.Role == "fan");
        fan.FirstName = "Δοκιμαστικός";
        fan.LastName = "Χρήστης";
        fan.Birthday = new DateOnly(1990, 1, 1);
        fan.Email = "preview@example.invalid";
        fan.FavoriteTeam = "both";
        fan.NotificationPreferencesJson = "{\"news\":true,\"matches\":false}";
        await context.SaveChangesAsync();
    }
}
