using System.Text.Json;
using Npgsql;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.DependencyInjection;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string FcPlayersPath="/api/admin/football/players?organization_id=lefteria_fc";
    private const string AcademyPlayersPath="/api/admin/football/players?organization_id=lefteria_fc_academy";

    private static JsonElement PlayerRow(JsonElement body,string id)
    {
        var row=body.GetProperty("players").EnumerateArray()
            .FirstOrDefault(p=>p.GetProperty("id").GetString()==id);
        Require(row.ValueKind==JsonValueKind.Object,"Synthetic roster member missing from scoped picker.");
        return row;
    }

    private async Task AdminRosterPlayerDirectoryAsync()
    {
        Console.WriteLine("ROSTER_PICKER_PROBE start");
        using var anonymous=Client();
        using(var denied=await anonymous.GetAsync(FcPlayersPath))
            await ExpectJsonAsync(denied,401,"not_authenticated");
        var admin=await database.SeedAsync(organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,admin);

        using(var scope=factory.Services.CreateScope())
        await using(var connection=await scope.ServiceProvider
            .GetRequiredService<WebAdminConnections>().OpenAsync(CancellationToken.None))
        {
            var denied=false;
            try
            {
                await using var direct=new NpgsqlCommand("SELECT id,name FROM club.players",connection);
                await direct.ExecuteScalarAsync();
            }
            catch(PostgresException e) when(e.SqlState=="42501"){denied=true;}
            Require(denied,"Web-admin role has raw access to youth roster.");
            denied=false;
            try
            {
                await using var cmd=new NpgsqlCommand(
                    "SELECT club.read_admin_roster_players(repeat('0',64),'lefteria_fc',0,20)",connection);
                await cmd.ExecuteScalarAsync();
            }
            catch(PostgresException e) when(e.SqlState=="P0001"){denied=true;}
            Require(denied,"Roster picker accepted an unbound admin session digest.");
        }

        Console.WriteLine("ROSTER_PICKER_PROBE token_and_raw_grants_ok");
        var fixture=await database.SeedAudienceAsync();
        try
        {
            using(var response=await GetAsync(client,FcPlayersPath,login.Cookie))
            {
                Console.WriteLine("ROSTER_PICKER_PROBE fc_status="+(int)response.StatusCode);
                var body=await ExpectJsonAsync(response,200);
                Console.WriteLine("ROSTER_PICKER_PROBE fc_parsed player_count="+body.GetProperty("players").GetArrayLength());

                var first=PlayerRow(body,fixture.Player(1));
                Console.WriteLine("ROSTER_PICKER_PROBE fc_first_found fields="+first.EnumerateObject().Count());

                Require(body.GetProperty("organization_id").GetString()=="lefteria_fc" &&
                    body.GetProperty("ready").GetBoolean() &&
                    body.GetProperty("offset").GetInt32()==0 &&
                    body.GetProperty("limit").GetInt32()==20 &&
                    body.GetProperty("total").GetInt32()>=2 &&
                    first.EnumerateObject().Count()==4 &&
                    first.GetProperty("team_id").GetString()==fixture.Team1 &&
                    first.GetProperty("academy_group_ids").GetArrayLength()==0 &&
                    PlayerRow(body,fixture.Player(3)).GetProperty("team_id").ValueKind==JsonValueKind.Null,
                    "FC player membership directory differs from real canonical roster.");
                Console.WriteLine("ROSTER_PICKER_PROBE fc_assignment_checks_ok");
                Require(!body.GetRawText().Contains(fixture.Player(2),StringComparison.Ordinal) &&
                    !body.GetRawText().Contains(fixture.Player(4),StringComparison.Ordinal) &&
                    !body.GetRawText().Contains("phone",StringComparison.OrdinalIgnoreCase) &&
                    !body.GetRawText().Contains("birth",StringComparison.OrdinalIgnoreCase),
                    "Inactive, cross-owner or private youth data leaked.");
            }
            Console.WriteLine("ROSTER_PICKER_PROBE fc_membership_ok");
            using(var response=await GetAsync(client,AcademyPlayersPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(response,200);
                var groups=PlayerRow(body,fixture.Player(4)).GetProperty("academy_group_ids");
                Require(body.GetProperty("organization_id").GetString()=="lefteria_fc_academy" &&
                    body.GetProperty("total").GetInt32()>=3 &&
                    groups.GetArrayLength()==2 &&
                    groups[0].GetString()==fixture.Group1 &&
                    groups[1].GetString()==fixture.Group2 &&
                    !body.GetRawText().Contains(fixture.Player(5),StringComparison.Ordinal) &&
                    !body.GetRawText().Contains(fixture.Player(1),StringComparison.Ordinal),
                    "Academy primary and plural membership union is wrong or leaked FC.");
            }
            Console.WriteLine("ROSTER_PICKER_PROBE academy_membership_ok");
            using(var response=await GetAsync(client,FcPlayersPath+"&offset=1&limit=1",login.Cookie))
            {
                var body=await ExpectJsonAsync(response,200);
                Require(body.GetProperty("offset").GetInt32()==1 &&
                    body.GetProperty("limit").GetInt32()==1 &&
                    body.GetProperty("players").GetArrayLength()==1,
                    "Manager roster player pagination failed.");
            }
            Console.WriteLine("ROSTER_PICKER_PROBE pagination_ok");
            await database.RemoveAudienceMembershipAsync(fixture);
            using(var response=await GetAsync(client,AcademyPlayersPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(response,200);
                Require(PlayerRow(body,fixture.Player(6)).GetProperty("academy_group_ids").GetArrayLength()==0,
                    "Player group display did not dynamically refresh from database.");
            }
            Console.WriteLine("ROSTER_PICKER_PROBE live_mutation_reflection_ok");
            foreach(var path in new[]{
                "/api/admin/football/players",
                "/api/admin/football/players?organization_id=all",
                FcPlayersPath+"&offset=-1",FcPlayersPath+"&offset=20001",
                FcPlayersPath+"&limit=0",FcPlayersPath+"&limit=51",
                FcPlayersPath+"&limit=2&limit=3",
                FcPlayersPath+"&organization_id=lefteria_fc_academy"})
                using(var response=await GetAsync(client,path,login.Cookie))
                    await ExpectStatusAsync(response,400);

            Console.WriteLine("ROSTER_PICKER_PROBE invalid_filters_ok");
            var editor=await database.SeedAsync(role:"editor",organizations:["lefteria_fc"]);
            using(var editorClient=Client())
            {
                var session=await LoginAsync(editorClient,editor);
                using var denied=await GetAsync(editorClient,FcPlayersPath,session.Cookie);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            var coach=await database.SeedAsync(role:"coach",organizations:["lefteria_fc_academy"]);
            using(var coachClient=Client())
            {
                var session=await LoginAsync(coachClient,coach);
                using var denied=await GetAsync(coachClient,AcademyPlayersPath,session.Cookie);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            var academyOnly=await database.SeedAsync(organizations:["lefteria_fc_academy"]);
            using(var academyClient=Client())
            {
                var session=await LoginAsync(academyClient,academyOnly);
                using(var denied=await GetAsync(academyClient,FcPlayersPath,session.Cookie))
                    await ExpectJsonAsync(denied,403,"not_authorized");
                using(var allowed=await GetAsync(academyClient,AcademyPlayersPath,session.Cookie))
                    await ExpectJsonAsync(allowed,200);
            }
            Console.WriteLine("ROSTER_PICKER_PROBE role_denial_ok");
            await database.SetStateAsync(admin,role:"editor");
            using(var denied=await GetAsync(client,FcPlayersPath,login.Cookie))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.SetStateAsync(admin,role:"manager");
            await database.RevokeOrganizationAsync(admin,"lefteria_fc");
            using(var denied=await GetAsync(client,FcPlayersPath,login.Cookie))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.ExpireSessionAsync(admin,login.Token);
            using(var denied=await GetAsync(client,AcademyPlayersPath,login.Cookie))
                await ExpectJsonAsync(denied,401,"not_authenticated");
        }
        finally {await database.CleanupAudienceAsync(fixture);}
        Console.WriteLine("ROSTER_PICKER_PROBE all_phases_ok");
    }
}
