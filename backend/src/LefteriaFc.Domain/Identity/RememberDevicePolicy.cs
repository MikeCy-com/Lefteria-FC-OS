namespace LefteriaFc.Domain.Identity;

public enum SessionRealm
{
    Admin,
    Mobile
}

public static class RememberDevicePolicy
{
    // Presence matters: older mobile clients omit the property and expect 90 days.
    // An explicit null always means the normal 24-hour device session.
    public static TimeSpan Resolve(SessionRealm realm, bool wasProvided, int? days)
    {
        if (!Enum.IsDefined(realm))
        {
            throw new ArgumentOutOfRangeException(nameof(realm));
        }

        if (days is not null and not (30 or 60 or 90))
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Remember days must be 30, 60 or 90.");
        }

        if (!wasProvided && days.HasValue)
        {
            throw new ArgumentException("An omitted remember selection cannot contain a value.", nameof(days));
        }

        if (!wasProvided && realm == SessionRealm.Mobile)
        {
            return TimeSpan.FromDays(90);
        }

        return TimeSpan.FromDays(days ?? 1);
    }
}
