using System.Text.Json;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private static string AppAnnouncementPath(string organization,string group)=>
        "/api/mobile/app/announcements/"+Uri.EscapeDataString(organization)+"/"+
        Uri.EscapeDataString(group);

    private async Task AppAnnouncementHttpChecksAsync(AccountImportFixture fixture,
        HttpClient parent,HttpClient player,HttpClient coach,HttpClient management,HttpClient fan)
    {
        // The administrative publish transition is exercised separately with
        // the real manager cookie in WebAdminHttpChecks. Here an owner-owned
        // disposable published fixture tests what each mobile identity can see.
        var admin=fixture.Plan.Admins.Single().Id;
        var academyId="ann_"+Guid.NewGuid().ToString("N");
        var teamId="ann_"+Guid.NewGuid().ToString("N");
        var draftId="ann_"+Guid.NewGuid().ToString("N");
        var academyPath=AppAnnouncementPath(OrganizationIds.Academy,fixture.GroupId);
        var teamPath=AppAnnouncementPath(OrganizationIds.Club,fixture.TeamId);
        try
        {
            await using(var db=database.CreateContext())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO club.admin_announcement_drafts(
                      id,organization_id,group_id,title,body,status,version,created_by)
                    VALUES({academyId},{OrganizationIds.Academy},{fixture.GroupId},
                      'Academy fixture announcement','Private Academy only',
                      'published',2,{admin})
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO club.admin_announcement_drafts(
                      id,organization_id,group_id,title,body,status,version,created_by)
                    VALUES({teamId},{OrganizationIds.Club},{fixture.TeamId},
                      'Club fixture announcement','Team members only',
                      'published',2,{admin})
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO club.admin_announcement_drafts(
                      id,organization_id,group_id,title,body,status,version,created_by)
                    VALUES({draftId},{OrganizationIds.Academy},{fixture.GroupId},
                      'Internal unpublished secret','Never show this draft',
                      'draft',1,{admin})
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO club.app_announcements(
                      id,organization_id,group_id,title,body,published_by)
                    VALUES({academyId},{OrganizationIds.Academy},{fixture.GroupId},
                      'Academy fixture announcement','Private Academy only',{admin})
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO club.app_announcements(
                      id,organization_id,group_id,title,body,published_by)
                    VALUES({teamId},{OrganizationIds.Club},{fixture.TeamId},
                      'Club fixture announcement','Team members only',{admin})
                    """);
            }

            var academy=await GetAsync(parent,academyPath,200);
            Check.True(academy.GetProperty("announcements").GetArrayLength()==1 &&
                academy.GetProperty("announcements")[0].GetProperty("id").GetString()==academyId &&
                academy.GetProperty("next_offset").ValueKind==JsonValueKind.Null,
                "Guardian cannot read exactly their currently linked Academy notice.");
            Check.True(!academy.GetRawText().Contains("Internal unpublished secret",
                StringComparison.Ordinal),"Unpublished admin draft leaked to the mobile app.");
            Check.Equal(7,academy.GetProperty("announcements")[0].EnumerateObject().Count(),
                "App notice included draft, author, contact details or hidden internal fields.");
            await GetAsync(parent,teamPath,403);
            await GetAsync(player,academyPath,403);
            await GetAsync(fan,academyPath,403);
            await GetAsync(fan,teamPath,403);
            var playerTeam=await GetAsync(player,teamPath,200);
            Check.True(playerTeam.GetProperty("announcements").GetArrayLength()==1 &&
                playerTeam.GetProperty("announcements")[0].GetProperty("id").GetString()==teamId,
                "Active First Team player cannot see their own official team notice.");
            Check.Equal(1,(await GetAsync(coach,academyPath,200))
                .GetProperty("announcements").GetArrayLength(),
                "Current Academy coach cannot see the Academy announcement.");
            Check.Equal(1,(await GetAsync(coach,teamPath,200))
                .GetProperty("announcements").GetArrayLength(),
                "Current team coach cannot see their team announcement.");
            await GetAsync(management,academyPath,403);
            Check.Equal(1,(await GetAsync(management,teamPath,200))
                .GetProperty("announcements").GetArrayLength(),
                "Selected FC manager cannot read active FC notice.");
            await PostAsync(management,"/api/mobile/management/organization",
                new{organization_id=OrganizationIds.Academy},200);
            try
            {
                await GetAsync(management,teamPath,403);
                Check.Equal(1,(await GetAsync(management,academyPath,200))
                    .GetProperty("announcements").GetArrayLength(),
                    "Management organization selection did not rebind notice access.");
            }
            finally
            {
                await PostAsync(management,"/api/mobile/management/organization",
                    new{organization_id=OrganizationIds.Club},200);
            }
            await GetAsync(parent,academyPath+"?offset=-1",422);
            await GetAsync(parent,academyPath+"?offset=100001",422);
            await GetAsync(parent,academyPath+"?offset=0&offset=1",422);
            Check.Equal(0,(await GetAsync(parent,academyPath+"?offset=50",200))
                .GetProperty("announcements").GetArrayLength(),
                "Announcements ignored bounded pagination.");

            await using(var db=database.CreateContext())
            {
                await db.RosterPlayers.Where(r=>r.Id==fixture.ChildId)
                    .ExecuteUpdateAsync(setters=>setters.SetProperty(r=>r.IsArchived,true));
                try {await GetAsync(parent,academyPath,403);}
                finally
                {
                    await db.RosterPlayers.Where(r=>r.Id==fixture.ChildId)
                        .ExecuteUpdateAsync(setters=>setters.SetProperty(r=>r.IsArchived,false));
                }
            }
            Check.Equal(1,(await GetAsync(parent,academyPath,200))
                .GetProperty("announcements").GetArrayLength(),
                "Restoring guardian roster assignment did not restore notice access.");
            await using(var db=database.CreateContext())
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE club.app_announcements SET removed_at=clock_timestamp()
                    WHERE id={academyId}
                    """);
            Check.Equal(0,(await GetAsync(parent,academyPath,200))
                .GetProperty("announcements").GetArrayLength(),
                "Admin-removed announcement was still visible in mobile reader.");
            Check.Equal(1,(await GetAsync(player,teamPath,200))
                .GetProperty("announcements").GetArrayLength(),
                "Removing Academy announcement affected an unrelated FC team.");
        }
        finally
        {
            await using var db=database.CreateContext();
            foreach(var id in new[]{academyId,teamId})
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.app_announcements WHERE id={id}");
            foreach(var id in new[]{academyId,teamId,draftId})
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.admin_announcement_drafts WHERE id={id}");
        }
    }
}
