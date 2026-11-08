using System.Text.Json;
using Npgsql;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.DependencyInjection;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string FcPlayersPath="/api/admin/football/players?organization_id=lefteria_fc";
    private const string AcademyPlayersPath="/api/admin/football/players?organization_id=lefteria_fc_academy";

    // Test fixtures can coexist with other valid roster rows. Never assume
    // a synthetic player appears on page one just because paging is correct.
    private static JsonElement PlayerRow(IReadOnlyList<JsonElement> rows,string id)
    {
        var row=rows.FirstOrDefault(p=>p.GetProperty("id").GetString()==id);
        Require(row.ValueKind==JsonValueKind.Object,"Synthetic player missing from scoped pages.");
        return row;
    }

    private async Task<(JsonElement First,IReadOnlyList<JsonElement> Rows)> ReadRosterPagesAsync(
        HttpClient client,string path,string cookie)
    {
        const int pageSize=50;
        var rows=new List<JsonElement>();
        JsonElement first=default;
        var total=0;
        for(var offset=0;offset<=20000;offset+=pageSize)
        {
            using var response=await GetAsync(client,path+"&offset="+offset+"&limit="+pageSize,cookie);
            var page=await ExpectJsonAsync(response,200);
            if(offset==0){first=page.Clone();total=page.GetProperty("total").GetInt32();}
            Require(page.GetProperty("offset").GetInt32()==offset &&
                page.GetProperty("limit").GetInt32()==pageSize &&
                page.GetProperty("total").GetInt32()==total,
                "Owner roster pagination did not preserve its metadata.");
            rows.AddRange(page.GetProperty("players").EnumerateArray().Select(p=>p.Clone()));
            if(offset+pageSize>=total)break;
            Require(offset<1000,"Disposable roster unexpectedly exceeds bounded test scan.");
        }
        Require(rows.Count==total,"Private player pages returned incomplete or duplicated rows.");
        return (first,rows);
    }

    private async Task AdminRosterPlayerDirectoryAsync()
    {
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

        var fixture=await database.SeedAudienceAsync();
        try
        {
            var fc=await ReadRosterPagesAsync(client,FcPlayersPath,login.Cookie);
            var fcBody=fc.First;
            var first=PlayerRow(fc.Rows,fixture.Player(1));
            Require(fcBody.GetProperty("organization_id").GetString()=="lefteria_fc" &&
                fcBody.GetProperty("ready").GetBoolean() &&
                fcBody.GetProperty("offset").GetInt32()==0 &&
                fcBody.GetProperty("limit").GetInt32()==50 &&
                fcBody.GetProperty("total").GetInt32()>=2 &&
                first.EnumerateObject().Count()==4 &&
                first.GetProperty("team_id").GetString()==fixture.Team1 &&
                first.GetProperty("academy_group_ids").GetArrayLength()==0 &&
                PlayerRow(fc.Rows,fixture.Player(3))
                    .GetProperty("team_id").ValueKind==JsonValueKind.Null,
                "FC player membership directory differs from real canonical roster.");
            Require(!fc.Rows.Any(p => p.GetProperty("id").GetString()==fixture.Player(2) ||
                    p.GetProperty("id").GetString()==fixture.Player(4)) &&
                fc.Rows.All(p=>p.EnumerateObject().Count()==4) &&
                !fcBody.GetRawText().Contains("phone",StringComparison.OrdinalIgnoreCase) &&
                !fcBody.GetRawText().Contains("birth",StringComparison.OrdinalIgnoreCase),
                "Inactive, cross-owner or private youth data leaked.");

            var academy=await ReadRosterPagesAsync(client,AcademyPlayersPath,login.Cookie);
            var academyGroups=PlayerRow(academy.Rows,fixture.Player(4))
                .GetProperty("academy_group_ids");
            Require(academy.First.GetProperty("organization_id").GetString()=="lefteria_fc_academy" &&
                academy.First.GetProperty("total").GetInt32()>=3 &&
                academyGroups.GetArrayLength()==2 &&
                academyGroups[0].GetString()==fixture.Group1 &&
                academyGroups[1].GetString()==fixture.Group2 &&
                !academy.Rows.Any(p=>p.GetProperty("id").GetString()==fixture.Player(5) ||
                    p.GetProperty("id").GetString()==fixture.Player(1)),
                "Academy primary/plural memberships or tenant boundary invalid.");

            using(var response=await GetAsync(client,FcPlayersPath+"&offset=1&limit=1",login.Cookie))
            {
                var body=await ExpectJsonAsync(response,200);
                Require(body.GetProperty("offset").GetInt32()==1 &&
                    body.GetProperty("limit").GetInt32()==1 &&
                    body.GetProperty("players").GetArrayLength()==1,
                    "Manager roster player pagination failed.");
            }
            await database.RemoveAudienceMembershipAsync(fixture);
            var updated=await ReadRosterPagesAsync(client,AcademyPlayersPath,login.Cookie);
            Require(PlayerRow(updated.Rows,fixture.Player(6))
                .GetProperty("academy_group_ids").GetArrayLength()==0,
                "Player group display did not dynamically refresh from database.");
            foreach(var path in new[]{
                "/api/admin/football/players",
                "/api/admin/football/players?organization_id=all",
                FcPlayersPath+"&offset=-1",FcPlayersPath+"&offset=20001",
                FcPlayersPath+"&limit=0",FcPlayersPath+"&limit=51",
                FcPlayersPath+"&limit=2&limit=3",
                FcPlayersPath+"&organization_id=lefteria_fc_academy"})
                using(var response=await GetAsync(client,path,login.Cookie))
                    await ExpectStatusAsync(response,400);

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
    }
}
