using System.Security.Cryptography;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;

var now = DateTimeOffset.Parse("2026-10-25T09:00:00Z");
var phone = "+35799000000";
var parent = new MobileIdentityGrant(Guid.NewGuid(), phone, MobileRole.Parent, true);
var fan = new MobileIdentityGrant(Guid.NewGuid(), phone, MobileRole.Fan, true);
var session = new MobileSession(Guid.NewGuid(), parent.IdentityId, phone, parent.Role, now.AddHours(3));
var digest = SHA256.HashData("test-only-challenge"u8);

var checks = new (string Name, Action Check)[]
{
    ("Legacy mobile omission retains 90 days", () =>
        Equal(TimeSpan.FromDays(90), RememberDevicePolicy.Resolve(SessionRealm.Mobile, false, null))),
    ("Explicit mobile null uses 24 hours", () =>
        Equal(TimeSpan.FromDays(1), RememberDevicePolicy.Resolve(SessionRealm.Mobile, true, null))),
    ("Admin omission uses 24 hours", () =>
        Equal(TimeSpan.FromDays(1), RememberDevicePolicy.Resolve(SessionRealm.Admin, false, null))),
    ("Every allowed remember duration is retained", () =>
    {
        foreach (var days in new[] { 30, 60, 90 })
        {
            Equal(TimeSpan.FromDays(days), RememberDevicePolicy.Resolve(SessionRealm.Mobile, true, days));
        }
    }),
    ("Unsupported remember durations fail", () =>
    {
        foreach (var days in new[] { -1, 0, 1, 29, 31, 365 })
        {
            Throws<ArgumentOutOfRangeException>(() => RememberDevicePolicy.Resolve(SessionRealm.Mobile, true, days));
        }
    }),
    ("Role switch retains device and exact session expiry", () =>
    {
        var switched = session.SwitchRole(parent, fan, now.AddMinutes(10));
        Equal(false, switched.IsNoop);
        Equal(fan.IdentityId, switched.Session.IdentityId);
        Equal(MobileRole.Fan, switched.Session.Role);
        Equal(session.ExpiresAt, switched.Session.ExpiresAt);
        Equal(session.DeviceSessionId, switched.Session.DeviceSessionId);
    }),
    ("Same role is a no-op", () =>
        Equal(true, session.SwitchRole(parent, parent, now).IsNoop)),
    ("Expired device cannot switch role", () =>
        Throws<MobileSessionAccessException>(() => session.SwitchRole(parent, fan, session.ExpiresAt))),
    ("Revoked current identity cannot switch role", () =>
        Throws<MobileSessionAccessException>(() => session.SwitchRole(parent with { IsActive = false }, fan, now))),
    ("Revoked club role can recover only its same-phone fan identity", () =>
    {
        var recovered = session.RecoverFanAccess(fan, now);
        Equal(MobileRole.Fan, recovered.Session.Role);
        Equal(session.ExpiresAt, recovered.Session.ExpiresAt);
        Throws<MobileSessionAccessException>(() => session.RecoverFanAccess(parent, now));
        Throws<MobileSessionAccessException>(() => session.RecoverFanAccess(fan with { NormalizedPhone = "+35799111111" }, now));
    }),
    ("One identity ID cannot change its immutable role", () =>
    {
        Throws<MobileSessionAccessException>(() => session.SwitchRole(parent, fan with { IdentityId = parent.IdentityId }, now));
        Throws<MobileSessionAccessException>(() => session.RecoverFanAccess(fan with { IdentityId = parent.IdentityId }, now));
        var fanSession = session.SwitchRole(parent, fan, now).Session;
        Throws<MobileSessionAccessException>(() => fanSession.RecoverFanAccess(fan with { IdentityId = Guid.NewGuid() }, now));
    }),
    ("Revoked target identity cannot be selected", () =>
        Throws<MobileSessionAccessException>(() => session.SwitchRole(parent, fan with { IsActive = false }, now))),
    ("Other phone identity cannot be selected", () =>
        Throws<MobileSessionAccessException>(() => session.SwitchRole(parent, fan with { NormalizedPhone = "+35799111111" }, now))),
    ("Unknown mobile role is rejected", () =>
    {
        Equal(false, MobileRoles.TryParse("super_admin", out _));
        Equal(false, MobileRoles.TryParse("Parent", out _));
        Throws<MobileSessionAccessException>(() => session.SwitchRole(parent, fan with { Role = (MobileRole)999 }, now));
    }),
    ("No organization assignment grants no data access", () =>
    {
        var scope = OrganizationScope.Resolve([], OrganizationIds.Club);
        Equal(OrganizationScopeStatus.Denied, scope.Status);
        Equal(false, scope.CanAccess(OrganizationIds.Club));
        Equal(false, scope.CanAccess(OrganizationIds.Academy));
    }),
    ("Both organizations require explicit selection", () =>
    {
        var scope = OrganizationScope.Resolve(OrganizationIds.All, null);
        Equal(OrganizationScopeStatus.SelectionRequired, scope.Status);
        Equal(false, scope.CanAccess(OrganizationIds.Club));
        Equal(false, scope.CanAccess(OrganizationIds.Academy));
    }),
    ("One assignment becomes active without broadening access", () =>
    {
        var scope = OrganizationScope.Resolve([OrganizationIds.Academy], OrganizationIds.Club);
        Equal(OrganizationIds.Academy, scope.SelectedId!);
        Equal(true, scope.CanAccess(OrganizationIds.Academy));
        Equal(false, scope.CanAccess(OrganizationIds.Club));
    }),
    ("Selected club cannot access academy data", () =>
    {
        var scope = OrganizationScope.Select(OrganizationIds.All, OrganizationIds.Club);
        Equal(true, scope.CanAccess(OrganizationIds.Club));
        Equal(false, scope.CanAccess(OrganizationIds.Academy));
    }),
    ("Unassigned organization selection is denied", () =>
        Equal(OrganizationScopeStatus.Denied, OrganizationScope.Select([OrganizationIds.Club], OrganizationIds.Academy).Status)),
    ("Unknown organization never grants access", () =>
    {
        Equal(OrganizationScopeStatus.InvalidSelection, OrganizationScope.Select(OrganizationIds.All, "other-club").Status);
        Equal(OrganizationScopeStatus.Denied, OrganizationScope.Resolve(["other-club"], "other-club").Status);
    }),
    ("OTP role selection keeps challenge; selected identity consumes it", () =>
    {
        var challenge = NewChallenge();
        Equal(OtpVerificationOutcome.Accepted, challenge.Verify(digest, now, false));
        Equal(false, challenge.IsConsumed);
        Equal(OtpVerificationOutcome.Accepted, challenge.Verify(digest, now, true));
        Equal(true, challenge.IsConsumed);
        Equal(OtpVerificationOutcome.AlreadyConsumed, challenge.Verify(digest, now, true));
    }),
    ("OTP expires exactly at its deadline", () =>
    {
        var challenge = NewChallenge();
        Equal(OtpVerificationOutcome.Expired, challenge.Verify(digest, challenge.ExpiresAt, true));
        Equal(0, challenge.VerificationCalls);
    }),
    ("OTP never works before creation and cannot exceed five minutes", () =>
    {
        Equal(OtpVerificationOutcome.NotYetValid, NewChallenge().Verify(digest, now.AddSeconds(-1), true));
        Throws<ArgumentOutOfRangeException>(() => new OtpChallenge(Guid.NewGuid(), digest, now, now.AddMinutes(6)));
        Throws<ArgumentOutOfRangeException>(() => new OtpChallenge(Guid.NewGuid(), digest, now, now));
    }),
    ("Rehydrated OTP attempts cannot reset a spent challenge", () =>
    {
        var challenge = new OtpChallenge(Guid.NewGuid(), digest, now, now.AddMinutes(5), 5);
        Equal(OtpVerificationOutcome.TooManyAttempts, challenge.Verify(digest, now, true));
        var consumed = new OtpChallenge(Guid.NewGuid(), digest, now, now.AddMinutes(5), 1, true);
        Equal(OtpVerificationOutcome.AlreadyConsumed, consumed.Verify(digest, now, true));
        Throws<ArgumentOutOfRangeException>(() => new OtpChallenge(Guid.NewGuid(), digest, now, now.AddMinutes(5), -1));
    }),
    ("Sixth OTP verification is refused even with the correct code", () =>
    {
        var challenge = NewChallenge();
        for (var i = 0; i < OtpChallenge.MaximumVerificationCalls; i++)
        {
            Equal(OtpVerificationOutcome.InvalidCode, challenge.Verify(new byte[32], now, true));
        }
        Equal(OtpVerificationOutcome.TooManyAttempts, challenge.Verify(digest, now, true));
        Equal(false, challenge.IsConsumed);
    }),
    ("OTP hash input cannot mutate a stored challenge", () =>
    {
        var supplied = (byte[])digest.Clone();
        var challenge = new OtpChallenge(Guid.NewGuid(), supplied, now, now.AddMinutes(5));
        supplied[0] ^= 0xff;
        Equal(OtpVerificationOutcome.Accepted, challenge.Verify(digest, now, false));
    })
};

var failures = 0;
foreach (var (name, check) in checks)
{
    try
    {
        check();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{checks.Length - failures}/{checks.Length} policy checks passed.");
return failures == 0 ? 0 : 1;

OtpChallenge NewChallenge() => new(Guid.NewGuid(), digest, now, now.AddMinutes(5));

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}; observed {actual}.");
    }
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
