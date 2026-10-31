using System.Data;
using LefteriaFc.Application.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.WebAdmin;

public sealed class PostgresWebAdminRepository(WebAdminConnections connections) : IWebAdminRepository
{
    private const string AccountColumns = "a.id,a.username,a.display_name,a.role,a.password_hash,a.is_active,a.invite_pending,a.otp2fa_enabled";

    public async Task<bool> AcquireLoginRateAsync(string clientFingerprint, string usernameFingerprint, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        // Separate domain-separated buckets; both successful and failed attempts consume them.
        foreach (var (key, capacity, seconds) in new[] { (clientFingerprint, 10d, 60d), (usernameFingerprint, 5d, 300d) })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO identity.admin_auth_rate_buckets (key,tokens,updated_at)
                VALUES (@key,@capacity-1,statement_timestamp())
                ON CONFLICT (key) DO UPDATE SET
                  tokens=LEAST(@capacity,admin_auth_rate_buckets.tokens+
                    GREATEST(0,EXTRACT(EPOCH FROM (statement_timestamp()-admin_auth_rate_buckets.updated_at)))*@refill)-1,
                  updated_at=GREATEST(statement_timestamp(),admin_auth_rate_buckets.updated_at)
                WHERE LEAST(@capacity,admin_auth_rate_buckets.tokens+
                    GREATEST(0,EXTRACT(EPOCH FROM (statement_timestamp()-admin_auth_rate_buckets.updated_at)))*@refill)>=1
                RETURNING key
                """;
            command.Parameters.AddWithValue("key", key);
            command.Parameters.AddWithValue("capacity", capacity);
            command.Parameters.AddWithValue("refill", capacity / seconds);
            if (await command.ExecuteScalarAsync(cancellationToken) is null or DBNull) return false;
        }
        return true;
    }

    public async Task<WebAdminCredential?> FindCredentialAsync(string username, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT " + AccountColumns + " FROM identity.admin_accounts a WHERE a.username=@username";
        command.Parameters.AddWithValue("username", username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Account(reader, 0) : null;
    }

    public async Task<bool> CreateSessionAsync(Guid id, WebAdminCredential candidate, string tokenDigest, string credentialDigest,
        TimeSpan lifetime, string subjectDigest, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO identity.admin_sessions (id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
            SELECT @id,@token,a.id,@credential,statement_timestamp(),statement_timestamp()+@lifetime
            FROM identity.admin_accounts a WHERE a.id=@admin AND a.password_hash=@hash AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled AND a.role=@role
            RETURNING id
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("token", tokenDigest);
        command.Parameters.AddWithValue("credential", credentialDigest);
        command.Parameters.AddWithValue("lifetime", lifetime);
        command.Parameters.AddWithValue("admin", candidate.Id);
        command.Parameters.AddWithValue("hash", candidate.PasswordHash!);
        command.Parameters.AddWithValue("role", candidate.Role);
        var created = await command.ExecuteScalarAsync(cancellationToken) is Guid;
        await AuditAsync(connection, transaction, created ? candidate.Id : null, created ? id : null,
            "admin.login", created ? "succeeded" : "denied", subjectDigest, created ? null : "account_changed", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<StoredWebAdminSession?> FindSessionAsync(string tokenDigest, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        Guid id; string credential; DateTimeOffset expiry; WebAdminCredential account;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT s.id,s.credential_digest,s.expires_at," + AccountColumns + """
                 FROM identity.admin_sessions s JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                 WHERE s.token_digest=@token AND s.revoked_at IS NULL AND s.expires_at>statement_timestamp()
                """;
            command.Parameters.AddWithValue("token", tokenDigest);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            id = reader.GetGuid(0); credential = reader.GetString(1); expiry = reader.GetFieldValue<DateTimeOffset>(2);
            account = Account(reader, 3);
        }
        var organizations = new List<WebAdminOrganizationDto>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT o.id,o.name FROM identity.admin_organization_grants g JOIN club.organizations o ON o.id=g.organization_id
                WHERE g.admin_account_id=@admin AND o.id IN ('lefteria_fc','lefteria_fc_academy') ORDER BY o.id
                """;
            command.Parameters.AddWithValue("admin", account.Id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) organizations.Add(new(reader.GetString(0), reader.GetString(1)));
        }
        await transaction.CommitAsync(cancellationToken);
        return new(id, account, credential, expiry, organizations);
    }

    public async Task RevokeSessionAsync(string tokenDigest, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid? admin = null; Guid? session = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE identity.admin_sessions SET revoked_at=GREATEST(created_at,statement_timestamp())
                WHERE token_digest=@token AND revoked_at IS NULL RETURNING id,admin_account_id
                """;
            command.Parameters.AddWithValue("token", tokenDigest);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken)) { session = reader.GetGuid(0); admin = reader.GetGuid(1); }
        }
        if (session is not null) await AuditAsync(connection, transaction, admin, session, "admin.logout", "succeeded", null, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AuditDeniedAsync(string subjectDigest, string reason, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await AuditAsync(connection, null, null, null, "admin.login", "denied", subjectDigest, reason, cancellationToken);
    }

    private static WebAdminCredential Account(NpgsqlDataReader reader, int offset) => new(
        reader.GetGuid(offset), reader.GetString(offset + 1), reader.GetString(offset + 2), reader.GetString(offset + 3),
        reader.IsDBNull(offset + 4) ? null : reader.GetString(offset + 4), reader.GetBoolean(offset + 5),
        reader.GetBoolean(offset + 6), reader.GetBoolean(offset + 7));

    private static async Task AuditAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid? actor, Guid? session,
        string action, string outcome, string? subject, string? reason, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO audit.security_events (id,occurred_at,actor_identity_id,device_session_id,action,outcome,subject_digest,reason_code,actor_realm)
            VALUES (@id,statement_timestamp(),@actor,@session,@action,@outcome,@subject,@reason,'admin')
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.Add(new("actor", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)actor ?? DBNull.Value });
        command.Parameters.Add(new("session", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)session ?? DBNull.Value });
        command.Parameters.AddWithValue("action", action); command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.Add(new("subject", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)subject ?? DBNull.Value });
        command.Parameters.Add(new("reason", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)reason ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
