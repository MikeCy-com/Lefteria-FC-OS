using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string CreateFcGroupPath="/api/admin/football/groups?organization_id=lefteria_fc";
    private const string CreateAcademyGroupPath="/api/admin/football/groups?organization_id=lefteria_fc_academy";

    private static async Task<HttpResponseMessage> AddGroup(
        HttpClient client,string cookie,string path,object body)
    {
        using var request=Mutation(path,JsonContent.Create(body));
        request.Headers.Add("Cookie",cookie);
        return await client.SendAsync(request);
    }

    private async Task AdminGroupCreateAsync()
    {
        using var anonymous=Client();
        using(var request=Mutation(CreateFcGroupPath,JsonContent.Create(new{name="Forbidden"})))
        using(var denied=await anonymous.SendAsync(request))
            await ExpectJsonAsync(denied,401,"not_authenticated");

        var admin=await database.SeedAsync(organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,admin);
        var fixture=await database.SeedAudienceAsync();
        string? fcId=null,academyId=null;
        try
        {
            using(var response=await AddGroup(client,login.Cookie,CreateFcGroupPath,new{name="New FC team"}))
            {
                var created=await ExpectJsonAsync(response,200);
                fcId=created.GetProperty("id").GetString();
                Require(fcId is not null && fcId.StartsWith("grp_",StringComparison.Ordinal) &&
                    created.EnumerateObject().Count()==4 &&
                    created.GetProperty("kind").GetString()=="team" &&
                    created.GetProperty("name").GetString()=="New FC team" &&
                    created.GetProperty("active_players").GetInt32()==0,
                    "New first-team group failed atomic create contract.");
            }
            using(var response=await AddGroup(client,login.Cookie,CreateAcademyGroupPath,new{name="New Academy U10"}))
            {
                var created=await ExpectJsonAsync(response,200);
                academyId=created.GetProperty("id").GetString();
                Require(academyId is not null && academyId.StartsWith("grp_",StringComparison.Ordinal) &&
                    academyId!=fcId && created.GetProperty("kind").GetString()=="academy_group" &&
                    created.GetProperty("active_players").GetInt32()==0,
                    "New Academy group is not separately owner-scoped.");
            }
            Require(await database.SyntheticGroupCountAsync("lefteria_fc","New FC team")==1 &&
                await database.SyntheticGroupCountAsync("lefteria_fc_academy","New Academy U10")==1,
                "New groups missing from real canonical tables.");
            Require(await database.GroupCreationAuditCountAsync(admin)==2,
                "One audit per successful group create required.");

            using(var fc=await GetAsync(client,FcAudiencePath,login.Cookie))
            {
                var directory=await ExpectJsonAsync(fc,200);
                Require(GetGroup(directory,fcId!).GetProperty("active_players").GetInt32()==0 &&
                    !directory.GetRawText().Contains("New Academy U10",StringComparison.Ordinal),
                    "Created FC team did not refresh the live owner-scoped directory.");
            }
            using(var academy=await GetAsync(client,AcademyAudiencePath,login.Cookie))
            {
                var directory=await ExpectJsonAsync(academy,200);
                Require(GetGroup(directory,academyId!).GetProperty("active_players").GetInt32()==0 &&
                    !directory.GetRawText().Contains("New FC team",StringComparison.Ordinal),
                    "Created Academy group escaped its roster owner boundary.");
            }

            await database.WithGroupAuditFailureAsync(admin,async()=>{
                using var denied=await AddGroup(client,login.Cookie,CreateFcGroupPath,
                    new{name="Audit must rollback"});
                await ExpectJsonAsync(denied,503,"group_unavailable");
            });
            Require(await database.SyntheticGroupCountAsync("lefteria_fc","Audit must rollback")==0 &&
                await database.GroupCreationAuditCountAsync(admin)==2,
                "Failed audit left an unlogged team or group row.");

            foreach(var payload in new object[]{
                new{name=""},new{name=" Padded name "},new{name=new string('x',121)},
                new{wrong="extra"},new{name="Unknown",owner="lefteria_fc_academy"}
            })
                using(var denied=await AddGroup(client,login.Cookie,CreateFcGroupPath,payload))
                {
                    var expected=payload.GetType().GetProperty("wrong") is not null ||
                        payload.GetType().GetProperty("owner") is not null
                        ? "invalid_json":"invalid_group";
                    await ExpectJsonAsync(denied,expected=="invalid_group"?422:400,expected);
                }
            using(var malformed=Mutation(CreateFcGroupPath,new StringContent(
                "{\"name\":\"First\",\"name\":\"Second\"}",Encoding.UTF8,"application/json")))
            {
                malformed.Headers.Add("Cookie",login.Cookie);
                using var denied=await client.SendAsync(malformed);
                await ExpectJsonAsync(denied,400,"invalid_json");
            }
            using(var wrongOrigin=Mutation(CreateFcGroupPath,JsonContent.Create(new{name="Forbidden origin"})))
            {
                wrongOrigin.Headers.Remove("Origin");
                wrongOrigin.Headers.Add("Origin","https://untrusted.invalid");
                wrongOrigin.Headers.Add("Cookie",login.Cookie);
                using var denied=await client.SendAsync(wrongOrigin);
                await ExpectJsonAsync(denied,403,"request_origin_denied");
            }
            foreach(var path in new[]{
                "/api/admin/football/groups",
                "/api/admin/football/groups?organization_id=all"})
                using(var denied=await AddGroup(client,login.Cookie,path,new{name="Denied"}))
                    await ExpectJsonAsync(denied,400,"invalid_organization");

            var coach=await database.SeedAsync(role:"coach",organizations:["lefteria_fc_academy"]);
            using(var coachClient=Client())
            {
                var coachLogin=await LoginAsync(coachClient,coach);
                using var denied=await AddGroup(coachClient,coachLogin.Cookie,CreateAcademyGroupPath,
                    new{name="Coach group forbidden"});
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            var editor=await database.SeedAsync(role:"editor",organizations:["lefteria_fc"]);
            using(var editorClient=Client())
            {
                var editorLogin=await LoginAsync(editorClient,editor);
                using var denied=await AddGroup(editorClient,editorLogin.Cookie,CreateFcGroupPath,
                    new{name="Editor forbidden"});
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            var onlyAcademy=await database.SeedAsync(organizations:["lefteria_fc_academy"]);
            using(var academyClient=Client())
            {
                var academyLogin=await LoginAsync(academyClient,onlyAcademy);
                using var denied=await AddGroup(academyClient,academyLogin.Cookie,CreateFcGroupPath,
                    new{name="Cross owner group"});
                await ExpectJsonAsync(denied,403,"not_authorized");
            }

            await database.SetStateAsync(admin,role:"coach");
            using(var denied=await AddGroup(client,login.Cookie,CreateFcGroupPath,
                new{name="Downgraded group"}))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.SetStateAsync(admin,role:"manager");
            await database.RevokeOrganizationAsync(admin,"lefteria_fc");
            using(var denied=await AddGroup(client,login.Cookie,CreateFcGroupPath,
                new{name="Revoked group"}))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.ExpireSessionAsync(admin,login.Token);
            using(var denied=await AddGroup(client,login.Cookie,CreateAcademyGroupPath,
                new{name="Expired group"}))
                await ExpectJsonAsync(denied,401,"not_authenticated");
            Require(await database.GroupCreationAuditCountAsync(admin)==2,
                "Denial/expiry unexpectedly wrote a group audit.");
        }
        finally
        {
            if(fcId is not null)await database.CleanupCreatedRosterGroupAsync("lefteria_fc",fcId);
            if(academyId is not null)await database.CleanupCreatedRosterGroupAsync("lefteria_fc_academy",academyId);
            await database.CleanupAudienceAsync(fixture);
        }
    }
}
