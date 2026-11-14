using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.PublicContent;

/// <summary>
/// Writes only the allowlisted public article projection. Immutable source
/// evidence and import manifests remain untouched. Uses the same dedicated
/// restricted admin-auth connection and session/grant row locks as ClubEditor.
/// </summary>
public sealed class PostgresAdminNewsRepository(WebAdminConnections connections, IWebAdminSecrets secrets) : IAdminNewsRepository
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public Task<AdminNewsArticleDto> ReadAsync(string token, string organization, string articleId, CancellationToken ct) =>
        ExecuteAsync(token, organization, articleId, null, ct);

    public Task<AdminNewsArticleDto> UpdateAsync(string token, string organization, string articleId,
        AdminNewsUpdateCommand command, CancellationToken ct)
    {
        if (command.ExpectedVersion < 0 || command.Article is null) throw AdminNewsEditable.Invalid();
        command.Article.Validate();
        return ExecuteAsync(token, organization, articleId, command, ct);
    }

    private async Task<AdminNewsArticleDto> ExecuteAsync(
        string token, string organization, string articleId, AdminNewsUpdateCommand? update, CancellationToken ct)
    {
        if (!PublicContentService.IsOrganization(organization))
            throw new WebAdminException(400, "invalid_organization", "Choose a known organization.");
        if (string.IsNullOrWhiteSpace(articleId) || articleId.Length > 128 ||
            articleId.Any(char.IsControl))
            throw new WebAdminException(400, "invalid_article", "Choose an existing published article.");
        if (!secrets.IsToken(token)) throw DeniedSession();

        try
        {
            await using var connection = await connections.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);

            NpgsqlCommand Command(string sql)
            {
                var result = new NpgsqlCommand(sql, connection, transaction);
                result.Parameters.AddWithValue("token", secrets.TokenDigest(token));
                result.Parameters.AddWithValue("org", organization);
                return result;
            }

            await using (var timeout = Command("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='8s'; SET LOCAL idle_in_transaction_session_timeout='10s'"))
                await timeout.ExecuteNonQueryAsync(ct);
            await using (var locks = Command("SELECT identity.lock_admin_club_authority(@token,@org)"))
                await locks.ExecuteNonQueryAsync(ct);

            Guid actor, session;
            await using (var authority = Command("""
                SELECT a.id,s.id,a.password_hash,a.role,s.credential_digest,
                  EXISTS(SELECT 1 FROM identity.admin_organization_grants g
                    WHERE g.admin_account_id=a.id AND g.organization_id=@org)
                FROM identity.admin_sessions s JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                WHERE s.token_digest=@token AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()
                  AND a.is_active AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL
                """))
            {
                await using var reader = await authority.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw DeniedSession();
                actor = reader.GetGuid(0);
                session = reader.GetGuid(1);
                var password = reader.GetString(2);
                var role = reader.GetString(3);
                var credential = reader.GetString(4);
                if (!secrets.SameDigest(credential, secrets.CredentialDigest(password)) ||
                    !AdminPermissions.IsKnownRole(role)) throw DeniedSession();
                if (!AdminPermissions.Has(role, update is null ? "news:read" : "news:write"))
                    throw new WebAdminException(403, "news_permission_denied", "This role cannot edit website news.");
                if (!reader.GetBoolean(5))
                    throw new WebAdminException(403, "organization_denied", "This organization is not assigned to the account.");
            }

            await using (var ready = Command("""
                SELECT EXISTS(SELECT 1 FROM public_content.import_state s
                  WHERE s.id='initial'
                    AND s.documents_count=(SELECT count(*) FROM public_content.documents))
                """))
                if (await ready.ExecuteScalarAsync(ct) is not true)
                    throw new WebAdminException(503, "public_content_unavailable", "Public content is not initialized.");

            PublicArticle article;
            long version;
            await using (var fetch = Command("""
                SELECT COALESCE(e.data_json,d.data_json)::text,COALESCE(e.version,0)
                FROM public_content.documents d
                LEFT JOIN public_content.edits e USING(kind,organization_id,id)
                WHERE d.kind='news' AND d.organization_id=@org AND d.id=@id
                """))
            {
                fetch.Parameters.AddWithValue("id", articleId);
                await using var reader = await fetch.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new WebAdminException(404, "news_article_not_found", "The published article does not exist.");
                article = JsonSerializer.Deserialize<PublicArticle>(reader.GetString(0), Json) ??
                    throw new InvalidOperationException("Invalid safe public article.");
                version = reader.GetInt64(1);
                if (await reader.ReadAsync(ct)) throw new InvalidOperationException("Ambiguous public article.");
                if (article.Id != articleId || article.OrganizationId != organization)
                    throw new InvalidOperationException("Incorrect public article owner.");
            }

            if (update is not null)
            {
                if (update.ExpectedVersion != version || version == long.MaxValue)
                    throw Conflict();
                article = update.Article.WithEditableFields(article);
                await using var save = Command("""
                    INSERT INTO public_content.edits(kind,organization_id,id,data_json,version)
                    SELECT 'news',@org,@id,@data::jsonb,1
                    WHERE @version=0
                      AND EXISTS(SELECT 1 FROM identity.admin_sessions
                        WHERE id=@session AND revoked_at IS NULL AND expires_at>clock_timestamp())
                    ON CONFLICT(kind,organization_id,id)
                    DO UPDATE SET data_json=@data::jsonb,version=edits.version+1
                    WHERE edits.version=@version
                      AND EXISTS(SELECT 1 FROM identity.admin_sessions
                        WHERE id=@session AND revoked_at IS NULL AND expires_at>clock_timestamp())
                    RETURNING version
                    """);
                save.Parameters.AddWithValue("id", articleId);
                save.Parameters.AddWithValue("data", JsonSerializer.Serialize(article, Json));
                save.Parameters.AddWithValue("version", version);
                save.Parameters.AddWithValue("session", session);
                if (await save.ExecuteScalarAsync(ct) is not long nextVersion)
                {
                    await using var check = Command("""
                        SELECT EXISTS(SELECT 1 FROM identity.admin_sessions
                          WHERE token_digest=@token AND revoked_at IS NULL AND expires_at>clock_timestamp())
                        """);
                    if (await check.ExecuteScalarAsync(ct) is not true) throw DeniedSession();
                    throw Conflict();
                }
                version = nextVersion;
                await using var audit = Command("""
                    INSERT INTO audit.security_events
                      (id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
                    VALUES(@audit,'admin',clock_timestamp(),@actor,@session,'admin.news.update','succeeded',@org,'published_article')
                    """);
                audit.Parameters.AddWithValue("audit", Guid.NewGuid());
                audit.Parameters.AddWithValue("actor", actor);
                audit.Parameters.AddWithValue("session", session);
                await audit.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return new(version, article);
        }
        catch (PostgresException e) when (e.SqlState == "P0001" && e.MessageText == "admin_club_session_lock_missing")
        { throw DeniedSession(); }
        catch (PostgresException e) when (e.SqlState == "P0001" && e.MessageText == "admin_club_grant_lock_missing")
        { throw new WebAdminException(403, "organization_denied", "This organization is not assigned to the account."); }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.DeadlockDetected or
            PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.SerializationFailure or
            PostgresErrorCodes.QueryCanceled)
        { throw new WebAdminException(503, "news_update_busy", "News editor is busy. Reload and try again."); }
    }

    private static WebAdminException DeniedSession() =>
        new(401, "admin_session_invalid", "Sign in again to continue.");
    private static WebAdminException Conflict() =>
        new(409, "news_version_conflict", "The article changed. Reload before saving.");
}
