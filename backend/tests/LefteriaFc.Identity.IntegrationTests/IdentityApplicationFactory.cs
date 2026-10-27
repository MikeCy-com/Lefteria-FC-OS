using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Identity;
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
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Roster;

[assembly: WebApplicationFactoryContentRoot("LefteriaFc.Api", "../../../../../src/LefteriaFc.Api", "LefteriaFc.Api.csproj", "0")]

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed class IdentityApplicationFactory(FixtureConfiguration fixture,bool enableLegacyBridge=false) : WebApplicationFactory<global::Program>
{
    public const string Issuer = "lefteria-identity-http-tests";
    public const string Audience = "lefteria-identity-http-test-client";
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string OtpKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string LegacySigningSecret { get; } = "isolated-legacy-test-secret-"+Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string LegacyFingerprintKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public DateTimeOffset LegacyStartsAt { get; } = DateTimeOffset.UtcNow.AddMinutes(-5);
    public DateTimeOffset LegacyEndsAt { get; } = DateTimeOffset.UtcNow.AddHours(2);
    public CapturingSmsSender Sms { get; } = new();
    public GatedAvatarSanitizer Avatars { get; } = new();
    public RosterQueryGate RosterGate { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = fixture.AppConnectionString,
                ["MobileAuth:SigningKey"] = SigningKey,
                ["MobileAuth:OtpKey"] = OtpKey,
                ["MobileAuth:Issuer"] = Issuer,
                ["MobileAuth:Audience"] = Audience,
                ["LegacyMobileBridge:Enabled"] = enableLegacyBridge.ToString(),
                ["LegacyMobileBridge:SigningSecret"] = enableLegacyBridge?LegacySigningSecret:"",
                ["LegacyMobileBridge:FingerprintKey"] = enableLegacyBridge?LegacyFingerprintKey:"",
                ["LegacyMobileBridge:StartsAt"] = enableLegacyBridge?LegacyStartsAt.ToString("O"):null,
                ["LegacyMobileBridge:EndsAt"] = enableLegacyBridge?LegacyEndsAt.ToString("O"):null,
                ["Twilio:AccountSid"] = "", ["Twilio:AuthToken"] = "", ["Twilio:FromNumber"] = ""
            }));
        builder.ConfigureTestServices(services =>
        {
            // No fake-sender path exists in product configuration. This replacement
            // is confined to the test host and cannot perform an external SMS call.
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(Sms);
            services.RemoveAll<IAvatarImageSanitizer>();
            services.AddSingleton<IAvatarImageSanitizer>(Avatars);
            services.RemoveAll<IRosterQueries>();
            services.AddScoped<IRosterQueries>(provider => new GatedRosterQueries(
                new PostgresRosterQueries(provider.GetRequiredService<LefteriaDbContext>()), RosterGate));
            services.RemoveAll<LefteriaDbContext>();
            services.AddScoped(_ => new LefteriaDbContext(new DbContextOptionsBuilder<LefteriaDbContext>()
                .UseNpgsql(fixture.AppConnectionString).Options));
            services.AddSingleton<IStartupFilter, TrustedTestRemoteIpFilter>();
        });
    }
}

internal sealed class RosterQueryGate
{
    private readonly ConcurrentQueue<Gate> gates = new();
    public Gate HoldNext() { var gate = new Gate(); gates.Enqueue(gate); return gate; }
    public async Task WaitAsync(CancellationToken token)
    {
        if (!gates.TryDequeue(out var gate)) return;
        gate.Entered.TrySetResult();
        await gate.Release.Task.WaitAsync(token);
    }
    internal sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
internal sealed class GatedRosterQueries(IRosterQueries inner, RosterQueryGate gate) : IRosterQueries
{
    public async Task<RosterPickerSnapshot> GetFirstTeamPickerAsync(MobileSession session, CancellationToken token)
    {
        await gate.WaitAsync(token);
        return await inner.GetFirstTeamPickerAsync(session, token);
    }
}

// Real native processing with a test-only gate AFTER decode. This permits
// revoking eligibility before the product's fresh binding transaction begins.
internal sealed class GatedAvatarSanitizer : IAvatarImageSanitizer
{
    private readonly SkiaAvatarImageSanitizer inner = new();
    private readonly ConcurrentQueue<Gate> gates = new();
    private int calls;
    public int Calls => Volatile.Read(ref calls);
    public Gate HoldNext() { var gate = new Gate(); gates.Enqueue(gate); return gate; }
    public SanitizedMobileAvatar Sanitize(ReadOnlyMemory<byte> input, string? contentType, CancellationToken token)
    {
        Interlocked.Increment(ref calls);
        var sanitized = inner.Sanitize(input, contentType, token);
        if (gates.TryDequeue(out var gate))
        {
            gate.Entered.TrySetResult();
            gate.Release.Task.WaitAsync(token).GetAwaiter().GetResult();
        }
        return sanitized;
    }
    internal sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

internal sealed class TrustedTestRemoteIpFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, continuation) =>
        {
            var value = context.Request.Headers["X-Identity-Test-Client"].ToString();
            if (!int.TryParse(value, out var client) || client is < 1 or > 250)
                throw new InvalidOperationException("A trusted test client identifier is required.");
            context.Connection.RemoteIpAddress = IPAddress.Parse($"192.0.2.{client}");
            return continuation(context);
        });
        next(app);
    };
}

internal sealed class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> messages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DeliveryGate> gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> failures = new(StringComparer.Ordinal);

    public Task<bool> SendOtpAsync(PhoneNumber phone, string code, MobileRole? detectedRole,
        CancellationToken cancellationToken)
    {
        _ = detectedRole;
        messages.GetOrAdd(phone.Digits, _ => new()).Enqueue(code);
        if (gates.TryRemove(phone.Digits, out var gate))
        {
            gate.Entered.TrySetResult();
            return gate.Result.Task.WaitAsync(cancellationToken);
        }
        return Task.FromResult(!failures.TryRemove(phone.Digits, out _));
    }

    public string Latest(PhoneNumber phone) => messages.TryGetValue(phone.Digits, out var queue) &&
        queue.TryPeek(out _) ? queue.ToArray()[^1] : throw new InvalidOperationException("No test SMS was captured.");
    public int Count(PhoneNumber phone) => messages.TryGetValue(phone.Digits, out var queue) ? queue.Count : 0;
    public void FailNext(PhoneNumber phone) => failures[phone.Digits] = true;
    public DeliveryGate HoldNext(PhoneNumber phone)
    {
        var gate = new DeliveryGate();
        if (!gates.TryAdd(phone.Digits, gate)) throw new InvalidOperationException("A test delivery is already held.");
        return gate;
    }

    internal sealed class DeliveryGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
