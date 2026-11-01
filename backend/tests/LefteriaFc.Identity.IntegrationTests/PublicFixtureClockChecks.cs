using LefteriaFc.Application.PublicContent;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class PublicFixtureClockChecks
{
    public static void Run()
    {
        var winter = new DateOnly(2026, 11, 7);
        var summer = new DateOnly(2026, 7, 7);
        if (PublicFixtureClock.KickoffAt(winter, "14:30") != DateTimeOffset.Parse("2026-11-07T12:30:00Z") ||
            PublicFixtureClock.KickoffAt(summer, "14:30") != DateTimeOffset.Parse("2026-07-07T11:30:00Z") ||
            PublicFixtureClock.KickoffAt(winter, "14:30:25") != DateTimeOffset.Parse("2026-11-07T12:30:25Z"))
            throw new InvalidOperationException("Club kickoff conversion lost the Cyprus offset or seconds.");
        foreach (var time in new string?[] { null, "", "25:00", "14:30Z", "invalid" })
            if (PublicFixtureClock.KickoffAt(winter, time) is not null)
                throw new InvalidOperationException("An unknown kickoff received a guessed instant.");
        if (PublicFixtureClock.KickoffAt(new DateOnly(2026, 3, 29), "03:30") is not null ||
            PublicFixtureClock.KickoffAt(new DateOnly(2026, 10, 25), "03:30") is not null)
            throw new InvalidOperationException("A skipped or ambiguous Cyprus wall time received a guessed offset.");
        Console.WriteLine("PASS public fixture clock: winter, summer, seconds, missing/invalid, DST gap/fold.");
    }
}
