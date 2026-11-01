using System.Globalization;

namespace LefteriaFc.Application.PublicContent;

public static class PublicFixtureClock
{
    public const string TimeZone = "Asia/Nicosia";
    private static readonly TimeZoneInfo ClubTimeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    // This is a read view only. The imported wall date/time and original bytes
    // remain unchanged; a missing, invalid or ambiguous kickoff has no offset.
    public static DateTimeOffset? KickoffAt(DateOnly date, string? time)
    {
        if (time is null || !TimeOnly.TryParseExact(time, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var clock)) return null;
        var wall = date.ToDateTime(clock, DateTimeKind.Unspecified);
        if (ClubTimeZone.IsInvalidTime(wall) || ClubTimeZone.IsAmbiguousTime(wall)) return null;
        return new DateTimeOffset(wall, ClubTimeZone.GetUtcOffset(wall)).ToUniversalTime();
    }
}
