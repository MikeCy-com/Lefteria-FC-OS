using System.Net.Http.Headers;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task RosterPickerAsync()
    {
        var plan = rosterFixture ?? throw new InvalidOperationException("Roster fixture was not imported.");
        var expected = plan.Players.Where(row => row.OrganizationId == OrganizationIds.Club && row.TeamType == "First Team" &&
            row.HasExplicitPickerFields && row.IsActive && !row.IsArchived && !row.IsTransferred).OrderBy(row => row.Number).ThenBy(row => row.Id, StringComparer.Ordinal).Take(50).ToArray();
        using var anonymous = Client();
        await GetAsync(anonymous, "/api/mobile/players-list", 401);
        foreach (var role in new[] { MobileRole.Fan, MobileRole.Parent, MobileRole.Coach, MobileRole.Player, MobileRole.Management })
        {
            using var client = Client();
            var phone = database.NewPhone();
            // No organization grants: this picker is role-independent public
            // roster information behind authentication, not operational scope.
            Guid? admin = role == MobileRole.Management ? await database.SeedAdminAsync() : null;
            var identity = await database.SeedIdentityAsync(phone, role, adminId: admin);
            var login = await LoginAsync(client, phone, role.ToWireValue());
            var body = await GetAsync(client, "/api/mobile/players-list?organization_id=lefteria_fc_academy&role=management&limit=9999", 200);
            var players = body.GetProperty("players").EnumerateArray().ToArray();
            Check.Equal(50, players.Length, "Picker limit/status filters differ.");
            for (var index = 0; index < players.Length; index++)
            {
                var item = players[index];
                Check.Equal(expected[index].Id, item.GetProperty("id").GetString(), "Picker did not use number plus ordinal ID ordering.");
                Check.Equal(expected[index].Number, item.GetProperty("number").GetInt32(), "Picker changed the jersey number.");
                Check.True(item.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(new[] { "id", "image_url", "name", "number", "position" }), "Picker exposed fields outside its safe projection.");
            }
            Check.True(!body.GetRawText().Contains("fixture-private@example.invalid", StringComparison.Ordinal) &&
                !body.GetRawText().Contains("owner-only-fixture-marker", StringComparison.Ordinal), "Picker exposed raw archive data.");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SignedToken(identity, phone,
                IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "mobile_access", role == MobileRole.Fan ? "parent" : "fan", false));
            using (var mismatched = await client.GetAsync("/api/mobile/players-list"))
                Check.True((int)mismatched.StatusCode is 401 or 403, "Picker accepted mismatched signed identity role.");
            client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("token").GetString());
            if (admin is { } adminId) await database.SetAdminActiveAsync(adminId, false);
            else await database.SetIdentityActiveAsync(identity, false);
            using var revoked = await client.GetAsync("/api/mobile/players-list");
            Check.True((int)revoked.StatusCode is 401 or 403, "Picker reused revoked identity/admin state.");
        }
        using var raceClient = Client();
        var racePhone = database.NewPhone(); var raceIdentity = await database.SeedIdentityAsync(racePhone, MobileRole.Fan);
        await LoginAsync(raceClient, racePhone, "fan");
        var gate = factory.RosterGate.HoldNext();
        var pending = raceClient.GetAsync("/api/mobile/players-list");
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await database.SetIdentityActiveAsync(raceIdentity, false); }
        finally { gate.Release.TrySetResult(); }
        using var race = await pending;
        Check.True((int)race.StatusCode is 401 or 403, "Final roster query trusted identity state cached before revocation.");
    }

    private async Task RosterIntegrityAsync()
    {
        var plan = rosterFixture ?? throw new InvalidOperationException("Roster fixture was not imported.");
        await database.AssertRosterRuntimePermissionsAsync();
        var runtimeRefused = false;
        try { await database.ApplyRosterAsRuntimeAsync(plan); }
        catch (LefteriaFc.Application.Roster.RosterImportApplyException exception) when (exception.Code == "owner_target_required") { runtimeRefused = true; }
        Check.True(runtimeRefused, "Runtime credentials could operate the owner importer.");
        var clubPlayer = plan.Players.First(row => row.OrganizationId == OrganizationIds.Club);
        var group = plan.Groups.Single(); var team = plan.Teams.Single();
        await database.AssertRejectedSqlAsync("INSERT INTO club.player_academy_groups (organization_id,player_id,academy_group_id) VALUES (@org,@player,@group)",
            PostgresErrorCodes.ForeignKeyViolation, new("org", OrganizationIds.Academy), new("player", clubPlayer.Id), new("group", group.Id));
        await database.AssertRejectedSqlAsync("""
            INSERT INTO club.players (id,organization_id,name,number,position,team_type,team_id,is_active,is_archived,is_transferred,has_explicit_picker_fields)
            VALUES (@id,@org,'Cross organization fixture',1,'Forward','Academy',@team,true,false,false,true)
            """, PostgresErrorCodes.ForeignKeyViolation, new("id", "cross-org-" + Guid.NewGuid().ToString("N")),
            new("org", OrganizationIds.Academy), new("team", team.Id));
        await using var context = database.CreateContext();
        Check.Equal(plan.MembershipCount, await context.RosterPlayerAcademyGroups.CountAsync(), "Rejected cross-org links changed membership rows.");
        Check.Equal(plan.Players.Count, await context.RosterPlayers.CountAsync(), "Rejected cross-org player changed canonical rows.");
    }
}
