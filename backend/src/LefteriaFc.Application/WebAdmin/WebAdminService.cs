using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.WebAdmin;

public sealed class WebAdminService(IWebAdminSettings settings, IWebAdminRepository repository,
    IWebAdminPasswordVerifier passwords, IWebAdminSecrets secrets)
{
    public async Task<WebAdminLoginResult> LoginAsync(WebAdminLoginCommand command, string client,
        CancellationToken cancellationToken)
    {
        RequireEnabled();
        if (command.Username is not { Length: > 0 and <= 80 } username ||
            command.Password is not { Length: > 0 and <= 4096 } password)
            throw new WebAdminException(400, "invalid_login_input", "Enter a username and password.");
        TimeSpan lifetime;
        try { lifetime = RememberDevicePolicy.Resolve(SessionRealm.Admin, true, command.RememberDays); }
        catch (ArgumentOutOfRangeException) { throw new WebAdminException(422, "invalid_remember_days", "Choose an allowed session duration."); }
        var subject = secrets.Fingerprint("admin-username", username);
        if (!await repository.AcquireLoginRateAsync(secrets.Fingerprint("admin-client", client), subject, cancellationToken))
            throw new WebAdminException(429, "login_rate_limited", "Too many attempts. Try again later.");
        var candidate = await repository.FindCredentialAsync(username, cancellationToken);
        var verified = await passwords.VerifyAsync(password, candidate?.PasswordHash, cancellationToken);
        if (!verified || candidate is null || !candidate.IsActive || candidate.InvitePending || !AdminPermissions.IsKnownRole(candidate.Role))
        {
            await repository.AuditDeniedAsync(subject, "credentials_invalid", cancellationToken);
            throw Invalid();
        }
        if (candidate.Otp2faEnabled)
        {
            await repository.AuditDeniedAsync(subject, "two_factor_unavailable", cancellationToken);
            throw new WebAdminException(503, "two_factor_unavailable", "This account requires two-factor authentication, which is not configured here.");
        }
        var token = secrets.NewToken();
        var digest = secrets.TokenDigest(token);
        if (!await repository.CreateSessionAsync(Guid.NewGuid(), candidate, digest,
                secrets.CredentialDigest(candidate.PasswordHash!), lifetime, subject, cancellationToken))
            throw Invalid();
        var session = await CurrentAsync(token, cancellationToken) ?? throw Invalid();
        return new(session, token);
    }

    public async Task<WebAdminSessionDto?> CurrentAsync(string? token, CancellationToken cancellationToken)
    {
        if (!settings.Enabled || !secrets.IsToken(token)) return null;
        var session = await repository.FindSessionAsync(secrets.TokenDigest(token!), cancellationToken);
        if (session is null || !session.Account.IsActive || session.Account.InvitePending || session.Account.Otp2faEnabled ||
            !AdminPermissions.IsKnownRole(session.Account.Role) || session.Account.PasswordHash is null ||
            !secrets.SameDigest(session.CredentialDigest, secrets.CredentialDigest(session.Account.PasswordHash))) return null;
        var account = session.Account;
        return new(new(account.Id, account.Username, account.Name, account.Role,
            AdminPermissions.ForRole(account.Role), session.Organizations), session.ExpiresAt);
    }

    public async Task LogoutAsync(string? token, CancellationToken cancellationToken)
    {
        RequireEnabled();
        if (secrets.IsToken(token)) await repository.RevokeSessionAsync(secrets.TokenDigest(token!), cancellationToken);
    }

    public void RequireEnabled()
    {
        if (!settings.Enabled) throw new WebAdminException(503, "admin_auth_unavailable", "Admin authentication is not configured here.");
    }
    private static WebAdminException Invalid() => new(401, "credentials_invalid", "Invalid username or password.");
}
