using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using LefteriaFc.Identity.IntegrationTests;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.MobilePreview;

// Persistent preview-workspace host. Seeds the existing fictional fixtures once
// and never deletes them; binds only 127.0.0.1 behind the workspace bridge.
internal static class WorkspaceHostProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return args switch
            {
                ["serve"] => await ServeAsync(),
                ["seed-management"] => await SeedManagementAsync(),
                ["import-public", var directory] => await WorkspacePublicImport.RunAsync((await LoadFixtureAsync()).Fixture, directory),
                _ => throw new PreviewUsageException("Usage: MobilePreview --workspace serve|seed-management|import-public <manifest-dir>")
            };
        }
        catch (PreviewUsageException error) { Console.Error.WriteLine(error.Message); return 2; }
        catch (WorkspaceException error) { Console.Error.WriteLine(error.Message); return 3; }
    }

    private static async Task<int> ServeAsync()
    {
        var (fixture, authConnection) = await LoadFixtureAsync();
        var settings = new WorkspaceSettings(Required("LEFTERIA_WORKSPACE_PUBLIC_ORIGIN"), Required("MOBILE_SIGNING_KEY"),
            Required("MOBILE_OTP_KEY"), Required("WEB_ADMIN_THROTTLE_KEY"), Required("WEB_ADMIN_FOUNDING_SETUP_CODE"),
            Required("LEFTERIA_WORKSPACE_KNOWN_PROXY"));
        if (!int.TryParse(Required("LEFTERIA_WORKSPACE_API_PORT"), out var port) || port is < 1024 or > 65535)
            throw new PreviewUsageException("LEFTERIA_WORKSPACE_API_PORT must be an unprivileged port.");
        var database = new TestDatabase(fixture, settings.OtpKey);
        await database.AssertRestrictedApplicationRoleAsync();
        var state = await EnsureSeededAsync(fixture, database);

        await using var factory = new WorkspaceApiFactory(fixture, authConnection, state.Phones(), settings);
        factory.UseKestrel(server => server.Listen(IPAddress.Loopback, port));
        factory.StartServer();
        var addresses = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null || addresses.Count != 1 || addresses.Single() != $"http://127.0.0.1:{port}")
            throw new WorkspaceException("The workspace API must have only its explicit loopback listener.");

        Console.WriteLine($"Workspace API listening on http://127.0.0.1:{port} (loopback only).");
        Console.WriteLine($"Fictional player/fan phone: {state.PlayerPhone}");
        Console.WriteLine($"Fictional parent phone: {state.ParentPhone}");
        Console.WriteLine($"Fictional coach/management phone: {state.StaffPhone}" +
            (state.ManagementSeeded ? "" : " (management role deferred until owner setup)"));
        Console.WriteLine($"Fixture code: {FixedFixtureOtpProtector.Code} (fictional phones only; no SMS is sent).");

        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; stop.TrySetResult(); });
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => { context.Cancel = true; stop.TrySetResult(); });
        factory.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stop.TrySetResult());
        await stop.Task;
        Console.WriteLine("Workspace API stopping; persisted demo data is kept.");
        return 0;
    }

    private static async Task<WorkspaceState> EnsureSeededAsync(FixtureConfiguration fixture, TestDatabase database)
    {
        var path = Required("LEFTERIA_WORKSPACE_STATE");
        var pending = path + ".pending";
        if (await WorkspaceState.LoadAsync(path) is { } existing)
        {
            await using var context = database.CreateContext();
            var digits = existing.Phones().Select(phone => phone.Digits).ToArray();
            if (await context.MobileIdentities.Select(row => row.NormalizedPhone).Distinct().CountAsync(phone => digits.Contains(phone)) != 3)
                throw new WorkspaceException("Persisted demo state does not match the database; refusing to reseed.");
            return existing;
        }
        if (File.Exists(pending))
            throw new WorkspaceException("A previous demo seed did not finish; the bootstrap must reset the disposable database.");
        await File.WriteAllTextAsync(pending, DateTimeOffset.UtcNow.ToString("O"));
        var accounts = await database.SeedWorkspaceCalendarAsync();
        await PreviewProgram.SeedFanDetailsAsync(database, accounts.PlayerPhone);
        await new WebPlatformPreviewClubFixture(fixture).SeedAsync();
        var state = new WorkspaceState
        {
            PlayerPhone = accounts.PlayerPhone.WireValue, ParentPhone = accounts.ParentPhone.WireValue,
            StaffPhone = accounts.StaffPhone.WireValue, Prefix = accounts.Prefix, SeededAt = DateTimeOffset.UtcNow
        };
        await state.SaveAsync(path);
        File.Delete(pending);
        Console.WriteLine("Seeded the existing fictional roster, staff, accounts, Oct-2026 calendar and club profiles.");
        return state;
    }

    private static async Task<int> SeedManagementAsync()
    {
        var (fixture, _) = await LoadFixtureAsync();
        var path = Required("LEFTERIA_WORKSPACE_STATE");
        var state = await WorkspaceState.LoadAsync(path) ?? throw new WorkspaceException("Demo data is not seeded yet; start the backend first.");
        var database = new TestDatabase(fixture, Required("MOBILE_OTP_KEY"));
        var staff = PhoneNumber.Parse(state.StaffPhone);
        await using (var context = database.CreateContext())
        {
            if (await context.MobileIdentities.AnyAsync(row => row.NormalizedPhone == staff.Digits && row.Role == "management"))
            {
                state.ManagementSeeded = true;
                await state.SaveAsync(path);
                Console.WriteLine($"Demo management login already present for {state.StaffPhone}.");
                return 0;
            }
        }
        await using (var connection = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM identity.founding_admin)", connection);
            if (await command.ExecuteScalarAsync() is not true)
                throw new WorkspaceException("Owner setup is not complete. Create the owner account at /admin/login first; nothing was changed.");
        }
        // Existing fixture helpers: management admin with both organizations, linked management role.
        var adminId = await database.SeedAdminAsync(OrganizationIds.Club, OrganizationIds.Academy);
        await database.SeedIdentityAsync(staff, MobileRole.Management, "Δοκιμαστικός Χρήστης", adminId);
        state.ManagementSeeded = true;
        await state.SaveAsync(path);
        Console.WriteLine($"Seeded demo management login for {state.StaffPhone} (FC and Academy).");
        return 0;
    }

    private static async Task<(FixtureConfiguration Fixture, string AuthConnection)> LoadFixtureAsync()
    {
        var path = Required("LEFTERIA_TEST_FIXTURE");
        var fixture = await FixtureConfiguration.LoadAsync(path);
        await fixture.VerifyServerAsync();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var username = document.RootElement.GetProperty("adminAuthUsername").GetString();
        var connection = document.RootElement.GetProperty("adminAuthConnectionString").GetString();
        if (string.IsNullOrWhiteSpace(username) || username == fixture.AdminUsername || username == fixture.AppUsername)
            throw new WorkspaceException("The web-admin auth role must be separate from the owner and app roles.");
        FixtureConfiguration.ValidateConnection(connection, username);
        var auth = new NpgsqlConnectionStringBuilder(connection);
        if (auth.Password is not { Length: >= 32 } || auth.Password == new NpgsqlConnectionStringBuilder(fixture.AppConnectionString).Password)
            throw new WorkspaceException("The web-admin auth credentials must be independent.");
        await WebPlatformPreviewProgram.VerifyAuthServerAsync(connection!, username);
        return (fixture, connection!);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new PreviewUsageException($"{name} is required.");
}

internal sealed class WorkspaceException(string message) : Exception(message);

internal sealed class WorkspaceState
{
    public required string PlayerPhone { get; init; }
    public required string ParentPhone { get; init; }
    public required string StaffPhone { get; init; }
    public required string Prefix { get; init; }
    public DateTimeOffset SeededAt { get; init; }
    public bool ManagementSeeded { get; set; }

    public IReadOnlyCollection<PhoneNumber> Phones() =>
        [PhoneNumber.Parse(PlayerPhone), PhoneNumber.Parse(ParentPhone), PhoneNumber.Parse(StaffPhone)];

    public static async Task<WorkspaceState?> LoadAsync(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<WorkspaceState>(await File.ReadAllTextAsync(path)) : null;

    public async Task SaveAsync(string path)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
