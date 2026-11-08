using System.Net.Http.Json;
using System.Text.Json;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string FcCoachAssignmentPath="/api/admin/football/coach-assignments?organization_id=lefteria_fc";
    private const string AcademyCoachAssignmentPath="/api/admin/football/coach-assignments?organization_id=lefteria_fc_academy";

    private static async Task<HttpResponseMessage> PutCoach(
        HttpClient client,string cookie,string path,string staff,
        string[] expected,string[] desired)
    {
        using var request=Mutation(path,JsonContent.Create(new {
            staff_id=staff,expected_group_ids=expected,group_ids=desired
        }));
        request.Method=HttpMethod.Put;
        if(!string.IsNullOrEmpty(cookie))request.Headers.Add("Cookie",cookie);
        return await client.SendAsync(request);
    }

    private async Task AdminCoachAssignmentsAsync()
    {
        Console.WriteLine("CHECK coach assignment stage: anonymous");
        using(var anonymous=Client())
        using(var denied=await PutCoach(anonymous,"",FcCoachAssignmentPath,"unknown",[],[]))
            await ExpectJsonAsync(denied,401,"not_authenticated");
        var admin=await database.SeedAsync(organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,admin);
        var roster=await database.SeedAudienceAsync();
        WebAdminDatabaseFixture.CoachFixture? coach=null;
        try
        {
            coach=await database.SeedCoachesAsync(roster);
            Console.WriteLine("CHECK coach assignment stage: first-team add");
            using(var changed=await PutCoach(client,login.Cookie,FcCoachAssignmentPath,
                coach.Coach1,[roster.Team1],[roster.Team1,roster.Team2]))
            {
                var body=await ExpectJsonAsync(changed,200);
                Require(body.GetProperty("organization_id").GetString()=="lefteria_fc" &&
                    body.GetProperty("staff_id").GetString()==coach.Coach1 &&
                    body.GetProperty("group_ids").GetArrayLength()==2,
                    "First-team coach add did not persist verified membership.");
            }
            using(var directory=await GetAsync(client,FcCoachesPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(directory,200);
                Require(body.GetProperty("coaches").EnumerateArray().Any(c=>
                    c.GetProperty("id").GetString()==coach.Coach1 &&
                    c.GetProperty("group_ids").GetArrayLength()==2),
                    "Coach directory did not refresh from canonical FC memberships.");
            }
            Console.WriteLine("CHECK coach assignment stage: stale conflict");
            using(var stale=await PutCoach(client,login.Cookie,FcCoachAssignmentPath,
                coach.Coach1,[roster.Team1],[]))
                await ExpectJsonAsync(stale,409,"coach_assignment_conflict");
            using(var restored=await PutCoach(client,login.Cookie,FcCoachAssignmentPath,
                coach.Coach1,[roster.Team1,roster.Team2],[roster.Team1]))
                await ExpectJsonAsync(restored,200);

            Console.WriteLine("CHECK coach assignment stage: designated protected");
            using(var designated=await PutCoach(client,login.Cookie,AcademyCoachAssignmentPath,
                coach.Coach1,[roster.Group1,roster.Group2],[roster.Group1]))
                await ExpectJsonAsync(designated,409,"coach_assignment_conflict");

            Console.WriteLine("CHECK coach assignment stage: Academy remove");
            using(var changed=await PutCoach(client,login.Cookie,AcademyCoachAssignmentPath,
                coach.Coach1,[roster.Group1,roster.Group2],[roster.Group2]))
                await ExpectJsonAsync(changed,200);
            using(var directory=await GetAsync(client,AcademyCoachesPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(directory,200);
                Require(body.GetProperty("coaches").EnumerateArray().Any(c=>
                    c.GetProperty("id").GetString()==coach.Coach1 &&
                    c.GetProperty("group_ids").GetArrayLength()==1 &&
                    c.GetProperty("group_ids")[0].GetString()==roster.Group2),
                    "Academy coach group removal failed actual owner projection.");
            }
            using(var restored=await PutCoach(client,login.Cookie,AcademyCoachAssignmentPath,
                coach.Coach1,[roster.Group2],[roster.Group1,roster.Group2]))
                await ExpectJsonAsync(restored,200);

            using(var invalid=await PutCoach(client,login.Cookie,FcCoachAssignmentPath,
                coach.Coach2,[],[roster.Team1]))
                await ExpectJsonAsync(invalid,422,"invalid_coach_assignment");
            using(var invalid=await PutCoach(client,login.Cookie,FcCoachAssignmentPath,
                coach.Coach1,[roster.Team1],[roster.Group1]))
                await ExpectJsonAsync(invalid,422,"invalid_coach_assignment");

            Console.WriteLine("CHECK coach assignment stage: permissions");
            var actorCoach=await database.SeedAsync(role:"coach",organizations:["lefteria_fc_academy"]);
            using(var actorClient=Client())
            {
                var actorLogin=await LoginAsync(actorClient,actorCoach);
                using var denied=await PutCoach(actorClient,actorLogin.Cookie,
                    AcademyCoachAssignmentPath,coach.Coach1,
                    [roster.Group1,roster.Group2],[]);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            var academyOnly=await database.SeedAsync(organizations:["lefteria_fc_academy"]);
            using(var other=Client())
            {
                var otherLogin=await LoginAsync(other,academyOnly);
                using var denied=await PutCoach(other,otherLogin.Cookie,
                    FcCoachAssignmentPath,coach.Coach1,[roster.Team1],[]);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            using(var scope=factory.Services.CreateScope())
            await using(var con=await scope.ServiceProvider
                .GetRequiredService<WebAdminConnections>().OpenAsync(CancellationToken.None))
            {
                var denied=false;
                try {
                    await using var cmd=new NpgsqlCommand(
                        "DELETE FROM club.staff_teams WHERE false",con);
                    await cmd.ExecuteNonQueryAsync();
                }catch(PostgresException e) when(e.SqlState=="42501"){denied=true;}
                Require(denied,"Admin auth obtained raw coach team deletion authority.");
            }
            Console.WriteLine("CHECK coach assignment stage: revocations");
            await database.SetStateAsync(admin,role:"editor");
            using(var denied=await PutCoach(client,login.Cookie,FcCoachAssignmentPath,
                coach.Coach1,[roster.Team1],[]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.SetStateAsync(admin,role:"manager");
            await database.RevokeOrganizationAsync(admin,"lefteria_fc");
            using(var denied=await PutCoach(client,login.Cookie,FcCoachAssignmentPath,
                coach.Coach1,[roster.Team1],[]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.ExpireSessionAsync(admin,login.Token);
            using(var denied=await PutCoach(client,login.Cookie,AcademyCoachAssignmentPath,
                coach.Coach1,[roster.Group1,roster.Group2],[]))
                await ExpectJsonAsync(denied,401,"not_authenticated");
        }
        finally
        {
            if(coach is not null)await database.CleanupCoachesAsync(coach);
            await database.CleanupAudienceAsync(roster);
        }
    }
}
