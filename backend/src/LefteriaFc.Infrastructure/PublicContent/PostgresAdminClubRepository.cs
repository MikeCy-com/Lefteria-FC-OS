using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.PublicContent;

public sealed class PostgresAdminClubRepository(WebAdminConnections connections, IWebAdminSecrets secrets) : IAdminClubRepository
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public Task<AdminClubProfileDto> ReadAsync(string token, string organization, CancellationToken cancellationToken) =>
        ExecuteAsync(token, organization, null, cancellationToken);
    public Task<AdminClubProfileDto> UpdateAsync(string token, string organization, AdminClubUpdateCommand command, CancellationToken cancellationToken)
    {
        if (command.ExpectedVersion < 0 || command.Club is null) throw AdminClubEditable.Invalid();
        command.Club.Validate();
        return ExecuteAsync(token, organization, command, cancellationToken);
    }

    private async Task<AdminClubProfileDto> ExecuteAsync(string token, string organization, AdminClubUpdateCommand? update, CancellationToken ct)
    {
        if (!PublicContentService.IsOrganization(organization)) throw new WebAdminException(400, "invalid_organization", "Choose a known organization.");
        if (!secrets.IsToken(token)) throw DeniedSession();
        try
        {
            await using var connection = await connections.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            NpgsqlCommand Command(string sql)
            {
                var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("token", secrets.TokenDigest(token));
                command.Parameters.AddWithValue("org", organization);
                return command;
            }
            await using (var timeout = Command("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='8s'; SET LOCAL idle_in_transaction_session_timeout='10s'")) await timeout.ExecuteNonQueryAsync(ct);
            await using (var locks = Command("SELECT identity.lock_admin_club_authority(@token,@org)")) await locks.ExecuteNonQueryAsync(ct);
            // The definer only takes row locks; all sensitive reads still use
            // this restricted admin connection. Locks survive until commit.
            Guid actor, session; string hash, role, credential;
            await using (var authority = Command("""
                SELECT a.id,s.id,a.password_hash,a.role,s.credential_digest,
                  EXISTS(SELECT 1 FROM identity.admin_organization_grants g WHERE g.admin_account_id=a.id AND g.organization_id=@org)
                FROM identity.admin_sessions s JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                WHERE s.token_digest=@token AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()
                  AND a.is_active AND NOT a.invite_pending AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL
                """))
            {
                await using var reader = await authority.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw DeniedSession();
                actor = reader.GetGuid(0); session = reader.GetGuid(1); hash = reader.GetString(2); role = reader.GetString(3); credential = reader.GetString(4);
                if (!secrets.SameDigest(credential, secrets.CredentialDigest(hash)) || !AdminPermissions.IsKnownRole(role)) throw DeniedSession();
                if (!AdminPermissions.Has(role, update is null ? "settings:read" : "settings:write"))
                    throw new WebAdminException(403, "admin_permission_denied", "This role cannot edit club settings.");
                if (!reader.GetBoolean(5)) throw new WebAdminException(403, "organization_denied", "This organization is not assigned to the account.");
            }
            await using (var ready = Command("SELECT EXISTS(SELECT 1 FROM public_content.import_state s WHERE s.id='initial' AND s.documents_count=(SELECT count(*) FROM public_content.documents))"))
                if (await ready.ExecuteScalarAsync(ct) is not true) throw new WebAdminException(503, "public_content_unavailable", "Public content is not initialized.");

            PublicClub club; long version;
            await using (var content = Command("""
                SELECT COALESCE(e.data_json,d.data_json)::text,COALESCE(e.version,0)
                FROM public_content.documents d LEFT JOIN public_content.edits e USING(kind,organization_id,id)
                WHERE d.kind='club' AND d.organization_id=@org
                """))
            {
                await using var reader = await content.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new WebAdminException(404, "club_profile_not_found", "The imported club profile was not found.");
                club = JsonSerializer.Deserialize<PublicClub>(reader.GetString(0), Json) ?? throw new InvalidOperationException("Invalid club projection.");
                version = reader.GetInt64(1);
                if (await reader.ReadAsync(ct)) throw new InvalidOperationException("Ambiguous club projection.");
            }
            if (update is not null)
            {
                if (update.ExpectedVersion != version || version == long.MaxValue) throw Conflict();
                club = update.Club.ToPublic(club.Id, organization);
                await using var save = Command("""
                    INSERT INTO public_content.edits(kind,organization_id,id,data_json,version)
                    SELECT 'club',@org,@id,@data::jsonb,1 WHERE (@version=0 OR EXISTS(
                      SELECT 1 FROM public_content.edits WHERE kind='club' AND organization_id=@org AND id=@id AND version=@version))
                      AND EXISTS(SELECT 1 FROM identity.admin_sessions WHERE id=@session AND revoked_at IS NULL AND expires_at>clock_timestamp())
                    ON CONFLICT(kind,organization_id,id) DO UPDATE SET data_json=@data::jsonb,version=edits.version+1
                    WHERE edits.version=@version AND EXISTS(SELECT 1 FROM identity.admin_sessions WHERE id=@session AND revoked_at IS NULL AND expires_at>clock_timestamp())
                    RETURNING version
                    """);
                save.Parameters.AddWithValue("id", club.Id); save.Parameters.AddWithValue("data", JsonSerializer.Serialize(club, Json));
                save.Parameters.AddWithValue("version", update.ExpectedVersion); save.Parameters.AddWithValue("session", session);
                var saved = await save.ExecuteScalarAsync(ct);
                if (saved is not long next)
                {
                    await using var stillValid = Command("SELECT EXISTS(SELECT 1 FROM identity.admin_sessions WHERE token_digest=@token AND revoked_at IS NULL AND expires_at>clock_timestamp())");
                    if (await stillValid.ExecuteScalarAsync(ct) is not true) throw DeniedSession();
                    throw Conflict();
                }
                version = next;
                await using var audit = Command("""
                    INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
                    VALUES(@audit,'admin',clock_timestamp(),@actor,@session,'admin.club.update','succeeded',@org,'public_profile')
                    """);
                audit.Parameters.AddWithValue("audit", Guid.NewGuid()); audit.Parameters.AddWithValue("actor", actor); audit.Parameters.AddWithValue("session", session);
                await audit.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return new(version, club);
        }
        catch (PostgresException error) when (error.SqlState == "P0001" && error.MessageText == "admin_club_session_lock_missing")
        { throw DeniedSession(); }
        catch (PostgresException error) when (error.SqlState == "P0001" && error.MessageText == "admin_club_grant_lock_missing")
        { throw new WebAdminException(403, "organization_denied", "This organization is not assigned to the account."); }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.QueryCanceled)
        { throw new WebAdminException(503, "club_update_busy", "Club settings are busy. Reload and try again."); }
    }
    private static WebAdminException DeniedSession() => new(401, "admin_session_invalid", "Sign in again to continue.");
    private static WebAdminException Conflict() => new(409, "club_version_conflict", "The club profile changed. Reload before saving.");
}
