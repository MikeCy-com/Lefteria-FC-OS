using System.Text.Json;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string FcCoachesPath="/api/admin/football/coaches?organization_id=lefteria_fc";
    private const string AcademyCoachesPath="/api/admin/football/coaches?organization_id=lefteria_fc_academy";

    private async Task AdminCoachDirectoryAsync()
    {
        using(var anonymous=Client())
        using(var denied=await anonymous.GetAsync(FcCoachesPath))
            await ExpectJsonAsync(denied,401,"not_authenticated");
        var admin=await database.SeedAsync(organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,admin);
        var roster=await database.SeedAudienceAsync();
        WebAdminDatabaseFixture.CoachFixture? coaches=null;
        try
        {
            coaches=await database.SeedCoachesAsync(roster);
            using(var fc=await GetAsync(client,FcCoachesPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(fc,200);
                Require(body.GetProperty("organization_id").GetString()=="lefteria_fc" &&
                    body.GetProperty("ready").GetBoolean() &&
                    body.GetProperty("total").GetInt32()>=1 &&
                    body.GetProperty("coaches").EnumerateArray().Any(c =>
                        c.GetProperty("id").GetString()==coaches.Coach1 &&
                        c.GetProperty("group_ids").GetArrayLength()==1 &&
                        c.GetProperty("group_ids")[0].GetString()==roster.Team1) &&
                    !body.GetRawText().Contains(roster.Group1,StringComparison.Ordinal) &&
                    !body.GetRawText().Contains(roster.Group2,StringComparison.Ordinal) &&
                    !body.GetRawText().Contains(coaches.Coach2,StringComparison.Ordinal) &&
                    !body.GetRawText().Contains("phone",StringComparison.OrdinalIgnoreCase),
                    "FC coach directory leaked Academy membership or private details.");
            }
            using(var academy=await GetAsync(client,AcademyCoachesPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(academy,200);
                Require(body.GetProperty("organization_id").GetString()=="lefteria_fc_academy" &&
                    body.GetProperty("coaches").EnumerateArray().Any(c =>
                        c.GetProperty("id").GetString()==coaches.Coach1 &&
                        c.GetProperty("group_ids").GetArrayLength()==2 &&
                        c.GetProperty("group_ids")[0].GetString()==roster.Group1 &&
                        c.GetProperty("group_ids")[1].GetString()==roster.Group2) &&
                    !body.GetRawText().Contains(roster.Team1,StringComparison.Ordinal),
                    "Academy coach directory failed to combine plural and primary coach group links.");
            }
            foreach(var path in new[]{
                "/api/admin/football/coaches",
                "/api/admin/football/coaches?organization_id=all",
                FcCoachesPath+"&offset=-1",FcCoachesPath+"&limit=51",
                FcCoachesPath+"&limit=0",FcCoachesPath+"&limit=2&limit=3",
                FcCoachesPath+"&organization_id=lefteria_fc_academy"})
                using(var denied=await GetAsync(client,path,login.Cookie))
                    await ExpectStatusAsync(denied,400);
            using(var scope=factory.Services.CreateScope())
            await using(var con=await scope.ServiceProvider
                .GetRequiredService<WebAdminConnections>().OpenAsync(CancellationToken.None))
            {
                var denied=false;
                try{
                    await using var cmd=new NpgsqlCommand("SELECT id,name FROM club.staff",con);
                    await cmd.ExecuteScalarAsync();
                }catch(PostgresException e) when(e.SqlState=="42501"){denied=true;}
                Require(denied,"Admin-auth role unexpectedly reads raw staff identities.");
            }
            var coachActor=await database.SeedAsync(role:"coach",organizations:["lefteria_fc_academy"]);
            using(var coachClient=Client())
            {
                var loginCoach=await LoginAsync(coachClient,coachActor);
                using var denied=await GetAsync(coachClient,AcademyCoachesPath,loginCoach.Cookie);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            var academyOnly=await database.SeedAsync(organizations:["lefteria_fc_academy"]);
            using(var other=Client())
            {
                var otherLogin=await LoginAsync(other,academyOnly);
                using var denied=await GetAsync(other,FcCoachesPath,otherLogin.Cookie);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            await database.SetStateAsync(admin,role:"editor");
            using(var denied=await GetAsync(client,FcCoachesPath,login.Cookie))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.SetStateAsync(admin,role:"manager");
            await database.RevokeOrganizationAsync(admin,"lefteria_fc");
            using(var denied=await GetAsync(client,FcCoachesPath,login.Cookie))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.ExpireSessionAsync(admin,login.Token);
            using(var denied=await GetAsync(client,AcademyCoachesPath,login.Cookie))
                await ExpectJsonAsync(denied,401,"not_authenticated");
        }
        finally
        {
            if(coaches is not null)await database.CleanupCoachesAsync(coaches);
            await database.CleanupAudienceAsync(roster);
        }
    }
}
