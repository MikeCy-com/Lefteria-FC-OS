using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Api.WebAdmin;

public sealed record FoundingAdminSetupStatus(bool Available);
public sealed record FoundingAdminSetupCommand(string? Username, string? Name, string? Password, string? SetupCode);

public static class FoundingAdminEndpoints
{
    public static void MapFoundingAdminSetup(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/auth/setup").WithTags("Web administration");
        group.MapGet("", async (IOptions<WebAdminOptions> options, WebAdminConnections connections,
            CancellationToken token) => TypedResults.Ok(new FoundingAdminSetupStatus(
                Configured(options.Value) && await AvailableAsync(connections, token))))
            .WithName("GetFoundingAdminSetupStatus");

        group.MapPost("", async (FoundingAdminSetupCommand body, IOptions<WebAdminOptions> options,
            WebAdminConnections connections, IWebAdminRepository repository, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken token) =>
        {
            if (!Configured(options.Value))
                throw new WebAdminException(404, "setup_closed", "Owner setup is unavailable.");
            var client = context.Connection.RemoteIpAddress;
            var address = client?.IsIPv4MappedToIPv6 == true ? client.MapToIPv4().ToString() : client?.ToString() ?? "unknown";
            if (!await repository.AcquireLoginRateAsync(secrets.Fingerprint("owner-setup-client", address),
                    secrets.Fingerprint("owner-setup", "first-owner"), token))
                throw new WebAdminException(429, "setup_rate_limited", "Too many attempts. Try again later.");
            if (body.SetupCode is not { Length: 64 } code ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(options.Value.FoundingSetupCode)))
                throw new WebAdminException(403, "setup_code_invalid", "Invalid owner setup code.");
            var username = body.Username?.Trim().ToLowerInvariant();
            var name = body.Name?.Trim();
            if (username is null || !Regex.IsMatch(username, @"\A[a-z0-9][a-z0-9._@+\-]{2,79}\z", RegexOptions.CultureInvariant) ||
                name is not { Length: > 0 and <= 200 } || name.Any(char.IsControl) ||
                body.Password is not { Length: >= 12 and <= 72 } password || password.Contains('\0') ||
                Encoding.UTF8.GetByteCount(password) > 72)
                throw new WebAdminException(422, "invalid_setup_details", "Enter a valid username, name and password.");

            if (!await AvailableAsync(connections, token))
                throw new WebAdminException(409, "setup_closed", "Owner setup is already closed.");

            var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
            await using var connection = await connections.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT identity.create_founding_admin(@id,@username,@name,@hash)";
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("username", username);
            command.Parameters.AddWithValue("name", name);
            command.Parameters.AddWithValue("hash", hash);
            if (await command.ExecuteScalarAsync(token) is not true)
                throw new WebAdminException(409, "setup_closed", "Owner setup is already closed.");
            return TypedResults.NoContent();
        }).WithName("CreateFoundingAdmin").Produces(204).Produces<WebAdminError>(400).Produces<WebAdminError>(403)
            .Produces<WebAdminError>(413).Produces<WebAdminError>(415)
            .Produces<WebAdminError>(404).Produces<WebAdminError>(409).Produces<WebAdminError>(422)
            .Produces<WebAdminError>(429).Produces<WebAdminError>(503);
    }

    private static bool Configured(WebAdminOptions options) => options.Enabled &&
        options.FoundingSetupCode.Length == 64 &&
        options.FoundingSetupCode.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;

    private static async Task<bool> AvailableAsync(WebAdminConnections connections, CancellationToken token)
    {
        await using var connection = await connections.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT identity.founding_admin_setup_available()";
        return await command.ExecuteScalarAsync(token) is true;
    }
}
