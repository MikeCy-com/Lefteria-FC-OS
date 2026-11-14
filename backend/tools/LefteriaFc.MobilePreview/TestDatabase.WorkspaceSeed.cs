using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.AccountImport;
using LefteriaFc.Infrastructure.ClubCalendar;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

// Tool-only persistent workspace seed. Uses the existing calendar-mode fixtures,
// minus the imported admin record that would close founding owner setup.
internal sealed partial class TestDatabase
{
    public async Task<AccountImportFixture> SeedWorkspaceCalendarAsync()
    {
        await using (var database = CreateContext())
        {
            if (await database.MobileIdentities.AnyAsync() || await database.AdminAccounts.AnyAsync() ||
                await database.RosterImportStates.AnyAsync() || await database.StaffRelationshipImportStates.AnyAsync() ||
                await database.AccountImportStates.AnyAsync() || await database.CalendarImportStates.AnyAsync() ||
                await database.CalendarItems.AnyAsync())
                throw new InvalidOperationException("The workspace seed requires an empty guarded fixture.");
        }

        var accounts = WithoutAdminRecords(AccountImportFixture.Create(this, calendarScenario: true));
        var calendar = CalendarDatabaseFixture.Create(accounts);
        if (calendar.Plan.QuarantinedRows != 0)
            throw new InvalidOperationException("The fictional calendar fixture must have a complete clean import.");
        await ApplyRosterAsync(accounts.Roster);
        await ApplyStaffAsync(accounts.Staff);
        await ApplyAccountsAsync(accounts);
        var outcome = await PostgresCalendarImport.ApplyAsync(calendar.Manifest, fixture.AdminConnectionString,
            calendar.Manifest.Digest, FixtureConfiguration.ExpectedDatabase, "development");
        if (!outcome.Applied && !outcome.Noop)
            throw new InvalidOperationException("The fictional calendar import was not applied.");
        return accounts;
    }

    // Any admin record closes founding setup, so the imported management admin
    // and its linked management mobile role are deferred (REQ-27).
    private static AccountImportFixture WithoutAdminRecords(AccountImportFixture source)
    {
        var input = source.Input.DeepClone().AsObject();
        input["admin_users"] = new JsonArray();
        var mobiles = input["mobile_users"]!.AsArray();
        foreach (var row in mobiles.Where(row => (string?)row!["role"] == "management").ToArray()) mobiles.Remove(row);
        var manifest = AccountImportValidator.Validate(JsonSerializer.SerializeToUtf8Bytes(input));
        var plan = AccountImportPlanner.Build(manifest, new AccountImportTrustedContext(source.Roster.Digest,
            source.Staff.Digest, AccountImportFixture.Commit, source.Roster, source.Staff));
        return new AccountImportFixture
        {
            Prefix = source.Prefix, PlayerPhone = source.PlayerPhone, ParentPhone = source.ParentPhone,
            StaffPhone = source.StaffPhone, EvidencePhone = source.EvidencePhone, InactivePhone = source.InactivePhone,
            StalePhone = source.StalePhone, ConflictPhone = source.ConflictPhone, Input = input,
            Roster = source.Roster, Staff = source.Staff, Manifest = manifest, Plan = plan
        };
    }
}
