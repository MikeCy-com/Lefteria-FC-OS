using System.Data.Common;
using System.Text.Json.Serialization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.Family;

// A pending invitation never grants player access. Internal grant operations
// are not routed to HTTP until complete player authorization is implemented.
public sealed record PendingFamilyInvitationDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed record FamilyOperationResult([property: JsonPropertyName("ok")] bool Ok);

// This safe recipient-facing projection discloses neither child nor guardian
// identity. It never claims that the recipient has a player login role.
public sealed record FamilyDelegationAvailabilityDto(
    [property: JsonPropertyName("has_active_delegation")] bool HasActiveDelegation);

// Outgoing reservation metadata for a currently verified guardian only.
// Never return the recipient's phone number, invite target or source record JSON.
public sealed record ParentOutgoingInvitationDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("player_id")] string PlayerId,
    [property: JsonPropertyName("organization_id")] string OrganizationId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed record ParentOutgoingInvitationsResult(
    [property: JsonPropertyName("items")] IReadOnlyList<ParentOutgoingInvitationDto> Items);

// No child, guardian, phone, or player-role details are returned to the target.
// A fan can see a reservation after verifying ownership of their own number,
// but cannot accept it or gain a new identity in this read-only slice.
public sealed record RecipientPendingInvitationDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("organization_id")] string OrganizationId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed record RecipientPendingInvitationsResult(
    [property: JsonPropertyName("items")] IReadOnlyList<RecipientPendingInvitationDto> Items);

// Internal authorization projection only. Never serialize this raw child ID,
// grant identifier, or organization to an untrusted mobile response.
public sealed record FamilyDelegationSnapshot(Guid GrantId, string PlayerId, string OrganizationId)
{
    public override string ToString() => "Current revalidated family delegation";
}

public interface IFamilyInvitationRepository
{
    Task<PendingFamilyInvitationDto> IssueAsync(MobileSession session, string childId,
        string normalizedTargetPhone, Guid invitationId, CancellationToken token);
    Task CancelAsync(MobileSession session, Guid invitationId, CancellationToken token);
    Task<IReadOnlyList<ParentOutgoingInvitationDto>> ReadOutgoingAsync(
        MobileSession session, CancellationToken token);
    Task<IReadOnlyList<RecipientPendingInvitationDto>> ReadRecipientAsync(
        MobileSession session, CancellationToken token);
    Task DeclineAsync(MobileSession session, Guid invitationId, CancellationToken token);
    Task AcceptGrantAsync(MobileSession session, Guid invitationId, Guid grantId, CancellationToken token);
    Task RevokeGrantAsync(MobileSession session, string playerId, CancellationToken token);
    Task<FamilyDelegationSnapshot?> ReadCurrentDelegationAsync(MobileSession session, CancellationToken token);
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
        catch (DbException) { throw Unavailable(); }
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
        catch (DbException) { throw Unavailable(); }
    }

    public async Task<ParentOutgoingInvitationsResult> ReadOutgoingAsync(
        MobileSession session, CancellationToken token)
    {
        RequireParent(session);
        await identity.AuthorizeCurrentIdentityAsync(session, token);
        try
        {
            var items = await repository.ReadOutgoingAsync(session, token);
            // Returning a silently truncated private list misleads the parent
            // into believing they have seen all pending reservations.
            if (items.Count > 100)
                throw Unavailable();
            return new(items);
        }
        catch (DbException) { throw Unavailable(); }
    }

    public async Task<RecipientPendingInvitationsResult> ReadRecipientAsync(
        MobileSession session, CancellationToken token)
    {
        if (session.Role != MobileRole.Fan)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,
                "Recipient invitations require a current verified fan session.");
        await identity.AuthorizeCurrentIdentityAsync(session, token);
        try
        {
            var items = await repository.ReadRecipientAsync(session, token);
            // A single target phone can have only one pending slot. If a
            // database error causes multiple results, fail closed.
            if (items.Count > 1)
                throw Unavailable();
            return new(items);
        }
        catch (DbException) { throw Unavailable(); }
    }

    public async Task<FamilyOperationResult> DeclineAsync(
        MobileSession session, Guid invitationId, CancellationToken token)
    {
        if (session.Role != MobileRole.Fan)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,
                "Declining an invitation requires the verified target's fan session.");
        if (invitationId == Guid.Empty) throw InvalidInput();
        await identity.AuthorizeCurrentIdentityAsync(session, token);
        await ThrottleAsync(MobileAuthThrottleScope.FamilyInvitation,
            session.IdentityId.ToString("N"), token);
        try
        {
            await repository.DeclineAsync(session, invitationId, token);
            return new(true);
        }
        catch (DbException) { throw Unavailable(); }
    }

    // Internal only: no HTTP acceptance route or player-role elevation.
    // The database rechecks the live OTP fan, canonical guardian, roster,
    // collision boundaries, and pending invitation under transaction locks.
    public async Task<FamilyOperationResult> AcceptGrantAsync(
        MobileSession session, Guid invitationId, CancellationToken token)
    {
        RequireFan(session);
        if (invitationId == Guid.Empty) throw InvalidInput();
        await identity.AuthorizeCurrentIdentityAsync(session, token);
        await ThrottleAsync(MobileAuthThrottleScope.FamilyInvitation,
            session.IdentityId.ToString("N"), token);
        try
        {
            await repository.AcceptGrantAsync(session, invitationId, Guid.NewGuid(), token);
            return new(true);
        }
        catch (DbException) { throw Unavailable(); }
    }

    // Internal only: the guardian's original player association is verified
    // again by the scoped PostgreSQL function before any grant is revoked.
    public async Task<FamilyOperationResult> RevokeGrantAsync(
        MobileSession session, string? playerId, CancellationToken token)
    {
        RequireParent(session);
        var child = ChildId(playerId);
        await identity.AuthorizeCurrentIdentityAsync(session, token);
        await ThrottleAsync(MobileAuthThrottleScope.FamilyInvitation,
            session.IdentityId.ToString("N"), token);
        try
        {
            await repository.RevokeGrantAsync(session, child, token);
            return new(true);
        }
        catch (DbException) { throw Unavailable(); }
    }

    public async Task<FamilyDelegationAvailabilityDto> GetDelegationAvailabilityAsync(
        MobileSession session, CancellationToken token)
    {
        // A false result is intentional: historical grants, inactive guardians,
        // conflicting canonical numbers and revoked slots all disappear.
        var current = await ReadCurrentDelegationAsync(session, token);
        return new(current is not null);
    }

    // A current stored grant is not sufficient authority: the repository
    // invokes the guardian/roster/phone revalidation function on every read.
    // Consumers must still enforce player data authorization independently.
    public async Task<FamilyDelegationSnapshot?> ReadCurrentDelegationAsync(
        MobileSession session, CancellationToken token)
    {
        RequireFan(session);
        await identity.AuthorizeCurrentIdentityAsync(session, token);
        try { return await repository.ReadCurrentDelegationAsync(session, token); }
        catch (DbException) { throw Unavailable(); }
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

    private static void RequireFan(MobileSession session)
    {
        if (session.Role != MobileRole.Fan)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,
                "Delegated family access requires the recipient's verified fan role.");
    }

    private static void RequireParent(MobileSession session)
    {
        if (session.Role != MobileRole.Parent)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable, "Family invitations require the current parent role.");
    }

    private static MobileAuthException InvalidInput() => new(MobileAuthFaultKind.Validation,
        MobileAuthErrorCode.InvalidInput, "Invalid family invitation input.");
    private static MobileAuthException Unavailable() => new(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.FamilyUnavailable, "Family invitations are temporarily unavailable.");
}
