using System.Net.Http.Json;
using System.Text.Json;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string FcAssignmentPath="/api/admin/football/assignments?organization_id=lefteria_fc";
    private const string AcademyAssignmentPath="/api/admin/football/assignments?organization_id=lefteria_fc_academy";

    private static async Task<HttpResponseMessage> PutAssignment(
        HttpClient client,string cookie,string path,string player,
        string[] expected,string[] desired)
    {
        using var request=Mutation(path,JsonContent.Create(new {
            player_id=player,expected_group_ids=expected,group_ids=desired
        }));
        request.Method=HttpMethod.Put;
        request.Headers.Add("Cookie",cookie);
        return await client.SendAsync(request);
    }

    private async Task AdminRosterAssignmentsAsync()
    {
        using(var anonymous=Client())
        using(var denied=await PutAssignment(anonymous,"",FcAssignmentPath,
            "no-such-player",[],[]))
            await ExpectJsonAsync(denied,401,"not_authenticated");

        var admin=await database.SeedAsync(organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,admin);
        var fixture=await database.SeedAudienceAsync();
        try
        {
            using(var result=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                fixture.Player(3),[],[fixture.Team2]))
            {
                var body=await ExpectJsonAsync(result,200);
                Require(body.GetProperty("player_id").GetString()==fixture.Player(3) &&
                    body.GetProperty("team_id").GetString()==fixture.Team2 &&
                    body.GetProperty("academy_group_ids").GetArrayLength()==0,
                    "First-team assignment did not return verified canonical membership.");
            }
            var fc=await ReadRosterPagesAsync(client,FcPlayersPath,login.Cookie);
            Require(PlayerRow(fc.Rows,fixture.Player(3)).GetProperty("team_id").GetString()==fixture.Team2,
                "Assignment was not reflected in live player directory.");
            using(var stale=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                fixture.Player(3),[],[fixture.Team1]))
                await ExpectJsonAsync(stale,409,"assignment_conflict");
            using(var denied=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                fixture.Player(3),[fixture.Team2],[fixture.Group1]))
                await ExpectJsonAsync(denied,422,"invalid_assignment");
            using(var restore=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                fixture.Player(3),[fixture.Team2],[]))
                await ExpectJsonAsync(restore,200);

            using(var academy=await PutAssignment(client,login.Cookie,AcademyAssignmentPath,
                fixture.Player(4),[fixture.Group1,fixture.Group2],[fixture.Group2]))
            {
                var body=await ExpectJsonAsync(academy,200);
                Require(body.GetProperty("team_id").ValueKind==JsonValueKind.Null &&
                    body.GetProperty("academy_group_ids").GetArrayLength()==1 &&
                    body.GetProperty("academy_group_ids")[0].GetString()==fixture.Group2,
                    "Academy group removal did not retain correct canonical membership.");
            }
            var updated=await ReadRosterPagesAsync(client,AcademyPlayersPath,login.Cookie);
            Require(PlayerRow(updated.Rows,fixture.Player(4))
                .GetProperty("academy_group_ids").GetArrayLength()==1,
                "Academy membership change did not refresh live roster.");
            using(var restore=await PutAssignment(client,login.Cookie,AcademyAssignmentPath,
                fixture.Player(4),[fixture.Group2],[fixture.Group1,fixture.Group2]))
                await ExpectJsonAsync(restore,200);

            Require(await database.AssignmentAuditCountAsync(admin)==4,
                "Each successful membership replacement must produce an audit record.");
            await database.WithAssignmentAuditFailureAsync(admin,async()=>{
                using var denied=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                    fixture.Player(3),[],[fixture.Team1]);
                await ExpectJsonAsync(denied,503,"assignment_unavailable");
            });
            var rollback=await ReadRosterPagesAsync(client,FcPlayersPath,login.Cookie);
            Require(PlayerRow(rollback.Rows,fixture.Player(3))
                    .GetProperty("team_id").ValueKind==JsonValueKind.Null &&
                    await database.AssignmentAuditCountAsync(admin)==4,
                "Audit failure did not roll back canonical player assignment.");

            using(var invalid=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                fixture.Player(2),[fixture.Team1],[]))
                await ExpectJsonAsync(invalid,422,"invalid_assignment");
            using(var invalid=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                fixture.Player(4),[],[fixture.Team2]))
                await ExpectJsonAsync(invalid,422,"invalid_assignment");
            using(var invalid=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                fixture.Player(3),[],["not_existing_group"]))
                await ExpectJsonAsync(invalid,422,"invalid_assignment");

            var coach=await database.SeedAsync(role:"coach",organizations:["lefteria_fc_academy"]);
            using(var coachClient=Client())
            {
                var coachLogin=await LoginAsync(coachClient,coach);
                using var denied=await PutAssignment(coachClient,coachLogin.Cookie,
                    AcademyAssignmentPath,fixture.Player(4),
                    [fixture.Group1,fixture.Group2],[]);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            var other=await database.SeedAsync(organizations:["lefteria_fc_academy"]);
            using(var otherClient=Client())
            {
                var otherLogin=await LoginAsync(otherClient,other);
                using var denied=await PutAssignment(otherClient,otherLogin.Cookie,
                    FcAssignmentPath,fixture.Player(3),[],[fixture.Team1]);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            using(var scope=factory.Services.CreateScope())
            await using(var connection=await scope.ServiceProvider
                .GetRequiredService<WebAdminConnections>().OpenAsync(CancellationToken.None))
            {
                var denied=false;
                try
                {
                    await using var sql=new NpgsqlCommand(
                        "UPDATE club.players SET team_id=NULL WHERE false",connection);
                    await sql.ExecuteNonQueryAsync();
                }
                catch(PostgresException e) when(e.SqlState=="42501"){denied=true;}
                Require(denied,"Admin-auth role unexpectedly obtained raw player UPDATE.");
            }
            await database.SetStateAsync(admin,role:"coach");
            using(var denied=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                fixture.Player(3),[],[fixture.Team1]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.SetStateAsync(admin,role:"manager");
            await database.RevokeOrganizationAsync(admin,"lefteria_fc");
            using(var denied=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                fixture.Player(3),[],[fixture.Team1]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.ExpireSessionAsync(admin,login.Token);
            using(var denied=await PutAssignment(client,login.Cookie,AcademyAssignmentPath,
                fixture.Player(4),[fixture.Group1,fixture.Group2],[]))
                await ExpectJsonAsync(denied,401,"not_authenticated");
        }
        finally {await database.CleanupAudienceAsync(fixture);}
    }
}
