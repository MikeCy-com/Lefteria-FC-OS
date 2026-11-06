using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LefteriaFc.Infrastructure.Identity;

public sealed class PostgresAuthThrottle(LefteriaDbContext database, IOptions<MobileAuthOptions> options) : IAuthThrottle
{
    private readonly byte[] key = Convert.FromBase64String(options.Value.OtpKey);

    public async Task<AuthThrottleDecision> TryAcquireAsync(MobileAuthThrottleScope scope, string clientKey,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var capacity = scope switch
        {
            MobileAuthThrottleScope.OtpRequest => 5,
            MobileAuthThrottleScope.OtpVerify => 10,
            MobileAuthThrottleScope.LegacyExchange => 10,
            MobileAuthThrottleScope.FamilyInvitation => 10,
            MobileAuthThrottleScope.FamilyInvitationTarget => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        var windowSeconds = scope is MobileAuthThrottleScope.FamilyInvitation or MobileAuthThrottleScope.FamilyInvitationTarget ? 3600d : 60d;
        var fingerprint = Convert.ToHexString(HMACSHA256.HashData(key,
            Encoding.UTF8.GetBytes($"lefteria-auth-throttle-v1\n{scope}\n{clientKey}"))).ToLowerInvariant();
        await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = ((NpgsqlConnection)database.Database.GetDbConnection()).CreateCommand();
            // Database time coordinates all API instances. The app's clock cannot refill a bucket.
            command.CommandText = """
                INSERT INTO identity.auth_rate_buckets (key, tokens, updated_at)
                VALUES (@key, @capacity - 1, statement_timestamp())
                ON CONFLICT (key) DO UPDATE SET
                  tokens = LEAST(@capacity, auth_rate_buckets.tokens +
                    GREATEST(0, EXTRACT(EPOCH FROM (statement_timestamp() - auth_rate_buckets.updated_at))) * @refill) - 1,
                  updated_at = GREATEST(statement_timestamp(), auth_rate_buckets.updated_at)
                WHERE LEAST(@capacity, auth_rate_buckets.tokens +
                  GREATEST(0, EXTRACT(EPOCH FROM (statement_timestamp() - auth_rate_buckets.updated_at))) * @refill) >= 1
                RETURNING tokens
                """;
            command.Parameters.AddWithValue("key", fingerprint);
            command.Parameters.AddWithValue("capacity", (double)capacity);
            command.Parameters.AddWithValue("refill", capacity / windowSeconds);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is null or DBNull
                ? new AuthThrottleDecision(false, (int)Math.Ceiling(windowSeconds / capacity))
                : new AuthThrottleDecision(true);
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
        }
    }
}
