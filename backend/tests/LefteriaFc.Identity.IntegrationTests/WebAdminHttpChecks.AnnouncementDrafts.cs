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
        try
        {
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
            }
            using(var saved=await CreateDraft(client,login.Cookie,AcademyDraftsPath,
                new{group_id=roster.Group2,title="Fixture Academy announcement",body="No notification delivered."}))
                await ExpectJsonAsync(saved,200);

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
            var coach=await database.SeedAsync(role:"coach",organizations:["lefteria_fc_academy"]);
            using(var coachClient=Client())
            {
                var coachLogin=await LoginAsync(coachClient,coach);
                using var denied=await GetAsync(coachClient,AcademyDraftsPath,coachLogin.Cookie);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            await database.SetStateAsync(admin,role:"editor");
            using(var denied=await GetAsync(client,FcDraftsPath,login.Cookie))
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
            await database.CleanupAnnouncementDraftsAsync(admin);
            await database.CleanupAudienceAsync(roster);
        }
    }
}
