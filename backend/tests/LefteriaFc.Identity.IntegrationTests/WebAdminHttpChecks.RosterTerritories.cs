using System.Net.Http.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private async Task AdminRosterTerritoryManagerAsync()
    {
        var manager=await database.SeedAsync(organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,manager);
        var audience=await database.SeedAudienceAsync();
        var sites=await database.SeedAnnouncementTerritoriesAsync(audience,manager);
        try
        {
            // One First Team squad and one Academy site only.
            var firstTeam=await ReadRosterPagesAsync(client,FcPlayersPath,login.Cookie);
            Require(firstTeam.Rows.Any(x=>x.GetProperty("id").GetString()==audience.Player(1)) &&
                !firstTeam.Rows.Any(x=>x.GetProperty("id").GetString()==audience.Player(3)),
                "The delegated First Team manager saw an unassigned player or lost its own assigned player.");
            var academy=await ReadRosterPagesAsync(client,AcademyPlayersPath,login.Cookie);
            Require(academy.Rows.Any(x=>x.GetProperty("id").GetString()==audience.Player(6)) &&
                !academy.Rows.Any(x=>x.GetProperty("id").GetString()==audience.Player(4)),
                "Location manager viewed the child's mixed-location Academy memberships.");

            using(var denied=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                audience.Player(1),[audience.Team1],[audience.Team2]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            using(var denied=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                audience.Player(3),[],[audience.Team1]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            using(var denied=await PutAssignment(client,login.Cookie,AcademyAssignmentPath,
                audience.Player(4),[audience.Group1,audience.Group2],[audience.Group2]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            using(var denied=await PutAssignment(client,login.Cookie,AcademyAssignmentPath,
                audience.Player(6),[audience.Group2],[audience.Group1]))
                await ExpectJsonAsync(denied,403,"not_authorized");

            await database.RevokeAnnouncementTerritoriesAsync(manager,sites);
            Require((await ReadRosterPagesAsync(client,FcPlayersPath,login.Cookie)).Rows.Count==0 &&
                (await ReadRosterPagesAsync(client,AcademyPlayersPath,login.Cookie)).Rows.Count==0,
                "A revoked territory grant still exposed a manager roster.");
            using(var denied=await PutAssignment(client,login.Cookie,AcademyAssignmentPath,
                audience.Player(6),[audience.Group2],[audience.Group2]))
                await ExpectJsonAsync(denied,403,"not_authorized");
        }
        finally
        {
            await database.CleanupAnnouncementTerritoriesAsync(manager,sites);
            await database.CleanupAudienceAsync(audience);
        }
    }

    private async Task AdminAcademyLocationGroupCreationAsync()
    {
        var manager=await database.SeedAsync(role:"manager",
            organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,manager);
        Console.WriteLine("CHECK scoped create: seed canonical roster + site");
        var roster=await database.SeedAudienceAsync();
        var site=await database.SeedAnnouncementTerritoriesAsync(roster,manager);
        string? groupId=null;
        const string path="/api/admin/football/groups/with-profile?organization_id=lefteria_fc_academy";
        const string fcPath="/api/admin/football/groups/with-profile?organization_id=lefteria_fc";

        static object Request(string name,Guid requestId,Guid? location) =>
            new {
                name,request_id=requestId,location_id=location,
                academy_profile=new {
                    age_range="U9",coach_name="",training_schedule="Tuesdays",
                    description="Disposable Academy group",max_players=25,
                    season="2026-27",banner_url="",display_order=0
                }
            };
        static async Task<HttpResponseMessage> Post(HttpClient http,string cookie,
            string target,object body)
        {
            using var request=Mutation(target,JsonContent.Create(body));
            request.Headers.Add("Cookie",cookie);
            return await http.SendAsync(request);
        }

        try
        {
            var id=Guid.NewGuid();
            var payload=Request("Fixture City One · U9",id,site.SecondLocation);
            Console.WriteLine("CHECK scoped create: sibling-site deny");
            using(var denied=await Post(client,login.Cookie,path,
                Request("Wrong sibling site",Guid.NewGuid(),site.FirstLocation)))
                await ExpectJsonAsync(denied,403,"group_creation_denied");
            Console.WriteLine("CHECK scoped create: missing location deny");
            using(var denied=await Post(client,login.Cookie,path,
                Request("Without a location",Guid.NewGuid(),null)))
                await ExpectJsonAsync(denied,422,"invalid_group_profile");
            Console.WriteLine("CHECK scoped create: FC manager deny");
            using(var denied=await Post(client,login.Cookie,fcPath,new{
                name="Unscoped FC",request_id=Guid.NewGuid(),
                team_profile=new{level="First Team",description="",banner_url=""}}))
                await ExpectJsonAsync(denied,403,"not_authorized");
            Console.WriteLine("CHECK scoped create: flat Academy deny");
            using(var denied=await Post(client,login.Cookie,
                "/api/admin/football/groups?organization_id=lefteria_fc_academy",
                new{name="Forbidden flat Academy group"}))
                await ExpectJsonAsync(denied,403,"not_authorized");

            Console.WriteLine("CHECK scoped create: no-grant outsider");
            var outsider=await database.SeedAsync(role:"manager",
                organizations:["lefteria_fc_academy"]);
            using(var outsiderClient=Client())
            {
                var other=await LoginAsync(outsiderClient,outsider);
                using var denied=await Post(outsiderClient,other.Cookie,path,
                    Request("Organization grant only",Guid.NewGuid(),site.SecondLocation));
                await ExpectJsonAsync(denied,403,"group_creation_denied");
            }

            Console.WriteLine("CHECK scoped create: authorized site commit");
            using(var saved=await Post(client,login.Cookie,path,payload))
            {
                var data=await ExpectJsonAsync(saved,200);
                groupId=data.GetProperty("id").GetString();
                Require(groupId is not null &&
                    data.GetProperty("location_id").GetGuid()==site.SecondLocation &&
                    data.GetProperty("city_id").GetGuid()==site.City &&
                    data.GetProperty("profile").GetProperty("revision").GetInt64()==1 &&
                    data.GetProperty("replay").GetBoolean()==false,
                    "Scoped Academy create omitted its verified location or profile.");
            }
            Console.WriteLine("CHECK scoped create: persisted canonical assignment");
            await database.AssertScopedAcademyGroupAsync(groupId!,site.City,site.SecondLocation);
            Console.WriteLine("CHECK scoped create: idempotent replay");
            using(var replay=await Post(client,login.Cookie,path,payload))
            {
                var data=await ExpectJsonAsync(replay,200);
                Require(data.GetProperty("id").GetString()==groupId &&
                    data.GetProperty("replay").GetBoolean(),
                    "Network replay created a second site group.");
            }
            using(var conflict=await Post(client,login.Cookie,path,
                Request("Changed retry payload",id,site.SecondLocation)))
                await ExpectJsonAsync(conflict,409,"group_creation_conflict");
            using(var duplicate=await Post(client,login.Cookie,path,
                Request("Fixture City One · U9",Guid.NewGuid(),site.SecondLocation)))
                await ExpectJsonAsync(duplicate,409,"group_creation_conflict");
            Require(await database.SyntheticGroupCountAsync(
                "lefteria_fc_academy","Fixture City One · U9")==1,
                "Replays or duplicate names created multiple Academy groups.");

            Console.WriteLine("CHECK scoped create: audit rollback");
            await database.WithGroupAuditFailureAsync(manager,async()=>{
                using var failed=await Post(client,login.Cookie,path,
                    Request("Audit rollback site group",Guid.NewGuid(),site.SecondLocation));
                await ExpectJsonAsync(failed,503,"group_creation_unavailable");
            });
            Require(await database.SyntheticGroupCountAsync(
                "lefteria_fc_academy","Audit rollback site group")==0,
                "Site group creation was not atomic with the audit.");

            Console.WriteLine("CHECK scoped create: immediate grant revocation");
            await database.RevokeAnnouncementTerritoriesAsync(manager,site);
            using(var denied=await Post(client,login.Cookie,path,
                Request("Revoked site group",Guid.NewGuid(),site.SecondLocation)))
                await ExpectJsonAsync(denied,403,"group_creation_denied");
            using(var denied=await Post(client,login.Cookie,path,payload))
                await ExpectJsonAsync(denied,403,"group_creation_denied");
        }
        finally
        {
            Console.WriteLine("CHECK scoped create: cleanup");
            if(groupId is not null)
                await database.CleanupScopedAcademyGroupsAsync(manager,[groupId]);
            await database.CleanupAnnouncementTerritoriesAsync(manager,site);
            await database.CleanupAudienceAsync(roster);
            Console.WriteLine("CHECK scoped create: cleanup complete");
        }
    }

}
