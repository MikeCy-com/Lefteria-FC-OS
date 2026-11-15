using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.PublicContent;

/// <summary>
/// Unpublished content is accessible only through the dedicated admin
/// connection. Every operation locks/rechecks the current account, session,
/// role and exact organization grant, then commits an audit with each write.
/// </summary>
public sealed class PostgresAdminNewsDraftRepository(
    WebAdminConnections connections, IWebAdminSecrets secrets) : IAdminNewsDraftRepository
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public Task<AdminNewsDraftPageDto> ReadAsync(string token, string organization, CancellationToken ct) =>
        InTransactionAsync(token, organization, "news:read", async (connection, transaction, _, __) =>
        {
            await using var read = new NpgsqlCommand("""
                SELECT id,data_json::text,version,updated_at
                FROM public_content.news_drafts
                WHERE organization_id=@org ORDER BY updated_at DESC,id LIMIT 30
                """, connection, transaction);
            read.Parameters.AddWithValue("org", organization);
            var rows = new List<AdminNewsDraftDto>();
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var editable = JsonSerializer.Deserialize<AdminNewsDraftEditable>(reader.GetString(1), Json)
                    ?? throw new InvalidOperationException("Invalid private editorial projection.");
                editable.Validate();
                rows.Add(new AdminNewsDraftDto(
                    reader.GetString(0), organization, "draft", reader.GetInt64(2),
                    editable, reader.GetFieldValue<DateTimeOffset>(3)));
            }
            return new AdminNewsDraftPageDto(organization, rows);
        }, ct);

    public Task<AdminNewsDraftDto> CreateAsync(string token, string organization,
        AdminNewsDraftCreate command, CancellationToken ct)
    {
        if (command.Article is null) throw AdminNewsDraftEditable.Invalid();
        command.Article.Validate();
        var id = "draft_" + Guid.NewGuid().ToString("N");
        return InTransactionAsync(token, organization, "news:write", async (connection, transaction, actor, session) =>
        {
            await using var save = new NpgsqlCommand("""
                INSERT INTO public_content.news_drafts
                    (organization_id,id,data_json,version)
                SELECT @org,@id,@data::jsonb,1
                WHERE EXISTS (SELECT 1 FROM identity.admin_sessions
                    WHERE id=@session AND revoked_at IS NULL AND expires_at>clock_timestamp())
                RETURNING version,updated_at
                """, connection, transaction);
            save.Parameters.AddWithValue("org", organization);
            save.Parameters.AddWithValue("id", id);
            save.Parameters.AddWithValue("data", JsonSerializer.Serialize(command.Article, Json));
            save.Parameters.AddWithValue("session", session);
            long version;
            DateTimeOffset updated;
            await using (var reader = await save.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) throw DeniedSession();
                version = reader.GetInt64(0);
                updated = reader.GetFieldValue<DateTimeOffset>(1);
            }
            await AuditAsync(connection, transaction, actor, session, organization,
                "admin.news_draft.create", ct);
            return new AdminNewsDraftDto(id, organization, "draft", version, command.Article, updated);
        }, ct);
    }

    public Task<AdminNewsDraftDto> UpdateAsync(string token, string organization, string draftId,
        AdminNewsDraftRevision command, CancellationToken ct)
    {
        if (!ValidDraftId(draftId) || command.ExpectedVersion < 1 || command.Article is null)
            throw AdminNewsDraftEditable.Invalid();
        command.Article.Validate();
        return InTransactionAsync(token, organization, "news:write", async (connection, transaction, actor, session) =>
        {
            await using var save = new NpgsqlCommand("""
                UPDATE public_content.news_drafts
                SET data_json=@data::jsonb,version=version+1,updated_at=clock_timestamp()
                WHERE organization_id=@org AND id=@id AND version=@version
                  AND EXISTS (SELECT 1 FROM identity.admin_sessions
                    WHERE id=@session AND revoked_at IS NULL AND expires_at>clock_timestamp())
                RETURNING version,updated_at
                """, connection, transaction);
            save.Parameters.AddWithValue("org", organization);
            save.Parameters.AddWithValue("id", draftId);
            save.Parameters.AddWithValue("data", JsonSerializer.Serialize(command.Article, Json));
            save.Parameters.AddWithValue("version", command.ExpectedVersion);
            save.Parameters.AddWithValue("session", session);
            long version = 0;
            DateTimeOffset updated = default;
            await using (var reader = await save.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    version = reader.GetInt64(0);
                    updated = reader.GetFieldValue<DateTimeOffset>(1);
                }
            }
            if (version == 0)
            {
                await using var exists = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT 1 FROM public_content.news_drafts
                      WHERE organization_id=@org AND id=@id)
                    """, connection, transaction);
                exists.Parameters.AddWithValue("org", organization);
                exists.Parameters.AddWithValue("id", draftId);
                if (await exists.ExecuteScalarAsync(ct) is not true)
                    throw new WebAdminException(404, "news_draft_not_found", "This draft is not available.");
                throw new WebAdminException(409, "news_draft_conflict", "Reload before saving this draft.");
            }
            await AuditAsync(connection, transaction, actor, session, organization,
                "admin.news_draft.update", ct);
            return new AdminNewsDraftDto(draftId, organization, "draft", version, command.Article, updated);
        }, ct);
    }

    private async Task<T> InTransactionAsync<T>(string token, string organization, string capability,
        Func<NpgsqlConnection, NpgsqlTransaction, Guid, Guid, Task<T>> work, CancellationToken ct)
    {
        if (!PublicContentService.IsOrganization(organization))
            throw new WebAdminException(400, "invalid_organization", "Choose a known organization.");
        if (!secrets.IsToken(token)) throw DeniedSession();

        try
        {
            await using var connection = await connections.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var timeout = new NpgsqlCommand("""
                SET LOCAL lock_timeout='3s';
                SET LOCAL statement_timeout='8s';
                SET LOCAL idle_in_transaction_session_timeout='10s';
                """, connection, transaction))
                await timeout.ExecuteNonQueryAsync(ct);

            var digest = secrets.TokenDigest(token);
            await using (var locks = new NpgsqlCommand(
                "SELECT identity.lock_admin_club_authority(@token,@org)",
                connection, transaction))
            {
                locks.Parameters.AddWithValue("token", digest);
                locks.Parameters.AddWithValue("org", organization);
                await locks.ExecuteNonQueryAsync(ct);
            }

            Guid actor, session;
            await using (var authority = new NpgsqlCommand("""
                SELECT a.id,s.id,a.password_hash,a.role,s.credential_digest,
                  EXISTS (SELECT 1 FROM identity.admin_organization_grants g
                    WHERE g.admin_account_id=a.id AND g.organization_id=@org)
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                WHERE s.token_digest=@token AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL
                """, connection, transaction))
            {
                authority.Parameters.AddWithValue("token", digest);
                authority.Parameters.AddWithValue("org", organization);
                await using var reader = await authority.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw DeniedSession();
                actor = reader.GetGuid(0);
                session = reader.GetGuid(1);
                var password = reader.GetString(2);
                var role = reader.GetString(3);
                var credential = reader.GetString(4);
                if (!secrets.SameDigest(credential, secrets.CredentialDigest(password)) ||
                    !AdminPermissions.IsKnownRole(role)) throw DeniedSession();
                if (!AdminPermissions.Has(role, capability))
                    throw new WebAdminException(403, "news_permission_denied", "CMS access denied.");
                if (!reader.GetBoolean(5))
                    throw new WebAdminException(403, "organization_denied", "Organization access denied.");
            }

            var result = await work(connection, transaction, actor, session);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (PostgresException error) when (error.SqlState == "P0001" &&
            error.MessageText == "admin_club_session_lock_missing") { throw DeniedSession(); }
        catch (PostgresException error) when (error.SqlState == "P0001" &&
            error.MessageText == "admin_club_grant_lock_missing")
        { throw new WebAdminException(403, "organization_denied", "Organization access denied."); }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.DeadlockDetected or
            PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.SerializationFailure or
            PostgresErrorCodes.QueryCanceled)
        { throw new WebAdminException(503, "news_drafts_busy", "Draft workspace is busy."); }
    }

    private static async Task AuditAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actor, Guid session, string org, string action, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                 action,outcome,organization_id,reason_code)
            VALUES(@id,'admin',clock_timestamp(),@actor,@session,
                   @action,'succeeded',@org,'unpublished_news')
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("session", session);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("org", org);
        await command.ExecuteNonQueryAsync(token);
    }

    private static bool ValidDraftId(string id) =>
        id.Length == 38 && id.StartsWith("draft_", StringComparison.Ordinal) &&
        id.AsSpan(6).IndexOfAnyExcept("0123456789abcdef") == -1;

    private static WebAdminException DeniedSession() =>
        new(401, "admin_session_invalid", "Sign in again to continue.");
}
