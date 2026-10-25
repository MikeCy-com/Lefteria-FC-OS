namespace LefteriaFc.Domain.Identity;

// Grants must be freshly loaded from persistence before evaluating a session.
// A phone number identifies a device login; each role remains a separate identity.
public sealed record MobileIdentityGrant(
    Guid IdentityId,
    string NormalizedPhone,
    MobileRole Role,
    bool IsActive);

public sealed record MobileRoleSwitch(bool IsNoop, MobileSession Session);

public sealed record MobileSession(
    Guid DeviceSessionId,
    Guid IdentityId,
    string NormalizedPhone,
    MobileRole Role,
    DateTimeOffset ExpiresAt)
{
    public MobileRoleSwitch RecoverFanAccess(MobileIdentityGrant fanIdentity, DateTimeOffset now)
    {
        if (ExpiresAt <= now)
        {
            throw new MobileSessionAccessException(MobileSessionDenial.Expired);
        }

        if (DeviceSessionId == Guid.Empty || IdentityId == Guid.Empty ||
            string.IsNullOrWhiteSpace(NormalizedPhone) || !Enum.IsDefined(Role) ||
            !fanIdentity.IsActive || fanIdentity.IdentityId == Guid.Empty ||
            fanIdentity.Role != MobileRole.Fan ||
            (fanIdentity.IdentityId == IdentityId) != (Role == MobileRole.Fan) ||
            !string.Equals(NormalizedPhone, fanIdentity.NormalizedPhone, StringComparison.Ordinal))
        {
            throw new MobileSessionAccessException(MobileSessionDenial.TargetIdentityUnavailable);
        }

        // Revoking a club role must still permit the same verified person to use
        // their regular fan identity. This path can never grant another club role.
        return new MobileRoleSwitch(fanIdentity.IdentityId == IdentityId && Role == MobileRole.Fan,
            this with { IdentityId = fanIdentity.IdentityId, Role = MobileRole.Fan });
    }

    public MobileRoleSwitch SwitchRole(
        MobileIdentityGrant currentIdentity,
        MobileIdentityGrant targetIdentity,
        DateTimeOffset now)
    {
        if (ExpiresAt <= now)
        {
            throw new MobileSessionAccessException(MobileSessionDenial.Expired);
        }

        if (!MatchesCurrentGrant(currentIdentity))
        {
            throw new MobileSessionAccessException(MobileSessionDenial.CurrentIdentityUnavailable);
        }

        if (!targetIdentity.IsActive ||
            targetIdentity.IdentityId == Guid.Empty ||
            !Enum.IsDefined(targetIdentity.Role) ||
            (targetIdentity.IdentityId == IdentityId) != (targetIdentity.Role == Role) ||
            !string.Equals(NormalizedPhone, targetIdentity.NormalizedPhone, StringComparison.Ordinal))
        {
            throw new MobileSessionAccessException(MobileSessionDenial.TargetIdentityUnavailable);
        }

        if (targetIdentity.Role == Role)
        {
            return new MobileRoleSwitch(true, this);
        }

        return new MobileRoleSwitch(false, this with
        {
            IdentityId = targetIdentity.IdentityId,
            Role = targetIdentity.Role
        });
    }

    private bool MatchesCurrentGrant(MobileIdentityGrant identity) =>
        DeviceSessionId != Guid.Empty &&
        IdentityId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(NormalizedPhone) &&
        Enum.IsDefined(Role) &&
        identity.IsActive &&
        identity.IdentityId == IdentityId &&
        identity.Role == Role &&
        string.Equals(identity.NormalizedPhone, NormalizedPhone, StringComparison.Ordinal);
}

public enum MobileSessionDenial
{
    Expired,
    CurrentIdentityUnavailable,
    TargetIdentityUnavailable
}

public sealed class MobileSessionAccessException(MobileSessionDenial reason)
    : Exception("The mobile identity is not available for this session.")
{
    public MobileSessionDenial Reason { get; } = reason;
}
