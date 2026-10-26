using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.Roster;
using LefteriaFc.Application.StaffRelationships;
using LefteriaFc.Domain.Organizations;
using LefteriaFc.Infrastructure.StaffRelationships;
using LefteriaFc.Tools.RosterImport;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private ValidatedStaffRelationshipImport? staffFixture;
    private static async Task StaffValidationAsync()
    {
        var manifest = StaffManifest(new string('a', 64)); var people = manifest["staff"]!.AsArray();
        var person = StaffPerson("opaque-shared-staff");
        person["team_ids"] = new JsonArray("opaque-team", "opaque-team"); person["team_id"] = "opaque-team";
        person["academy_group_ids"] = new JsonArray("opaque-group"); person["academy_group_id"] = "opaque-group";
        person["phone"] = " +357 (99) 123456 "; person["email"] = ""; person["country"] = "CY";
        person["has_login"] = true; person["admin_role"] = "super_admin"; people.Add(person);
        var plan = StaffRelationshipImportValidator.Validate(RosterBytes(manifest)); var first = plan.Staff.Single();
        Check.True(first.IsActive && !first.IsArchived && !first.HasExplicitActive && !first.HasExplicitArchived, "Staff status defaults lost presence provenance.");
        Check.True(first.Teams.Single().FromPlural && first.Teams.Single().FromSingular && first.Groups.Single().FromSingular, "Staff singular/plural assignments were not deduplicated.");
        Check.Equal(" +357 (99) 123456 ", plan.Contacts.Single(row => row.Field == "phone").RawValue, "Contact provenance normalized a phone.");
        Check.Equal("empty", plan.Contacts.Single(row => row.Field == "email").Presence, "Explicit empty contact lost its presence.");
        Check.True(plan.Source.Single().Json.Contains("super_admin", StringComparison.Ordinal), "Owner archive lost source login mirror metadata.");
        foreach (var (field, value) in new (string, JsonNode?)[] { ("is_active", null), ("is_archived", "false"), ("role", "coach"), ("team_ids", null),
            ("academy_group_ids", "group"), ("team_id", 9), ("phone", 9), ("email", new string('a',321)), ("organization_id", "unknown") })
        {
            var invalid = manifest.DeepClone().AsObject(); invalid["staff"]![0]![field] = value; ExpectInvalidStaff(RosterBytes(invalid));
        }
        var duplicate = manifest.DeepClone().AsObject(); duplicate["staff"]!.AsArray().Add(person.DeepClone()); ExpectInvalidStaff(RosterBytes(duplicate));
        var nested = Encoding.UTF8.GetString(RosterBytes(manifest)).Replace("\"country\":\"CY\"", "\"private\":{\"key\":1,\"key\":2}", StringComparison.Ordinal);
        ExpectInvalidStaff(Encoding.UTF8.GetBytes(nested));
        foreach (var value in new JsonNode?[] { null, JsonValue.Create("") })
        {
            var contact = manifest.DeepClone().AsObject(); contact["staff"]![0]!["phone"] = value;
            var result = StaffRelationshipImportValidator.Validate(RosterBytes(contact));
            Check.Equal(value is null ? "null" : "empty", result.Contacts.First(row => row.Field == "phone").Presence, "Raw contact null/empty semantics changed.");
        }
        var oldConnection = Environment.GetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION"); var output = Console.Out; var errors = Console.Error;
        var path = Path.Combine(Path.GetTempPath(), "lefteria-staff-validation-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllBytesAsync(path, RosterBytes(manifest)); Environment.SetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION", "invalid-no-network");
            using var captured = new StringWriter(); Console.SetOut(captured); Console.SetError(captured);
            Check.Equal(0, await RosterImportProgram.Main(["--kind", "staff-relationships", "--input", path]), "Staff dry-run contacted a database.");
            Check.True(!captured.ToString().Contains("123456", StringComparison.Ordinal) && !captured.ToString().Contains("Fixture staff", StringComparison.Ordinal), "Staff CLI printed private values.");
        }
        finally { Console.SetOut(output); Console.SetError(errors); Environment.SetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION", oldConnection); File.Delete(path); }
    }
    private async Task StaffEmptySnapshotAsync()
    {
        var missing = StaffRelationshipImportValidator.Validate(RosterBytes(StaffManifest(new string('a',64))));
        await ExpectStaffRefusalAsync(missing, "roster_snapshot_required");
        var manifest = RosterManifest("staff-empty"); manifest["teams"] = new JsonArray(); manifest["academy_groups"] = new JsonArray();
        var roster = RosterImportValidator.Validate(RosterBytes(manifest)); await database.ApplyRosterAsync(roster);
        var empty = StaffRelationshipImportValidator.Validate(RosterBytes(StaffManifest(roster.Digest)));
        await using (var context = database.CreateContext()) Check.True(!await context.StaffRelationshipImportStates.AnyAsync(), "Missing staff readiness was fabricated.");
        Check.True((await database.ApplyStaffAsync(empty)).Applied, "Verified empty staff import was rejected.");
        await using (var context = database.CreateContext())
        {
            var state = await context.StaffRelationshipImportStates.SingleAsync();
            Check.Equal(0, state.StaffCount, "Verified empty staff state has fabricated staff.");
            Check.Equal(0, state.ContactsCount, "Verified empty base roster has fabricated contacts.");
        }
        Check.True((await database.ApplyStaffAsync(empty)).Noop, "Empty staff snapshot replay was not a no-op.");
        await database.CleanupStaffImportAsync(empty); await database.CleanupRosterImportAsync(roster);
    }
    private async Task StaffImportAsync()
    {
        var roster = rosterFixture ?? throw new InvalidOperationException("Roster was not imported.");
        var prefix = roster.Teams.Single().Id[..^5];
        var manifest = StaffManifest(roster.Digest); var people = manifest["staff"]!.AsArray();
        var shared = StaffPerson(prefix + "-shared-staff"); shared["organization_id"] = OrganizationIds.Club;
        shared["team_ids"] = new JsonArray(roster.Teams.Single().Id, roster.Teams.Single().Id); shared["team_id"] = roster.Teams.Single().Id;
        shared["academy_group_id"] = roster.Groups.Single().Id; shared["academy_group_ids"] = new JsonArray(roster.Groups.Single().Id);
        shared["phone"] = "+357 99 654321"; shared["email"] = "staff-private@example.invalid"; shared["country"] = "CY";
        shared["has_login"] = true; shared["admin_role"] = "super_admin"; people.Add(shared);
        var implicitCoach = StaffPerson(prefix + "-implicit-staff"); implicitCoach["role"] = "Physiotherapist"; implicitCoach["is_active"] = false; people.Add(implicitCoach);
        staffFixture = StaffRelationshipImportValidator.Validate(RosterBytes(manifest));
        var wrongDigest = manifest.DeepClone().AsObject(); wrongDigest["roster_sha256"] = new string('b',64);
        await ExpectStaffRefusalAsync(StaffRelationshipImportValidator.Validate(RosterBytes(wrongDigest)), "roster_digest_mismatch");
        var wrongCommit = manifest.DeepClone().AsObject(); wrongCommit["source_commit"] = new string('c',40);
        await ExpectStaffRefusalAsync(StaffRelationshipImportValidator.Validate(RosterBytes(wrongCommit)), "source_commit_mismatch");
        var dangling = manifest.DeepClone().AsObject(); dangling["staff"]![0]!["team_id"] = "dangling-team";
        await ExpectStaffRefusalAsync(StaffRelationshipImportValidator.Validate(RosterBytes(dangling)), "staff_team_reference");
        await ExpectStaffRefusalAsync(StaffRelationshipImportValidator.Validate(RosterBytes(StaffManifest(roster.Digest))), "group_coach_reference");
        await using var context = database.CreateContext();
        var before = (await context.MobileIdentities.CountAsync(), await context.AdminAccounts.CountAsync(), await context.AdminOrganizationGrants.CountAsync());
        var orphan = "staff-uninitialized-" + Guid.NewGuid().ToString("N");
        context.Staff.Add(new() { Id = orphan, Name = "Uninitialized staff", JobRole = "Scout", TeamType = "First Team", IsActive = true }); await context.SaveChangesAsync();
        try { await ExpectStaffRefusalAsync(staffFixture, "staff_uninitialized_target_not_empty"); }
        finally { await context.Staff.Where(row => row.Id == orphan).ExecuteDeleteAsync(); }
        var probe = "staff_failure_" + Guid.NewGuid().ToString("N");
        // Generated UUID identifier and validated SHA-256, never manifest text.
        var create = "CREATE FUNCTION migration.PROBE() RETURNS trigger LANGUAGE plpgsql AS $failure$ BEGIN IF NEW.import_digest='DIGEST' THEN RAISE EXCEPTION 'Isolated staff archive failure'; END IF; RETURN NEW; END; $failure$; CREATE TRIGGER PROBE BEFORE INSERT ON migration.staff_source_documents FOR EACH ROW EXECUTE FUNCTION migration.PROBE();"
            .Replace("PROBE", probe, StringComparison.Ordinal).Replace("DIGEST", staffFixture.Digest, StringComparison.Ordinal);
        await context.Database.ExecuteSqlRawAsync(create);
        try
        {
            var failed = false; try { await database.ApplyStaffAsync(staffFixture); } catch (DbUpdateException) { failed = true; }
            Check.True(failed, "Synthetic staff archive failure did not abort import.");
            Check.Equal(0, await context.Staff.CountAsync(), "Failed staff import committed staff rows.");
            Check.Equal(0, await context.ContactProvenance.CountAsync(), "Failed staff import committed contacts.");
            Check.Equal(0, await context.StaffRelationshipImportStates.CountAsync(), "Failed staff import committed readiness.");
            Check.Equal(0, await context.SecurityAuditEvents.CountAsync(row => row.SubjectDigest == staffFixture.Digest), "Failed staff import committed audit.");
        }
        finally
        {
            var cleanup = "DROP TRIGGER IF EXISTS PROBE ON migration.staff_source_documents; DROP FUNCTION IF EXISTS migration.PROBE();".Replace("PROBE", probe, StringComparison.Ordinal);
            await context.Database.ExecuteSqlRawAsync(cleanup);
        }
        var results = await Task.WhenAll(database.ApplyStaffAsync(staffFixture), database.ApplyStaffAsync(staffFixture));
        Check.Equal(1, results.Count(row => row.Applied), "Concurrent staff imports both wrote."); Check.Equal(1, results.Count(row => row.Noop), "Concurrent staff replay did not no-op.");
        Check.True((await database.ApplyStaffAsync(staffFixture)).Noop, "Staff exact replay did not no-op.");
        var sharedId = staffFixture.Staff.Single(row => row.Teams.Count > 0).Id;
        var phoneRow = await context.ContactProvenance.AsNoTracking().SingleAsync(row => row.StaffId == sharedId && row.Field == "phone");
        var sourceRow = await context.StaffSourceDocuments.AsNoTracking().SingleAsync(row => row.SourceId == sharedId);
        await RejectSameCountMutationAsync(
            () => context.Staff.Where(row => row.Id == sharedId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsActive, false)),
            () => context.Staff.Where(row => row.Id == sharedId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsActive, true)));
        await RejectSameCountMutationAsync(
            () => context.ContactProvenance.Where(row => row.StaffId == sharedId && row.Field == "phone").ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RawValue, "changed-private-provenance")),
            () => context.ContactProvenance.Where(row => row.StaffId == sharedId && row.Field == "phone").ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RawValue, phoneRow.RawValue)));
        await RejectSameCountMutationAsync(
            () => context.StaffSourceDocuments.Where(row => row.SourceId == sharedId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SourceJson, " " + sourceRow.SourceJson)),
            () => context.StaffSourceDocuments.Where(row => row.SourceId == sharedId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SourceJson, sourceRow.SourceJson)));
        await RejectSameCountMutationAsync(
            () => context.StaffTeams.Where(row => row.StaffId == sharedId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FromSingular, false)),
            () => context.StaffTeams.Where(row => row.StaffId == sharedId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FromSingular, true)));
        Check.True((await database.ApplyStaffAsync(staffFixture)).Noop, "Restored exact staff data did not reconcile.");
        var changed = manifest.DeepClone().AsObject(); changed["staff"]![0]!["name"] = "Replacement is forbidden";
        await ExpectStaffRefusalAsync(StaffRelationshipImportValidator.Validate(RosterBytes(changed)), "staff_initial_snapshot_already_applied");
        Check.Equal(before, (await context.MobileIdentities.CountAsync(), await context.AdminAccounts.CountAsync(), await context.AdminOrganizationGrants.CountAsync()), "Staff import created identities, admins or grants.");
        Check.Equal(1, await context.SecurityAuditEvents.CountAsync(row => row.SubjectDigest == staffFixture.Digest), "Staff replay emitted duplicate success audits.");

        async Task RejectSameCountMutationAsync(Func<Task<int>> mutate, Func<Task<int>> restore)
        {
            await mutate();
            try { await ExpectStaffRefusalAsync(staffFixture, "staff_target_reconciliation_failed"); }
            finally { await restore(); }
        }
    }
    private async Task StaffIntegrityAsync()
    {
        var plan = staffFixture ?? throw new InvalidOperationException("Staff was not imported."); var roster = rosterFixture!;
        await using var context = database.CreateContext(); var shared = plan.Staff.Single(row => row.Teams.Count > 0);
        Check.Equal(1, await context.StaffTeams.CountAsync(row => row.StaffId == shared.Id && row.OrganizationId == OrganizationIds.Club), "FC staff assignment missing.");
        Check.Equal(1, await context.StaffAcademyGroups.CountAsync(row => row.StaffId == shared.Id && row.OrganizationId == OrganizationIds.Academy), "Shared person's Academy assignment was forced into FC.");
        var coach = await context.AcademyGroupCoaches.SingleAsync();
        var implied = await context.StaffAcademyGroups.SingleAsync(row => row.StaffId == coach.StaffId);
        Check.True(implied.FromGroupCoach && !implied.FromPlural && !implied.FromSingular, "Reverse coach link did not supply its independent group origin.");
        var staff = await context.Staff.SingleAsync(row => row.Id == coach.StaffId);
        Check.True(staff.JobRole == "Physiotherapist" && !staff.IsActive && staff.HasExplicitActive, "Noncoach/archived status was converted into an invented grant or dropped.");
        var player = roster.Players.Single(row => row.Id.EndsWith("-00", StringComparison.Ordinal));
        var own = await context.ContactProvenance.SingleAsync(row => row.PlayerId == player.Id && row.Field == "phone");
        var guardian = await context.ContactProvenance.SingleAsync(row => row.PlayerId == player.Id && row.Field == "parent_phone");
        Check.True(own.Kind == "player_own" && own.RawValue == "+357 99 123456" && guardian.Kind == "guardian" && guardian.RawValue == "+357 99 654321", "Player/guardian contacts were merged or normalized.");
        Check.Equal(2, await context.ContactProvenance.CountAsync(row => row.SourceCollection == "players" && row.Field == "phone" && row.RawValue == own.RawValue), "Duplicate phones were collapsed into inferred people.");
        Check.True(!await context.ContactProvenance.AnyAsync(row => row.Field == "emergency_contact_phone"), "Emergency phone became guardian authority.");
        Check.True((await context.StaffSourceDocuments.SingleAsync(row => row.SourceId == shared.Id)).SourceJson.Contains("\"country\":\"CY\"", StringComparison.Ordinal), "Staff country provenance was dropped.");
        await database.AssertStaffPrivatePermissionsAsync();
        await ExpectStaffRefusalAsync(plan, "owner_target_required", runtime: true);
        await database.AssertRejectedSqlAsync("UPDATE club.staff_teams SET organization_id=@org WHERE staff_id=@staff AND team_id=@team",
            PostgresErrorCodes.ForeignKeyViolation, new("staff", shared.Id), new("org", OrganizationIds.Academy), new("team", roster.Teams.Single().Id));
        await database.AssertRejectedSqlAsync("UPDATE club.academy_group_coaches SET staff_id=@staff WHERE academy_group_id=@group",
            PostgresErrorCodes.ForeignKeyViolation, new("group", roster.Groups.Single().Id), new("staff", "dangling-staff"));
        await database.AssertRejectedSqlAsync("UPDATE migration.contact_provenance SET presence='empty',raw_value=NULL WHERE staff_id=@staff AND field='phone'",
            PostgresErrorCodes.CheckViolation, new NpgsqlParameter("staff", shared.Id));
        using var client = Client(); var phone = database.NewPhone(); await database.SeedIdentityAsync(phone, LefteriaFc.Domain.Identity.MobileRole.Fan); await LoginAsync(client, phone, "fan");
        var response = await GetAsync(client, "/api/mobile/players-list", 200);
        Check.True(!response.GetRawText().Contains("123456", StringComparison.Ordinal) && !response.GetRawText().Contains("guardian-private", StringComparison.Ordinal) && !response.GetRawText().Contains("staff-private", StringComparison.Ordinal), "Picker leaked private provenance.");
    }
    private async Task ExpectStaffRefusalAsync(ValidatedStaffRelationshipImport plan, string code, bool runtime = false)
    {
        try { await database.ApplyStaffAsync(plan, runtime); }
        catch (RosterImportApplyException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException("Staff import did not refuse expected fixed code: " + code);
    }
    private static void ExpectInvalidStaff(byte[] bytes)
    {
        try { StaffRelationshipImportValidator.Validate(bytes); } catch (RosterImportValidationException) { return; }
        throw new InvalidOperationException("Staff manifest accepted invalid or ambiguous source input.");
    }
    private static JsonObject StaffManifest(string rosterDigest) => new()
    {
        ["schema_version"] = 1, ["source_repository"] = "MikeCy-com/Lefteria-FC", ["source_commit"] = "7833daceceac1611c368e7bc4df2a82c64b45e1a",
        ["roster_sha256"] = rosterDigest, ["complete_collections"] = new JsonArray("staff"), ["staff"] = new JsonArray()
    };
    private static JsonObject StaffPerson(string id) => new() { ["id"] = id, ["name"] = "Fixture staff", ["role"] = "Head Coach" };
}
