using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

// Only the owner connection seeds/removes these random fictional accounts.
// API requests use the independently validated web-admin role in the factory.
internal sealed partial class WebAdminDatabaseFixture(FixtureConfiguration fixture, WebAdminApplicationFactory factory)
{
    public const string Password = "fixture-password-26";
    public const string PasswordHash = "$2b$04$......................zZBj5OGxtMOVwSKwuhgTqOM4l8/c6/O";
    public const string SpacedPassword = "  fixture password  ";
    public const string SpacedPasswordHash = "$2b$04$......................jisM.i3.PCwc.shsoBrSJD4AMnC8.Ju";
    private readonly HashSet<Guid> accounts = [];
    private readonly HashSet<string> subjects = new(StringComparer.Ordinal);
    private readonly HashSet<string> buckets = new(StringComparer.Ordinal);

    public void TrackUsername(string username)
    {
        var subject = factory.Fingerprint("admin-username", username);
        subjects.Add(subject);
        buckets.Add(subject);
    }

    public void TrackClient(int client) => buckets.Add(factory.Fingerprint("admin-client", $"198.51.100.{client}"));

    public async Task<Account> SeedAsync(string role = "manager", bool active = true, bool invite = false, bool twoFactor = false,
        bool spacedPassword = false, params string[] organizations)
    {
        var account = new Account(Guid.NewGuid(), "WebAdminFixture-" + Guid.NewGuid().ToString("N"));
        accounts.Add(account.Id);
        TrackUsername(account.Username);
        await using var connection = await OwnerAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("""
            INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
            VALUES (@id,@username,'Fictional administrator fixture',@role,@active,@hash,@invite,@two_factor)
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", account.Id); command.Parameters.AddWithValue("username", account.Username);
            command.Parameters.AddWithValue("role", role); command.Parameters.AddWithValue("active", active);
            command.Parameters.AddWithValue("hash", spacedPassword ? SpacedPasswordHash : PasswordHash);
            command.Parameters.AddWithValue("invite", invite); command.Parameters.AddWithValue("two_factor", twoFactor);
            await command.ExecuteNonQueryAsync();
        }
        foreach (var organization in organizations)
        {
            await using var command = new NpgsqlCommand("INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id) VALUES (@id,@organization)", connection, transaction);
            command.Parameters.AddWithValue("id", account.Id); command.Parameters.AddWithValue("organization", organization);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return account;
    }

    public async Task SetStateAsync(Account account, string role = "manager", bool active = true, bool invite = false,
        bool twoFactor = false, string? passwordHash = null, string? activeOrganization = null)
    {
        RequireOwned(account);
        await using var connection = await OwnerAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE identity.admin_accounts SET role=@role,is_active=@active,invite_pending=@invite,
              otp2fa_enabled=@two_factor,password_hash=@hash,active_organization_id=@active_organization WHERE id=@id
            """, connection);
        command.Parameters.AddWithValue("id", account.Id); command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("active", active); command.Parameters.AddWithValue("invite", invite);
        command.Parameters.AddWithValue("two_factor", twoFactor); command.Parameters.AddWithValue("hash", passwordHash ?? PasswordHash);
        command.Parameters.Add(new("active_organization", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)activeOrganization ?? DBNull.Value });
        if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("The fictional admin update did not match its owned row.");
    }

    public async Task RevokeOrganizationAsync(Account account, string organization)
    {
        RequireOwned(account);
        await using var connection = await OwnerAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var clear = new NpgsqlCommand("UPDATE identity.admin_accounts SET active_organization_id=NULL WHERE id=@id AND active_organization_id=@organization", connection, transaction))
        {
            clear.Parameters.AddWithValue("id", account.Id); clear.Parameters.AddWithValue("organization", organization);
            await clear.ExecuteNonQueryAsync();
        }
        await using var command = new NpgsqlCommand("DELETE FROM identity.admin_organization_grants WHERE admin_account_id=@id AND organization_id=@organization", connection, transaction);
        command.Parameters.AddWithValue("id", account.Id); command.Parameters.AddWithValue("organization", organization);
        if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("The fictional organization grant was not removed.");
        await transaction.CommitAsync();
    }

    public async Task<int> SessionCountAsync(Account account)
    {
        RequireOwned(account);
        await using var connection = await OwnerAsync();
        await using var command = new NpgsqlCommand("SELECT count(*)::integer FROM identity.admin_sessions WHERE admin_account_id=@id", connection);
        command.Parameters.AddWithValue("id", account.Id);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    public async Task AssertSessionAsync(Account account, string token, int days)
    {
        RequireOwned(account);
        await using var connection = await OwnerAsync();
        await using var command = new NpgsqlCommand("""
            SELECT token_digest,credential_digest,expires_at-created_at,revoked_at IS NULL
            FROM identity.admin_sessions WHERE admin_account_id=@id AND token_digest=@digest
            """, connection);
        command.Parameters.AddWithValue("id", account.Id);
        command.Parameters.AddWithValue("digest", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token))).ToLowerInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetString(0).Length != 64 || reader.GetString(0) == token ||
            reader.GetString(1).Length != 64 || reader.GetString(1) == PasswordHash ||
            reader.GetFieldValue<TimeSpan>(2) != TimeSpan.FromDays(days) || !reader.GetBoolean(3) || await reader.ReadAsync())
            throw new InvalidOperationException("Only one exact-lifetime digest-backed fictional session should exist for the cookie.");
    }

    public async Task ExpireSessionAsync(Account account, string token)
    {
        RequireOwned(account);
        await using var connection = await OwnerAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE identity.admin_sessions SET created_at=statement_timestamp()-interval '2 days',
              expires_at=statement_timestamp()-interval '1 day'
            WHERE admin_account_id=@id AND token_digest=@digest AND revoked_at IS NULL
            """, connection);
        command.Parameters.AddWithValue("id", account.Id);
        command.Parameters.AddWithValue("digest", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token))).ToLowerInvariant());
        if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("The fictional session expiry update did not match one owned row.");
    }

    public async Task CleanupAsync()
    {
        await using var connection = await OwnerAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // An exact synthetic actor UUID or fresh-key subject digest is required.
        foreach (var sql in new[]
        {
            "DELETE FROM audit.security_events WHERE actor_realm='admin' AND (actor_identity_id=ANY(@ids) OR subject_digest=ANY(@subjects))",
            "DELETE FROM identity.admin_sessions WHERE admin_account_id=ANY(@ids)",
            "DELETE FROM identity.admin_auth_rate_buckets WHERE key=ANY(@buckets)",
            "UPDATE identity.admin_accounts SET active_organization_id=NULL WHERE id=ANY(@ids)",
            "DELETE FROM identity.admin_organization_grants WHERE admin_account_id=ANY(@ids)",
            "DELETE FROM identity.admin_accounts WHERE id=ANY(@ids)"
        })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("ids", accounts.ToArray());
            command.Parameters.AddWithValue("subjects", subjects.ToArray());
            command.Parameters.AddWithValue("buckets", buckets.ToArray());
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private async Task<NpgsqlConnection> OwnerAsync()
    {
        FixtureConfiguration.ValidateConnection(fixture.AdminConnectionString, fixture.AdminUsername);
        var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        try
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT current_database()=@database AND current_user=@owner AND host(inet_server_addr())='127.0.0.1' AND inet_server_port()=@port", connection);
            command.Parameters.AddWithValue("database", FixtureConfiguration.ExpectedDatabase);
            command.Parameters.AddWithValue("owner", fixture.AdminUsername); command.Parameters.AddWithValue("port", FixtureConfiguration.ExpectedPort);
            if (await command.ExecuteScalarAsync() is not true) throw new InvalidOperationException("Refusing an owner operation outside the exact disposable fixture.");
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private void RequireOwned(Account account)
    {
        if (!accounts.Contains(account.Id)) throw new InvalidOperationException("Refusing to change an unowned fictional account.");
    }

    internal sealed record Account(Guid Id, string Username);
}
