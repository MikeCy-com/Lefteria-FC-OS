using System.Net.Http.Json;
using System.Text;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string FcDraftsPath=
        "/api/admin/communications/announcement-drafts?organization_id=lefteria_fc";
    private const string AcademyDraftsPath=
        "/api/admin/communications/announcement-drafts?organization_id=lefteria_fc_academy";

    private static async Task<HttpResponseMessage> CreateDraft(
        HttpClient client,string cookie,string path,object content)
    {
        using var request=Mutation(path,JsonContent.Create(content));
        if(!string.IsNullOrEmpty(cookie))request.Headers.Add("Cookie",cookie);
        return await client.SendAsync(request);
    }

    private async Task AdminAnnouncementDraftsAsync()
    {
        using(var anonymous=Client())
        using(var denied=await CreateDraft(anonymous,"",FcDraftsPath,
            new{group_id="fake",title="Notice",body="Test"}))
            await ExpectJsonAsync(denied,401,"not_authenticated");

        var admin=await database.SeedAsync(organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,admin);
        var roster=await database.SeedAudienceAsync();
        string fcDraftId="",academyDraftId="";
        try
        {
            Console.WriteLine("CHECK announcements stage: draft creation");
            using(var saved=await CreateDraft(client,login.Cookie,FcDraftsPath,
                new{group_id=roster.Team1,title="Fixture FC announcement",body="Private draft only."}))
            {
                var draft=await ExpectJsonAsync(saved,200);
                Require(draft.GetProperty("id").GetString()?.StartsWith("ann_",StringComparison.Ordinal)==true &&
                    draft.GetProperty("group_id").GetString()==roster.Team1 &&
                    draft.GetProperty("organization_id").GetString()=="lefteria_fc" &&
                    draft.GetProperty("status").GetString()=="draft" &&
                    draft.GetProperty("version").GetInt64()==1,
                    "FC announcement was not a private canonical draft.");
                fcDraftId=draft.GetProperty("id").GetString()!;
            }
            using(var saved=await CreateDraft(client,login.Cookie,AcademyDraftsPath,
                new{group_id=roster.Group2,title="Fixture Academy announcement",body="No notification delivered."}))
            {
                var draft=await ExpectJsonAsync(saved,200);
                academyDraftId=draft.GetProperty("id").GetString()!;
            }

            Require(await database.AnnouncementDraftCountAsync("lefteria_fc",roster.Team1)==1 &&
                await database.AnnouncementDraftCountAsync("lefteria_fc_academy",roster.Group2)==1 &&
                await database.AnnouncementDraftAuditCountAsync(admin)==2,
                "Draft transaction or audit was not committed in exact owner scope.");

            using(var fc=await GetAsync(client,FcDraftsPath,login.Cookie))
            {
                var response=await ExpectJsonAsync(fc,200);
                Require(response.GetProperty("organization_id").GetString()=="lefteria_fc" &&
                    response.GetProperty("drafts").GetArrayLength()==1 &&
                    response.GetRawText().Contains(roster.Team1,StringComparison.Ordinal) &&
                    !response.GetRawText().Contains(roster.Group2,StringComparison.Ordinal),
                    "FC draft list leaked Academy draft.");
            }
            using(var academy=await GetAsync(client,AcademyDraftsPath,login.Cookie))
            {
                var response=await ExpectJsonAsync(academy,200);
                Require(response.GetProperty("drafts").GetArrayLength()==1 &&
                    !response.GetRawText().Contains(roster.Team1,StringComparison.Ordinal),
                    "Academy draft list leaked FC draft.");
            }
            Console.WriteLine("CHECK announcements stage: audit draft rollback");
            await database.WithAnnouncementDraftAuditFailureAsync(admin,async()=>{
                using var denied=await CreateDraft(client,login.Cookie,FcDraftsPath,
                    new{group_id=roster.Team1,title="Must rollback",body="Audit unavailable."});
                await ExpectJsonAsync(denied,503,"announcement_draft_unavailable");
            });
            Require(await database.AnnouncementDraftCountAsync("lefteria_fc",roster.Team1)==1 &&
                await database.AnnouncementDraftAuditCountAsync(admin)==2,
                "Failed audit created an unlogged announcement.");

            foreach(var invalid in new object[]{
                new{group_id=roster.Group1,title="Wrong owner",body="Invalid"},
                new{group_id="missing",title="Absent",body="Invalid"},
                new{group_id=roster.Team1,title="",body="Invalid"},
                new{group_id=roster.Team1,title=" padded ",body="Invalid"},
                new{group_id=roster.Team1,title="Valid",body=" "},
                new{group_id=roster.Team1,title="Valid",body=new string('a',4001)}
            })
                using(var denied=await CreateDraft(client,login.Cookie,FcDraftsPath,invalid))
                    await ExpectJsonAsync(denied,422,"invalid_announcement_draft");

            foreach(var path in new[]{
                "/api/admin/communications/announcement-drafts",
                "/api/admin/communications/announcement-drafts?organization_id=all",
                FcDraftsPath+"&organization_id=lefteria_fc_academy"})
                using(var denied=await CreateDraft(client,login.Cookie,path,
                    new{group_id=roster.Team1,title="Invalid owner",body="No."}))
                    await ExpectJsonAsync(denied,400,"invalid_organization");

            using(var duplicate=Mutation(FcDraftsPath,new StringContent(
                "{\"group_id\":\""+roster.Team1+"\",\"title\":\"A\",\"title\":\"B\",\"body\":\"C\"}",
                Encoding.UTF8,"application/json")))
            {
                duplicate.Headers.Add("Cookie",login.Cookie);
                using var denied=await client.SendAsync(duplicate);
                await ExpectJsonAsync(denied,400,"invalid_json");
            }
            using(var scope=factory.Services.CreateScope())
            await using(var con=await scope.ServiceProvider
                .GetRequiredService<WebAdminConnections>().OpenAsync(CancellationToken.None))
            {
                var denied=false;
                try
                {
                    await using var cmd=new NpgsqlCommand(
                        "SELECT title FROM club.admin_announcement_drafts",con);
                    await cmd.ExecuteScalarAsync();
                }
                catch(PostgresException e) when(e.SqlState=="42501"){denied=true;}
                Require(denied,"Admin-auth obtained direct announcement draft table read.");
            }
            Console.WriteLine("CHECK announcements stage: publish preflight");
            var fcPublish="/api/admin/communications/announcement-drafts/"+fcDraftId+
                "/publish?organization_id=lefteria_fc";
            var academyPublish="/api/admin/communications/announcement-drafts/"+academyDraftId+
                "/publish?organization_id=lefteria_fc_academy";
            var fcPublishedList="/api/admin/communications/app-announcements?organization_id=lefteria_fc";
            var academyPublishedList="/api/admin/communications/app-announcements?organization_id=lefteria_fc_academy";
            using(var before=await GetAsync(client,fcPublishedList,login.Cookie))
                Require((await ExpectJsonAsync(before,200)).GetProperty("announcements").GetArrayLength()==0,
                    "Unpublished notice drafts unexpectedly appeared in mobile publication storage.");
            using(var invalid=await CreateDraft(client,login.Cookie,fcPublish,new{version=0}))
                await ExpectJsonAsync(invalid,400,"invalid_json");
            using(var cross=await CreateDraft(client,login.Cookie,
                "/api/admin/communications/announcement-drafts/"+fcDraftId+
                    "/publish?organization_id=lefteria_fc_academy",new{version=1}))
                await ExpectJsonAsync(cross,403,"not_authorized");
            using(var malformed=await CreateDraft(client,login.Cookie,fcPublish,
                new{version=1,unknown="disallowed"}))
                await ExpectJsonAsync(malformed,400,"invalid_json");

            // A failed security audit must roll back the draft transition and
            // published notice atomically; it must not create an orphaned push.
            Console.WriteLine("CHECK announcements stage: publish audit rollback");
            await database.WithAppAnnouncementAuditFailureAsync(admin,
                "admin.app.announcement.publish",async()=>
                {
                    using var denied=await CreateDraft(client,login.Cookie,fcPublish,new{version=1});
                    await ExpectJsonAsync(denied,503,"app_announcement_unavailable");
                });
            Require(await database.AppAnnouncementCountAsync("lefteria_fc")==0,
                "Failed audit persisted a published app announcement.");
            using(var stillDraft=await GetAsync(client,FcDraftsPath,login.Cookie))
                Require((await ExpectJsonAsync(stillDraft,200)).GetProperty("drafts")
                    .EnumerateArray().Any(d=>d.GetProperty("id").GetString()==fcDraftId),
                    "Failed publication silently consumed a draft.");

            Console.WriteLine("CHECK announcements stage: successful publish");
            using(var published=await CreateDraft(client,login.Cookie,fcPublish,new{version=1}))
            {
                var item=await ExpectJsonAsync(published,200);
                Require(item.GetProperty("id").GetString()==fcDraftId &&
                    item.GetProperty("organization_id").GetString()=="lefteria_fc" &&
                    item.GetProperty("group_id").GetString()==roster.Team1 &&
                    item.GetProperty("status").GetString()=="published" &&
                    item.GetProperty("version").GetInt64()==1,
                    "Published FC announcement had an invalid canonical target or version.");
            }
            using(var published=await CreateDraft(client,login.Cookie,academyPublish,new{version=1}))
                await ExpectJsonAsync(published,200);
            Require(await database.AppAnnouncementCountAsync("lefteria_fc")==1 &&
                await database.AppAnnouncementCountAsync("lefteria_fc_academy")==1 &&
                await database.AppAnnouncementAuditCountAsync(admin,"admin.app.announcement.publish")==2,
                "Published app notices were not scoped and audited atomically.");
            using(var repeated=await CreateDraft(client,login.Cookie,fcPublish,new{version=1}))
                await ExpectJsonAsync(repeated,403,"not_authorized");
            using(var fcLive=await GetAsync(client,fcPublishedList,login.Cookie))
            {
                var item=await ExpectJsonAsync(fcLive,200);
                Require(item.GetProperty("organization_id").GetString()=="lefteria_fc" &&
                    item.GetProperty("announcements").GetArrayLength()==1 &&
                    item.GetProperty("announcements")[0].GetProperty("id").GetString()==fcDraftId &&
                    !item.GetRawText().Contains(academyDraftId,StringComparison.Ordinal),
                    "Admin published FC list leaked Academy content.");
            }
            using(var academyLive=await GetAsync(client,academyPublishedList,login.Cookie))
                Require((await ExpectJsonAsync(academyLive,200))
                    .GetProperty("announcements")[0].GetProperty("id").GetString()==academyDraftId,
                    "Academy published list omitted its sole canonical notice.");

            Console.WriteLine("CHECK announcements stage: stale removal");
            var removeFc="/api/admin/communications/app-announcements/"+fcDraftId+
                "?organization_id=lefteria_fc&version=1";
            using(var staleRequest=new HttpRequestMessage(HttpMethod.Delete,
                "/api/admin/communications/app-announcements/"+fcDraftId+
                    "?organization_id=lefteria_fc&version=2"))
            {
                staleRequest.Headers.Add("Origin",WebAdminApplicationFactory.Origin);
                staleRequest.Headers.Add("X-Lefteria-Admin","1");
                staleRequest.Headers.Add("Cookie",login.Cookie);
                using var denied=await client.SendAsync(staleRequest);
                await ExpectJsonAsync(denied,409,"app_announcement_conflict");
            }
            Console.WriteLine("CHECK announcements stage: remove audit rollback");
            await database.WithAppAnnouncementAuditFailureAsync(admin,
                "admin.app.announcement.remove",async()=>
                {
                    using var request=new HttpRequestMessage(HttpMethod.Delete,removeFc);
                    request.Headers.Add("Origin",WebAdminApplicationFactory.Origin);
                    request.Headers.Add("X-Lefteria-Admin","1");
                    request.Headers.Add("Cookie",login.Cookie);
                    using var denied=await client.SendAsync(request);
                    await ExpectJsonAsync(denied,503,"app_announcement_unavailable");
                });
            Require(await database.AppAnnouncementCountAsync("lefteria_fc")==1,
                "Failed removal audit erased a published announcement.");
            Console.WriteLine("CHECK announcements stage: successful removal");
            using(var request=new HttpRequestMessage(HttpMethod.Delete,removeFc))
            {
                request.Headers.Add("Origin",WebAdminApplicationFactory.Origin);
                request.Headers.Add("X-Lefteria-Admin","1");
                request.Headers.Add("Cookie",login.Cookie);
                using var reply=await client.SendAsync(request);
                var removed=await ExpectJsonAsync(reply,200);
                Require(removed.GetProperty("id").GetString()==fcDraftId &&
                    removed.GetProperty("removed").GetBoolean() &&
                    removed.GetProperty("version").GetInt64()==2,
                    "Admin-only notice removal did not advance the version.");
            }
            Require(await database.AppAnnouncementCountAsync("lefteria_fc")==0 &&
                await database.AppAnnouncementAuditCountAsync(admin,"admin.app.announcement.remove")==1,
                "Removed FC notice still appears or was not audited.");
            using(var removedList=await GetAsync(client,fcPublishedList,login.Cookie))
                Require((await ExpectJsonAsync(removedList,200)).GetProperty("announcements").GetArrayLength()==0,
                    "Admin removal did not remove the published notice from the official feed.");

            Console.WriteLine("CHECK announcements stage: permissions and revoked role");
            var coach=await database.SeedAsync(role:"coach",organizations:["lefteria_fc_academy"]);
            using(var coachClient=Client())
            {
                var coachLogin=await LoginAsync(coachClient,coach);
                using var denied=await GetAsync(coachClient,AcademyDraftsPath,coachLogin.Cookie);
                await ExpectJsonAsync(denied,403,"not_authorized");
                using var deniedPublished=await GetAsync(coachClient,academyPublishedList,coachLogin.Cookie);
                await ExpectJsonAsync(deniedPublished,403,"not_authorized");
                using var deniedPublish=await CreateDraft(coachClient,coachLogin.Cookie,
                    academyPublish,new{version=1});
                await ExpectJsonAsync(deniedPublish,403,"not_authorized");
            }
            await database.SetStateAsync(admin,role:"editor");
            using(var denied=await GetAsync(client,FcDraftsPath,login.Cookie))
                await ExpectJsonAsync(denied,403,"not_authorized");
            using(var denied=await GetAsync(client,academyPublishedList,login.Cookie))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.SetStateAsync(admin,role:"manager");
            await database.RevokeOrganizationAsync(admin,"lefteria_fc");
            using(var denied=await GetAsync(client,FcDraftsPath,login.Cookie))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.ExpireSessionAsync(admin,login.Token);
            using(var denied=await GetAsync(client,AcademyDraftsPath,login.Cookie))
                await ExpectJsonAsync(denied,401,"not_authenticated");
        }
        finally
        {
            Console.WriteLine("CHECK announcements stage: fixture cleanup");
            await database.CleanupAnnouncementDraftsAsync(admin);
            await database.CleanupAudienceAsync(roster);
        }
    }
}
