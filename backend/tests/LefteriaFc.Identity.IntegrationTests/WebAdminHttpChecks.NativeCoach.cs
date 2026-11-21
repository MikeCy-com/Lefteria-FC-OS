using System.Net.Http.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private static async Task<HttpResponseMessage> CreateNativeCoachAsync(
        HttpClient client,string? cookie,object payload)
    {
        using var request=Mutation("/api/admin/staff/coaches",JsonContent.Create(payload));
        if(cookie is not null)request.Headers.Add("Cookie",cookie);
        return await client.SendAsync(request);
    }

    private async Task AdminNativeCoachRegistrationAsync()
    {
        const string fc="lefteria_fc";
        const string academy="lefteria_fc_academy";
        var allAdmin=await database.SeedAsync(role:"super_admin",organizations:[fc,academy]);
        var limited=await database.SeedAsync(role:"super_admin",organizations:[fc]);
        var manager=await database.SeedAsync(role:"manager",organizations:[fc,academy]);
        var coachAccount=await database.SeedAsync(role:"coach",organizations:[fc]);
        var academyCoachAccount=await database.SeedAsync(role:"coach",organizations:[academy]);
        var sharedCoachAccount=await database.SeedAsync(role:"coach",organizations:[fc,academy]);
        var editor=await database.SeedAsync(role:"editor",organizations:[fc]);
        var roster=await database.SeedAudienceAsync();
        WebAdminDatabaseFixture.CoachFixture? imported=null;
        WebAdminDatabaseFixture.AnnouncementTerritoryFixture? sites=null;
        var requestIds=new List<Guid>();

        using var client=Client();
        var adminLogin=await LoginAsync(client,allAdmin);
        try
        {
            imported=await database.SeedCoachesAsync(roster);
            sites=await database.SeedAnnouncementTerritoriesAsync(roster,manager);
            var firstRequest=Guid.NewGuid();
            requestIds.Add(firstRequest);
            var first=new {
                request_id=firstRequest,organization_id=fc,
                name="Fictional native First Team coach",job_role="Youth Coach",
                group_id=roster.Team1,account_id=(Guid?)coachAccount.Id
            };
            using(var noCookie=Client())
            using(var denied=await CreateNativeCoachAsync(noCookie,null,first))
                await ExpectJsonAsync(denied,401,"not_authenticated");

            using(var managerClient=Client())
            {
                var managerLogin=await LoginAsync(managerClient,manager);
                using var denied=await CreateNativeCoachAsync(managerClient,managerLogin.Cookie,first);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            using(var coachClient=Client())
            {
                var coachLogin=await LoginAsync(coachClient,coachAccount);
                using var denied=await CreateNativeCoachAsync(coachClient,coachLogin.Cookie,first);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            using(var limitedClient=Client())
            {
                var limitedLogin=await LoginAsync(limitedClient,limited);
                using var denied=await CreateNativeCoachAsync(limitedClient,limitedLogin.Cookie,
                    new {request_id=Guid.NewGuid(),organization_id=academy,
                        name="Fictional foreign coach",job_role="Youth Coach",
                        group_id=roster.Group1,account_id=(Guid?)null});
                await ExpectJsonAsync(denied,403,"not_authorized");
                using var foreignShared=await CreateNativeCoachAsync(limitedClient,limitedLogin.Cookie,
                    new {request_id=Guid.NewGuid(),organization_id=fc,
                        name="Fictional shared Coach link",job_role="Youth Coach",
                        group_id=roster.Team1,account_id=(Guid?)sharedCoachAccount.Id});
                await ExpectJsonAsync(foreignShared,403,"native_coach_denied");
            }

            string staffId;
            Console.WriteLine("CHECK native Coach: first-team registration linked to real Coach account");
            using(var created=await CreateNativeCoachAsync(client,adminLogin.Cookie,first))
            {
                var body=await ExpectJsonAsync(created,201);
                staffId=body.GetProperty("staff_id").GetString()!;
                Require(body.GetProperty("organization_id").GetString()==fc &&
                    body.GetProperty("group_id").GetString()==roster.Team1 &&
                    body.GetProperty("request_id").GetGuid()==firstRequest &&
                    body.GetProperty("account_id").GetGuid()==coachAccount.Id &&
                    !body.GetProperty("replayed").GetBoolean(),
                    "Native Coach registration response did not match exact intended owner, account or squad.");
            }
            await database.VerifyNativeCoachAsync(staffId,fc,roster.Team1,
                coachAccount.Id,first.name);
            Require(await database.CountNativeCoachAuditAsync(staffId)==1,
                "Native Coach must emit one successful pseudonymous audit event.");
            using(var repeated=await CreateNativeCoachAsync(client,adminLogin.Cookie,first))
            {
                var body=await ExpectJsonAsync(repeated,201);
                Require(body.GetProperty("staff_id").GetString()==staffId &&
                    body.GetProperty("replayed").GetBoolean(),
                    "Exact retry created another Coach identity.");
            }
            Require(await database.CountNativeCoachAuditAsync(staffId)==1,
                "Idempotent retry emitted another audit event.");
            using(var mismatch=await CreateNativeCoachAsync(client,adminLogin.Cookie,
                new {request_id=firstRequest,organization_id=fc,
                    name="Conflicting Coach",job_role="Youth Coach",
                    group_id=roster.Team1,account_id=(Guid?)coachAccount.Id}))
                await ExpectJsonAsync(mismatch,409,"native_coach_conflict");

            Console.WriteLine("CHECK native Coach: reject forbidden identity, wrong owner and wrong canonical site");
            using(var wrongRole=await CreateNativeCoachAsync(client,adminLogin.Cookie,
                new {request_id=Guid.NewGuid(),organization_id=fc,
                    name="Fictional wrong role",job_role="Youth Coach",
                    group_id=roster.Team1,account_id=(Guid?)editor.Id}))
                await ExpectJsonAsync(wrongRole,403,"native_coach_denied");
            using(var wrongOrg=await CreateNativeCoachAsync(client,adminLogin.Cookie,
                new {request_id=Guid.NewGuid(),organization_id=fc,
                    name="Fictional wrong owner",job_role="Youth Coach",
                    group_id=roster.Team1,account_id=(Guid?)academyCoachAccount.Id}))
                await ExpectJsonAsync(wrongOrg,403,"native_coach_denied");
            using(var reusedAccount=await CreateNativeCoachAsync(client,adminLogin.Cookie,
                new {request_id=Guid.NewGuid(),organization_id=fc,
                    name="Fictional reused account",job_role="Youth Coach",
                    group_id=roster.Team2,account_id=(Guid?)coachAccount.Id}))
                await ExpectJsonAsync(reusedAccount,403,"native_coach_denied");
            using(var cross=await CreateNativeCoachAsync(client,adminLogin.Cookie,
                new {request_id=Guid.NewGuid(),organization_id=fc,
                    name="Fictional cross-owner link",job_role="Youth Coach",
                    group_id=roster.Group1,account_id=(Guid?)null}))
                await ExpectJsonAsync(cross,409,"native_coach_group_unassigned");
            using(var invalid=await CreateNativeCoachAsync(client,adminLogin.Cookie,
                new {request_id=Guid.NewGuid(),organization_id=fc,
                    name="Fictional invalid job",job_role="super_admin",
                    group_id=roster.Team1,account_id=(Guid?)null}))
                await ExpectJsonAsync(invalid,422,"native_coach_invalid");

            Console.WriteLine("CHECK native Coach: Academy group with verified exact location");
            var academyRequest=Guid.NewGuid();
            requestIds.Add(academyRequest);
            string academyStaff;
            using(var created=await CreateNativeCoachAsync(client,adminLogin.Cookie,new {
                request_id=academyRequest,organization_id=academy,
                name="Fictional Academy coach",job_role="Youth Coach",
                group_id=roster.Group2,account_id=(Guid?)null
            }))
            {
                var body=await ExpectJsonAsync(created,201);
                academyStaff=body.GetProperty("staff_id").GetString()!;
                Require(body.GetProperty("group_id").GetString()==roster.Group2 &&
                    body.GetProperty("account_id").ValueKind==System.Text.Json.JsonValueKind.Null,
                    "Academy creation invented another site or linked identity.");
            }
            await database.VerifyNativeCoachAsync(academyStaff,academy,roster.Group2,
                null,"Fictional Academy coach");
            using(var cross=await CreateNativeCoachAsync(client,adminLogin.Cookie,
                new {request_id=Guid.NewGuid(),organization_id=academy,
                    name="Fictional cross team",job_role="Youth Coach",
                    group_id=roster.Team1,account_id=(Guid?)null}))
                await ExpectJsonAsync(cross,409,"native_coach_group_unassigned");

            using(var fcDirectory=await GetAsync(client,FcCoachesPath,adminLogin.Cookie))
            {
                var body=await ExpectJsonAsync(fcDirectory,200);
                Require(body.GetProperty("coaches").EnumerateArray().Any(x=>
                    x.GetProperty("id").GetString()==staffId &&
                    x.GetProperty("group_ids")[0].GetString()==roster.Team1),
                    "Native coach not visible in actual FC coach directory.");
            }
            using(var academyDirectory=await GetAsync(client,AcademyCoachesPath,adminLogin.Cookie))
            {
                var body=await ExpectJsonAsync(academyDirectory,200);
                Require(body.GetProperty("coaches").EnumerateArray().Any(x=>
                    x.GetProperty("id").GetString()==academyStaff &&
                    x.GetProperty("group_ids")[0].GetString()==roster.Group2),
                    "Native coach not visible in actual Academy coach directory.");
            }
        }
        finally
        {
            await database.CleanupNativeCoachesAsync(requestIds);
            if(imported is not null)await database.CleanupCoachesAsync(imported);
            if(sites is not null)await database.CleanupAnnouncementTerritoriesAsync(manager,sites);
            await database.CleanupAudienceAsync(roster);
        }
    }
}
