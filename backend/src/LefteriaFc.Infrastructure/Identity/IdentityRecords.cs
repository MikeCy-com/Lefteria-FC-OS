namespace LefteriaFc.Infrastructure.Identity;

public sealed class MobileAvatarRecord
{
    public Guid IdentityId { get; set; }
    public string FileName { get; set; } = "";
    public string? PreviousAvatarUrl { get; set; }
    public byte[] JpegBytes { get; set; } = [];
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MobileIdentityRecord
{
    public Guid Id { get; set; }
    public string NormalizedPhone { get; set; } = "";
    public string Role { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string? LegacyId { get; set; }
    public Guid? ManagementAdminId { get; set; }
    public AdminAccountRecord? ManagementAdmin { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateOnly? Birthday { get; set; }
    public string? AvatarUrl { get; set; }
    public string? FavoriteTeam { get; set; }
    public string? Email { get; set; }
    public string? FavouritePlayerId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? NotificationPreferencesJson { get; set; }
    public string Platform { get; set; } = "Άλλο";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLogin { get; set; }
}

public sealed class AdminAccountRecord
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string? PasswordHash { get; set; }
    public string? LegacyId { get; set; }
    public string? ActiveOrganizationId { get; set; }
    public List<AdminOrganizationGrantRecord> OrganizationGrants { get; set; } = [];
}

public sealed class OrganizationRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class AdminOrganizationGrantRecord
{
    public Guid AdminAccountId { get; set; }
    public AdminAccountRecord AdminAccount { get; set; } = null!;
    public string OrganizationId { get; set; } = "";
    public OrganizationRecord Organization { get; set; } = null!;
}

public sealed class OtpChallengeRecord
{
    public Guid Id { get; set; }
    public string NormalizedPhone { get; set; } = "";
    public byte[] CodeDigest { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int VerificationCalls { get; set; }
    public bool IsConsumed { get; set; }
    public string? PendingFirstName { get; set; }
    public string? PendingLastName { get; set; }
    public DateOnly? PendingBirthday { get; set; }
}

public sealed class AuthRateBucketRecord
{
    public string Key { get; set; } = "";
    public double Tokens { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SecurityAuditRecord
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? ActorIdentityId { get; set; }
    public Guid? DeviceSessionId { get; set; }
    public string Action { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string? OrganizationId { get; set; }
    public string? SubjectDigest { get; set; }
    public Guid? ChallengeId { get; set; }
    public string? ReasonCode { get; set; }
}
