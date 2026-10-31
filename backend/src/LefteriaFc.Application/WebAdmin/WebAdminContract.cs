using System.Text.Json.Serialization;

namespace LefteriaFc.Application.WebAdmin;

public sealed record WebAdminOrganizationDto(string Id, string Name);
public sealed record WebAdminIdentityDto(Guid Id, string Username, string Name, string Role,
    IReadOnlyList<string> Permissions, IReadOnlyList<WebAdminOrganizationDto> Organizations);
public sealed record WebAdminSessionDto(WebAdminIdentityDto User, DateTimeOffset ExpiresAt);
public sealed record WebAdminLoginCommand(string? Username, string? Password,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? RememberDays)
{ public override string ToString() => "Web administrator login command (password redacted)"; }
public sealed record WebAdminLoginResult(WebAdminSessionDto Session, string Token)
{ public override string ToString() => "Web administrator login result (token redacted)"; }
public sealed record WebAdminCredential(Guid Id, string Username, string Name, string Role,
    string? PasswordHash, bool IsActive, bool InvitePending, bool Otp2faEnabled)
{ public override string ToString() => "Web administrator credential (redacted)"; }
public sealed record StoredWebAdminSession(Guid Id, WebAdminCredential Account, string CredentialDigest,
    DateTimeOffset ExpiresAt, IReadOnlyList<WebAdminOrganizationDto> Organizations)
{ public override string ToString() => "Stored web administrator session (redacted)"; }

public sealed class WebAdminException(int status, string code, string detail) : Exception(detail)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public interface IWebAdminSettings { bool Enabled { get; } }
public interface IWebAdminSecrets
{
    string NewToken();
    bool IsToken(string? token);
    string TokenDigest(string token);
    string CredentialDigest(string passwordHash);
    bool SameDigest(string left, string right);
    string Fingerprint(string purpose, string value);
}
public interface IWebAdminPasswordVerifier
{
    Task<bool> VerifyAsync(string password, string? encodedHash, CancellationToken cancellationToken);
}
public interface IWebAdminRepository
{
    Task<bool> AcquireLoginRateAsync(string clientFingerprint, string usernameFingerprint, CancellationToken cancellationToken);
    Task<WebAdminCredential?> FindCredentialAsync(string username, CancellationToken cancellationToken);
    Task<bool> CreateSessionAsync(Guid id, WebAdminCredential candidate, string tokenDigest, string credentialDigest,
        TimeSpan lifetime, string subjectDigest, CancellationToken cancellationToken);
    Task<StoredWebAdminSession?> FindSessionAsync(string tokenDigest, CancellationToken cancellationToken);
    Task RevokeSessionAsync(string tokenDigest, CancellationToken cancellationToken);
    Task AuditDeniedAsync(string subjectDigest, string reason, CancellationToken cancellationToken);
}
