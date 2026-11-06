using System.Data.Common;
using System.Text.Json.Serialization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.Family;

// Pending invitation reservation only. Acceptance never creates a player grant
// in this phase; only a future OTP-confirmed provisioner may do that.
public sealed record PendingFamilyInvitationDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed record FamilyOperationResult([property: JsonPropertyName("ok")] bool Ok);

public interface IFamilyInvitationRepository
{
    Task<PendingFamilyInvitationDto> IssueAsync(MobileSession session, string childId,
        string normalizedTargetPhone, Guid invitationId, CancellationToken token);
    Task CancelAsync(MobileSession session, Guid invitationId, CancellationToken token);
}

public sealed class FamilyInvitationService(
    MobileAuthService identity, IFamilyInvitationRepository repository,
    IAuthThrottle throttle, TimeProvider time)
{
    public async Task<PendingFamilyInvitationDto> IssueAsync(MobileSession session, string? childId,
        string? targetPhone, CancellationToken token)
    {
        RequireParent(session);
        var player = ChildId(childId);
        if (targetPhone is null || targetPhone.Length > 30 ||
            !PhoneNumber.TryParse(targetPhone, out var phone))
            throw InvalidInput();
        if (phone.Digits == session.NormalizedPhone) throw InvalidInput();
        await identity.AuthorizeCurrentIdentityAsync(session, token);
        await ThrottleAsync(MobileAuthThrottleScope.FamilyInvitation, session.IdentityId.ToString("N"), token);
        // One target cannot be repeatedly contacted by unrelated parents.
        await ThrottleAsync(MobileAuthThrottleScope.FamilyInvitationTarget, phone.Digits, token);
        try { return await repository.IssueAsync(session, player, phone.Digits, Guid.NewGuid(), token); }
        catch (DbException error) { throw DatabaseFault(error); }
    }

    public async Task<FamilyOperationResult> CancelAsync(MobileSession session, Guid invitationId,
        CancellationToken token)
    {
        RequireParent(session);
        if (invitationId == Guid.Empty) throw InvalidInput();
        await identity.AuthorizeCurrentIdentityAsync(session, token);
        await ThrottleAsync(MobileAuthThrottleScope.FamilyInvitation, session.IdentityId.ToString("N"), token);
        try
        {
            await repository.CancelAsync(session, invitationId, token);
            return new(true);
        }
        catch (DbException error) { throw DatabaseFault(error); }
    }

    private async Task ThrottleAsync(MobileAuthThrottleScope scope, string key, CancellationToken token)
    {
        try
        {
            var decision = await throttle.TryAcquireAsync(scope, key, time.GetUtcNow(), token);
            if (!decision.IsAllowed)
                throw new MobileAuthException(MobileAuthFaultKind.TooManyRequests,
                    MobileAuthErrorCode.RateLimited, "Too many family invitation requests.",
                    decision.RetryAfterSeconds);
        }
        catch (DbException)
        {
            throw Unavailable();
        }
    }

    private static string ChildId(string? value)
    {
        if (value is null || value.Length is < 1 or > 128 ||
            value != value.Trim() || value.Any(char.IsControl))
            throw InvalidInput();
        return value;
    }

    private static void RequireParent(MobileSession session)
    {
        if (session.Role != MobileRole.Parent)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable, "Family invitations require the current parent role.");
    }

    private static MobileAuthException DatabaseFault(DbException error)
    {
        if (error is Npgsql.PostgresException postgres)
        {
            if (postgres.SqlState == "P0001")
                return postgres.MessageText switch
                {
                    "family_invitation_denied" => new(MobileAuthFaultKind.Forbidden,
                        MobileAuthErrorCode.AccountUnavailable, "Family access is unavailable."),
                    "family_invitation_missing" => new(MobileAuthFaultKind.NotFound,
                        MobileAuthErrorCode.InvalidInput, "Invitation not found."),
                    "family_invitation_conflict" => new(MobileAuthFaultKind.Conflict,
                        MobileAuthErrorCode.FamilyInvitationConflict, "The invitation is no longer available."),
                    _ => Unavailable()
                };
            if (postgres.SqlState is "23505" or "23503")
                return new(MobileAuthFaultKind.Conflict,
                    MobileAuthErrorCode.FamilyInvitationConflict, "The invitation is no longer available.");
        }
        return Unavailable();
    }

    private static MobileAuthException InvalidInput() => new(MobileAuthFaultKind.Validation,
        MobileAuthErrorCode.InvalidInput, "Invalid family invitation input.");
    private static MobileAuthException Unavailable() => new(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.FamilyUnavailable, "Family invitations are temporarily unavailable.");
}
