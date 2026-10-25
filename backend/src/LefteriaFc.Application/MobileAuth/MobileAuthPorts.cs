using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Application.MobileAuth;

public enum MobileAuthThrottleScope { OtpRequest, OtpVerify }

public sealed record AuthThrottleDecision(bool IsAllowed, int? RetryAfterSeconds = null);

public interface IAuthThrottle
{
    Task<AuthThrottleDecision> TryAcquireAsync(MobileAuthThrottleScope scope, string clientKey,
        DateTimeOffset now, CancellationToken cancellationToken);
}

public interface ISmsSender
{
    // The code exists only transiently for delivery. Adapters must not log it.
    Task<bool> SendOtpAsync(PhoneNumber phone, string code, MobileRole? detectedRole,
        CancellationToken cancellationToken);
}

public interface IOtpProtector
{
    string CreateSixDigitCode();
    byte[] ComputeDigest(Guid challengeId, PhoneNumber phone, string code);
}

public interface IMobileTokenIssuer
{
    // Sign the supplied session without changing its device ID or exact expiration.
    string Issue(MobileSession session);
}

public sealed record ManagementOrganizationGrant(Guid AdminAccountId, bool IsActive,
    IReadOnlyList<string> OrganizationIds, string? ActiveOrganizationId);

// A trusted repository snapshot, never an HTTP request. Management requires an
// explicit admin FK; legacy alias IDs or the first matching phone are not grants.
public sealed record MobileIdentityProfile(
    Guid Id,
    PhoneNumber Phone,
    MobileRole Role,
    string Name,
    bool IsActive,
    string? LinkedPlayerId,
    IReadOnlyList<string> LinkedPlayerIds,
    string? TeamId,
    IReadOnlyList<string> AcademyGroupIds,
    string? AvatarUrl,
    string Platform,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastLogin,
    ManagementOrganizationGrant? Management = null,
    string? FavoriteTeam = null,
    string? FirstName = null,
    string? LastName = null,
    DateOnly? Birthday = null,
    string? FavouritePlayerId = null,
    IReadOnlyDictionary<string, bool>? NotificationPreferences = null,
    string? Email = null,
    DateTimeOffset? UpdatedAt = null);

public sealed record PendingFanRegistration(string FirstName, string LastName, DateOnly Birthday)
{
    public string FullName => FirstName + " " + LastName;
}

public sealed record OtpChallengeSnapshot(Guid Id, PhoneNumber Phone, byte[] CodeDigest,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, int VerificationCalls,
    bool IsConsumed, PendingFanRegistration? PendingFan = null);

public sealed record AuthAuditEntry(string Action, string Outcome, DateTimeOffset OccurredAt,
    Guid? IdentityId = null, Guid? DeviceSessionId = null, Guid? ChallengeId = null,
    string? ReasonCode = null);

public sealed record OrganizationSelectionSnapshot(MobileIdentityProfile? Identity,
    OrganizationScope Scope);

public interface IMobileAuthRepository
{
    Task<IMobileAuthTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MobileIdentityProfile>> GetIdentityProfilesAsync(PhoneNumber phone,
        CancellationToken cancellationToken);
    Task<MobileIdentityProfile?> GetIdentityProfileAsync(Guid identityId,
        CancellationToken cancellationToken);

    // Replace by normalized full phone and append audit atomically. Never store a raw code.
    Task IssueOrReplaceChallengeAsync(OtpChallengeSnapshot challenge, AuthAuditEntry audit,
        CancellationToken cancellationToken);

    // A failed older delivery must never delete a newer replacement challenge.
    Task DeleteChallengeIfMatchesAsync(PhoneNumber phone, Guid challengeId, AuthAuditEntry audit,
        CancellationToken cancellationToken);

    // In one DB transaction, re-read identity/admin status and organization grants,
    // validate with OrganizationScope.Select, persist only a Selected result, and
    // append mobile.organization.select audit on success in the same transaction.
    // Lock the relevant identity/admin/grant state so revocation cannot race the write.
    Task<OrganizationSelectionSnapshot> SelectOrganizationAsync(Guid identityId,
        string normalizedPhone, string? requestedOrganizationId, DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IMobileAuthTransaction : IAsyncDisposable
{
    // Lock this phone's current challenge (FOR UPDATE) until commit/disposal.
    Task<OtpChallengeSnapshot?> LockChallengeAsync(PhoneNumber phone,
        CancellationToken cancellationToken);

    // Return current status and explicit relational role grants. Lock relevant
    // records for workflows that may issue a session. Include inactive identities.
    Task<IReadOnlyList<MobileIdentityProfile>> LoadIdentityProfilesForUpdateAsync(PhoneNumber phone,
        CancellationToken cancellationToken);

    // Called only after a successful OTP comparison. Neither operation may reactivate
    // a disabled existing fan. Ensure preserves an existing fan's name/preferences.
    // Writes must be visible to subsequent transaction reads (flush without commit,
    // or merge tracked additions into snapshots).
    Task<MobileIdentityProfile> UpsertVerifiedFanAsync(PhoneNumber phone,
        PendingFanRegistration registration, string platform, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<MobileIdentityProfile> EnsureNeutralFanIdentityAsync(PhoneNumber phone,
        string neutralDisplayName, string platform, DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<MobileIdentityProfile> UpdateLastLoginAsync(Guid identityId, string platform,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<MobileIdentityProfile> UpdateFavoriteTeamAsync(Guid identityId, string? favoriteTeam,
        CancellationToken cancellationToken);
    Task<MobileIdentityProfile> UpdateProfileAsync(Guid identityId, MobileProfileUpdate update,
        CancellationToken cancellationToken);
    Task SaveChallengeProgressAsync(Guid challengeId, int verificationCalls, bool isConsumed,
        CancellationToken cancellationToken);
    Task AppendAuditAsync(AuthAuditEntry audit, CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}
