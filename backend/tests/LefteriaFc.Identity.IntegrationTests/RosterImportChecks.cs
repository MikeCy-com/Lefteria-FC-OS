using System.Text.Json.Nodes;
using LefteriaFc.Application.Roster;
using LefteriaFc.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private ValidatedRosterImport? rosterFixture;

    private async Task RosterImportAsync()
    {
        await using (var context = database.CreateContext())
            Check.True(!await context.RosterImportStates.AnyAsync() && !await context.RosterPlayers.AnyAsync(), "Roster fixture must start empty and uninitialized.");
        using var client = Client();
        var phone = database.NewPhone(); await database.SeedIdentityAsync(phone, MobileRole.Fan);
        await LoginAsync(client, phone, "fan");
        var unavailable = await GetAsync(client, "/api/mobile/players-list", 503);
        Check.Equal("roster_unavailable", unavailable.GetProperty("code").GetString(), "Missing roster readiness became fabricated empty success.");
        var empty = RosterManifest("empty"); empty["teams"] = new JsonArray(); empty["academy_groups"] = new JsonArray();
        var emptyPlan = RosterImportValidator.Validate(RosterBytes(empty));
        Check.True((await database.ApplyRosterAsync(emptyPlan)).Applied, "Verified empty initial snapshot was refused.");
        Check.Equal(0, (await GetAsync(client, "/api/mobile/players-list", 200)).GetProperty("players").GetArrayLength(), "Verified empty import did not return an empty list.");
        await database.CleanupRosterImportAsync(emptyPlan);

        var orphanId = "uninitialized-" + Guid.NewGuid().ToString("N");
        await using (var context = database.CreateContext())
        {
            context.RosterTeams.Add(new LefteriaFc.Infrastructure.Roster.RosterTeamRecord { Id = orphanId, OrganizationId = "lefteria_fc", Name = "Uninitialized fixture" });
            await context.SaveChangesAsync();
            try
            {
                var refused = false;
                try { await database.ApplyRosterAsync(emptyPlan); }
                catch (RosterImportApplyException exception) when (exception.Code == "uninitialized_target_not_empty") { refused = true; }
                Check.True(refused, "Importer adopted uninitialized existing data.");
            }
            finally { await context.RosterTeams.Where(row => row.Id == orphanId).ExecuteDeleteAsync(); }
        }

        var prefix = "roster-" + Guid.NewGuid().ToString("N");
        var manifest = RosterManifest(prefix); var players = manifest["players"]!.AsArray();
        for (var number = 0; number < 60; number++)
        {
            var player = RosterPlayer(prefix + "-" + number.ToString("D2"), "Fixture player " + number, number / 2);
            player["team_id"] = prefix + "-team"; players.Add(player);
        }
        foreach (var flag in new[] { "is_active", "is_archived", "is_transferred" })
        {
            var player = RosterPlayer(prefix + "-excluded-" + flag, "Excluded lifecycle fixture", -10);
            player[flag] = flag != "is_active"; players.Add(player);
        }
        foreach (var field in new[] { "is_active", "team_type" })
        {
            var player = RosterPlayer(prefix + "-missing-" + field, "Missing source picker field", -50);
            player.Remove(field);
            player["has_explicit_picker_fields"] = true;
            players.Add(player);
        }
        var academy = RosterPlayer(prefix + "-academy", "Private academy fixture", -20);
        academy["team_type"] = "Academy"; academy["academy_group_id"] = prefix + "-group";
        academy["academy_group_ids"] = new JsonArray(prefix + "-group", prefix + "-group"); players.Add(academy);
        var explicitOrg = RosterPlayer(prefix + "-explicit-org", "Explicit academy organization", -30);
        explicitOrg["organization_id"] = "lefteria_fc_academy"; players.Add(explicitOrg);
        var explicitFc = RosterPlayer(prefix + "-explicit-fc", "Explicit FC with academy type", -40);
        explicitFc["organization_id"] = "lefteria_fc"; explicitFc["team_type"] = "Academy"; players.Add(explicitFc);
        rosterFixture = RosterImportValidator.Validate(RosterBytes(manifest));
        Check.Equal(1, rosterFixture.MembershipCount, "Primary/plural group union was not deduplicated.");

        var trigger = "roster_failure_" + Guid.NewGuid().ToString("N");
        var create = "CREATE FUNCTION migration.PROBE() RETURNS trigger LANGUAGE plpgsql AS $failure$ BEGIN IF NEW.import_digest='DIGEST' THEN RAISE EXCEPTION 'Isolated roster archive failure'; END IF; RETURN NEW; END; $failure$; CREATE TRIGGER PROBE BEFORE INSERT ON migration.roster_source_documents FOR EACH ROW EXECUTE FUNCTION migration.PROBE();"
            .Replace("PROBE", trigger, StringComparison.Ordinal).Replace("DIGEST", rosterFixture.Digest, StringComparison.Ordinal);
        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(create);
            try
            {
                var failed = false;
                try { await database.ApplyRosterAsync(rosterFixture); }
                catch (DbUpdateException) { failed = true; }
                Check.True(failed, "Archive storage failure unexpectedly completed an import.");
                Check.Equal(0, await context.RosterPlayers.CountAsync(), "Failed archive left committed typed rows.");
                Check.Equal(0, await context.RosterTeams.CountAsync(), "Failed archive left committed teams.");
                Check.Equal(0, await context.RosterImportStates.CountAsync(), "Failed archive committed readiness early.");
                Check.Equal(0, await context.SecurityAuditEvents.CountAsync(row => row.SubjectDigest == rosterFixture.Digest), "Failed import committed success audit.");
            }
            finally
            {
                // Generated UUID identifier only, never input from the manifest.
                var cleanup = "DROP TRIGGER IF EXISTS PROBE ON migration.roster_source_documents; DROP FUNCTION IF EXISTS migration.PROBE();"
                    .Replace("PROBE", trigger, StringComparison.Ordinal);
                await context.Database.ExecuteSqlRawAsync(cleanup);
            }
        }
        var results = await Task.WhenAll(database.ApplyRosterAsync(rosterFixture), database.ApplyRosterAsync(rosterFixture));
        Check.Equal(1, results.Count(result => result.Applied), "Concurrent initial imports both wrote data.");
        Check.Equal(1, results.Count(result => result.Noop), "Concurrent exact digest replay was not a no-op.");
        Check.True((await database.ApplyRosterAsync(rosterFixture)).Noop, "Exact digest reapply did not no-op.");
        try { await database.ApplyRosterAsync(emptyPlan); }
        catch (RosterImportApplyException exception) when (exception.Code == "initial_snapshot_already_applied")
        {
            await using var context = database.CreateContext();
            Check.Equal(rosterFixture.Players.Count, await context.RosterPlayers.CountAsync(), "Rejected new digest overwrote current rows.");
            Check.Equal(1, await context.SecurityAuditEvents.CountAsync(row => row.SubjectDigest == rosterFixture.Digest), "Digest replay emitted duplicate import audits.");
            var defaulted = await context.RosterPlayers.Where(row => row.Id.StartsWith(prefix + "-missing-")).ToArrayAsync();
            Check.Equal(2, defaulted.Length, "Missing source fields were silently dropped instead of archived/imported.");
            Check.True(defaulted.All(row => row.IsActive && row.TeamType == "First Team" && !row.HasExplicitPickerFields),
                "Canonical defaults broadened explicit source picker provenance.");
            var archive = await context.RosterSourceDocuments.FirstAsync(row => row.Collection == "players");
            Check.True(archive.SourceJson.Contains("fixture-private@example.invalid", StringComparison.Ordinal), "Owner-only raw archive lost source values.");
            return;
        }
        throw new InvalidOperationException("A different initial snapshot overwrote initialized roster data.");
    }
}
