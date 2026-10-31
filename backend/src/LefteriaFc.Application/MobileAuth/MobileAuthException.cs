namespace LefteriaFc.Application.MobileAuth;

// API maps these categories to 400, 401, 403, 404, 422, 429 and 503 respectively.
public enum MobileAuthFaultKind
{
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Validation,
    PayloadTooLarge,
    TooManyRequests,
    ServiceUnavailable
}

public enum MobileAuthErrorCode
{
    InvalidPhone,
    InvalidInput,
    InvalidBirthday,
    InvalidRememberSelection,
    RateLimited,
    DeliveryUnavailable,
    ChallengeUnavailable,
    ChallengeExpired,
    ChallengeAttemptsExceeded,
    InvalidCode,
    AccountNotFound,
    RoleRequired,
    RoleUnavailable,
    SessionExpired,
    SessionInvalid,
    IdentityStateInvalid,
    AccountUnavailable,
    FormalAccountAlreadyExists,
    OrganizationSelectionRequired,
    OrganizationUnknown,
    OrganizationUnavailable,
    NoProfileChanges,
    InvalidTeamPreference,
    InvalidProfileField,
    InvalidNotificationPreferences,
    InvalidAvatar,
    AvatarTooLarge,
    AvatarProcessingBusy,
    AvatarStorageUnavailable,
    RosterUnavailable,
    CalendarUnavailable,
    InvalidCalendarInput,
    CalendarEventAmbiguous,
    LegacyBridgeUnavailable,
    LegacyTokenInvalid
}

public sealed class MobileAuthException(
    MobileAuthFaultKind kind,
    MobileAuthErrorCode code,
    string detail,
    int? retryAfterSeconds = null) : Exception(detail)
{
    public MobileAuthFaultKind Kind { get; } = kind;
    public MobileAuthErrorCode Code { get; } = code;
    public string Detail { get; } = detail;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
