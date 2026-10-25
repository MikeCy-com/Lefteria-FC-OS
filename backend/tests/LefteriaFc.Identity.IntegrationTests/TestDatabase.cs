using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed class TestDatabase(FixtureConfiguration fixture, string otpKey)
{
    private readonly HashSet<string> phones = new(StringComparer.Ordinal);
    private readonly List<Guid> adminIds = [];
    private readonly HashSet<int> clients = [];
    private readonly string phoneRunPrefix = "999" + RandomNumberGenerator.GetInt32(100_000_000, 999_999_999);
    private int phoneSequence;

    public PhoneNumber NewPhone()
    {
        var phone = PhoneNumber.Parse(phoneRunPrefix + (++phoneSequence).ToString("D3"));
        phones.Add(phone.Digits);
        return phone;
    }

    public void TrackClient(int client) => clients.Add(client);

    public LefteriaDbContext CreateContext() => new(new DbContextOptionsBuilder<LefteriaDbContext>()
        .UseNpgsql(fixture.AdminConnectionString, options => options.CommandTimeout(10)).Options);

    public async Task<Guid> SeedIdentityAsync(PhoneNumber phone, MobileRole role,
        string name = "Private formal identity fixture", Guid? adminId = null)
    {
        await using var database = CreateContext();
        var id = Guid.NewGuid();
        database.MobileIdentities.Add(new MobileIdentityRecord
        {
            Id = id, NormalizedPhone = phone.Digits, Role = role.ToWireValue(),
            DisplayName = name, ManagementAdminId = adminId, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), LastLogin = DateTimeOffset.UtcNow.AddHours(-1),
            Platform = "Web/PWA"
        });
        await database.SaveChangesAsync();
        return id;
    }

    public async Task<Guid> SeedAdminAsync(params string[] organizationIds)
    {
        await using var database = CreateContext();
        var id = Guid.NewGuid();
        adminIds.Add(id);
        database.AdminAccounts.Add(new AdminAccountRecord
        {
            Id = id, Username = "http-fixture-" + id.ToString("N"), DisplayName = "Fixture management",
            Role = "manager", IsActive = true
        });
        await database.SaveChangesAsync();
        database.AdminOrganizationGrants.AddRange(organizationIds.Select(organization => new AdminOrganizationGrantRecord
            { AdminAccountId = id, OrganizationId = organization }));
        await database.SaveChangesAsync();
        return id;
    }

    public async Task<OtpChallengeRecord?> ChallengeAsync(PhoneNumber phone)
    {
        await using var database = CreateContext();
        return await database.OtpChallenges.AsNoTracking().SingleOrDefaultAsync(row => row.NormalizedPhone == phone.Digits);
    }

    public async Task<MobileIdentityRecord?> IdentityAsync(PhoneNumber phone, MobileRole role)
    {
        await using var database = CreateContext();
        var wireRole = role.ToWireValue();
        return await database.MobileIdentities.AsNoTracking().SingleOrDefaultAsync(row =>
            row.NormalizedPhone == phone.Digits && row.Role == wireRole);
    }

    public async Task SetIdentityActiveAsync(Guid id, bool active)
    {
        await using var database = CreateContext();
        await database.MobileIdentities.Where(row => row.Id == id).ExecuteUpdateAsync(setters =>
            setters.SetProperty(row => row.IsActive, active));
    }

    public async Task SetAdminActiveAsync(Guid id, bool active)
    {
        await using var database = CreateContext();
        await database.AdminAccounts.Where(row => row.Id == id).ExecuteUpdateAsync(setters =>
            setters.SetProperty(row => row.IsActive, active));
    }

    public async Task<string?> ActiveOrganizationAsync(Guid id)
    {
        await using var database = CreateContext();
        return await database.AdminAccounts.Where(row => row.Id == id).Select(row => row.ActiveOrganizationId).SingleAsync();
    }

    public async Task RemoveGrantAsync(Guid id, string organization)
    {
        await using var database = CreateContext();
        await database.AdminOrganizationGrants.Where(row => row.AdminAccountId == id && row.OrganizationId == organization)
            .ExecuteDeleteAsync();
    }

    public async Task ExpireChallengeAsync(PhoneNumber phone)
    {
        await using var database = CreateContext();
        var now = DateTimeOffset.UtcNow;
        await database.OtpChallenges.Where(row => row.NormalizedPhone == phone.Digits).ExecuteUpdateAsync(setters =>
            setters.SetProperty(row => row.CreatedAt, now.AddMinutes(-10))
                .SetProperty(row => row.ExpiresAt, now.AddMinutes(-5)));
    }

    public async Task<int> CountLoginAuditsAsync(Guid identityId)
    {
        await using var database = CreateContext();
        return await database.SecurityAuditEvents.CountAsync(row => row.ActorIdentityId == identityId && row.Action == "mobile.login" && row.Outcome == "success");
    }

    public async Task AssertRestrictedApplicationRoleAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole,
                   has_schema_privilege(current_user, 'identity', 'USAGE'),
                   NOT has_schema_privilege(current_user, 'identity', 'CREATE'),
                   has_table_privilege(current_user, 'audit.security_events', 'INSERT'),
                   NOT has_table_privilege(current_user, 'audit.security_events', 'UPDATE'),
                   NOT has_table_privilege(current_user, 'audit.security_events', 'DELETE')
            FROM pg_roles WHERE rolname = current_user
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Check.True(await reader.ReadAsync(), "Restricted application role was not found.");
        for (var column = 0; column < 6; column++)
            Check.True(reader.GetBoolean(column), "The application role has unexpected database privileges.");
    }

    public async Task AssertRejectedSqlAsync(string sql, string expectedState, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException exception)
        {
            await transaction.RollbackAsync();
            Check.Equal(expectedState, exception.SqlState, "Unexpected PostgreSQL constraint result.");
            return;
        }
        await transaction.RollbackAsync();
        throw new InvalidOperationException("PostgreSQL accepted a row that should violate an identity constraint.");
    }

    public async Task CleanupAsync()
    {
        // These are exact generated test identities, never a schema/table-wide delete.
        await using var database = CreateContext();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var ownedPhones = phones.ToArray();
        await database.OtpChallenges.Where(row => ownedPhones.Contains(row.NormalizedPhone)).ExecuteDeleteAsync();
        await database.MobileIdentities.Where(row => ownedPhones.Contains(row.NormalizedPhone)).ExecuteDeleteAsync();
        await database.AdminAccounts.Where(row => adminIds.Contains(row.Id)).ExecuteUpdateAsync(setters =>
            setters.SetProperty(row => row.ActiveOrganizationId, (string?)null));
        await database.AdminOrganizationGrants.Where(row => adminIds.Contains(row.AdminAccountId)).ExecuteDeleteAsync();
        await database.AdminAccounts.Where(row => adminIds.Contains(row.Id)).ExecuteDeleteAsync();
        var fingerprints = clients.SelectMany(client => new[]
        {
            Fingerprint("OtpRequest", $"ip:192.0.2.{client}"),
            Fingerprint("OtpVerify", $"192.0.2.{client}")
        }).Concat(phones.Select(phone => Fingerprint("OtpRequest", "phone:" + phone))).ToArray();
        await database.AuthRateBuckets.Where(row => fingerprints.Contains(row.Key)).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        // Security audit events intentionally remain append-only in the disposable fixture.
    }

    private string Fingerprint(string scope, string key) => Convert.ToHexString(HMACSHA256.HashData(
        Convert.FromBase64String(otpKey), Encoding.UTF8.GetBytes($"lefteria-auth-throttle-v1\n{scope}\n{key}"))).ToLowerInvariant();
}

internal static class Check
{
    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message);
    }
}
