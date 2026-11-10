using System.Data.Common;
using System.Text.Json.Serialization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.Family;

// A phone-based pending request is NOT an authorized parent session. Each
// child remains independently scoped; several guardian permissions may coexist.
public sealed record GuardianPermissionDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("player_id")] string PlayerId,
    [property: JsonPropertyName("organization_id")] string OrganizationId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("recipient_hint")] string? RecipientHint);

public sealed record GuardianPermissionsResult(
    [property: JsonPropertyName("items")] IReadOnlyList<GuardianPermissionDto> Items);

public interface IGuardianPermissionRepository
{
    Task<GuardianPermissionDto> IssueAsync(MobileSession session, string childId,
        string phone, Guid requestId, CancellationToken ct);
    Task<GuardianPermissionsResult> ListAsync(MobileSession session, CancellationToken ct);
    Task RevokeAsync(MobileSession session, Guid permissionId, CancellationToken ct);

}

// This service only manages requests; it never turns a pending phone into
// access to private child data or creates another parent's identity.
public sealed class GuardianPermissionService(
    MobileAuthService identity, IGuardianPermissionRepository repository,
    IAuthThrottle throttle, TimeProvider time)
{
    public async Task<GuardianPermissionDto> IssueAsync(MobileSession session,
        string? childId, string? targetPhone, Guid requestId, CancellationToken ct)
    {
        RequireParent(session);
        if (requestId == Guid.Empty || childId is not { Length: >= 1 and <= 128 } ||
            childId != childId.Trim() || childId.Any(char.IsControl) ||
            targetPhone is null || targetPhone.Length > 30 ||
            !PhoneNumber.TryParse(targetPhone, out var phone) ||
            phone.Digits == session.NormalizedPhone)
            throw Invalid();
        await identity.AuthorizeCurrentIdentityAsync(session, ct);
        await Limit(session.IdentityId.ToString("N"), MobileAuthThrottleScope.FamilyInvitation, ct);
        await Limit(phone.Digits, MobileAuthThrottleScope.FamilyInvitationTarget, ct);
        try
        {
            var result = await repository.IssueAsync(session, childId, phone.Digits, requestId, ct);
            if (result.Id != requestId || result.PlayerId != childId ||
                result.Kind != "guardian" || result.Status != "granted")
                throw Unavailable();
            return result;
        }
        catch (DbException) { throw Unavailable(); }
    }

    public async Task<GuardianPermissionsResult> ListAsync(MobileSession session, CancellationToken ct)
    {
        RequireParent(session);
        await identity.AuthorizeCurrentIdentityAsync(session, ct);
        try
        {
            var result = await repository.ListAsync(session, ct);
            if (result.Items.Count > 100 ||
                result.Items.Select(x => x.Id).Distinct().Count() != result.Items.Count ||
                result.Items.Any(x => x.Kind != "guardian" ||
                    x.Status != "granted" ||
                    x.PlayerId.Length is < 1 or > 128 ||
                    x.RecipientHint is null || x.RecipientHint.Length != 8 ||
                    !x.RecipientHint.StartsWith("••••", StringComparison.Ordinal) ||
                    !x.RecipientHint[4..].All(char.IsAsciiDigit)))
                throw Unavailable();
            return result;
        }
        catch (DbException) { throw Unavailable(); }
    }

    public async Task<FamilyOperationResult> RevokeAsync(MobileSession session,
        Guid permissionId, CancellationToken ct)
    {
        RequireParent(session);
        if (permissionId == Guid.Empty) throw Invalid();
        await identity.AuthorizeCurrentIdentityAsync(session, ct);
        await Limit(session.IdentityId.ToString("N"), MobileAuthThrottleScope.FamilyInvitation, ct);
        try
        {
            await repository.RevokeAsync(session, permissionId, ct);
            return new(true);
        }
        catch (DbException) { throw Unavailable(); }
    }

    private async Task Limit(string key, MobileAuthThrottleScope scope, CancellationToken ct)
    {
        try
        {
            var decision = await throttle.TryAcquireAsync(scope, key, time.GetUtcNow(), ct);
            if (!decision.IsAllowed)
                throw new MobileAuthException(MobileAuthFaultKind.TooManyRequests,
                    MobileAuthErrorCode.RateLimited, "Too many family permission requests.",
                    decision.RetryAfterSeconds);
        }
        catch (DbException) { throw Unavailable(); }
    }

    private static void RequireParent(MobileSession session)
    {
        if (session.Role != MobileRole.Parent)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable, "Family permissions require the parent role.");
    }
    private static MobileAuthException Invalid() => new(MobileAuthFaultKind.Validation,
        MobileAuthErrorCode.InvalidInput, "Invalid family permission request.");
    private static MobileAuthException Unavailable() => new(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.FamilyUnavailable, "Family permissions are unavailable.");
}
