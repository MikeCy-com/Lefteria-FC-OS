using LefteriaFc.Infrastructure.ClubCalendar;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

// Tool-only additions to the linked guarded fixture, never API registration.
internal sealed partial class TestDatabase
{
    public async Task<CalendarDatabaseFixture> SeedPreviewCalendarAsync()
    {
        await using (var database = CreateContext())
        {
            if (await database.MobileIdentities.AnyAsync() || await database.AdminAccounts.AnyAsync() ||
                await database.RosterImportStates.AnyAsync() || await database.StaffRelationshipImportStates.AnyAsync() ||
                await database.AccountImportStates.AnyAsync() || await database.CalendarImportStates.AnyAsync() ||
                await database.CalendarItems.AnyAsync())
                throw new InvalidOperationException("The calendar preview requires an empty guarded disposable fixture.");
        }

        var accounts = AccountImportFixture.Create(this, calendarScenario: true);
        var calendar = CalendarDatabaseFixture.Create(accounts);
        if (calendar.Plan.QuarantinedRows != 0)
            throw new InvalidOperationException("The fictional calendar preview must have a complete clean import.");
        await ApplyRosterAsync(accounts.Roster);
        await ApplyStaffAsync(accounts.Staff);
        await ApplyAccountsAsync(accounts);
        var outcome = await PostgresCalendarImport.ApplyAsync(calendar.Manifest, fixture.AdminConnectionString,
            calendar.Manifest.Digest, FixtureConfiguration.ExpectedDatabase, "development");
        if (!outcome.Applied && !outcome.Noop)
            throw new InvalidOperationException("The fictional calendar preview import was not applied.");
        // Existing cleanup rechecks exact manifest bytes, IDs and fictional actors
        // before deleting anything, and runs before account/staff/roster cleanup.
        calendarFixture = calendar;
        return calendar;
    }
}
