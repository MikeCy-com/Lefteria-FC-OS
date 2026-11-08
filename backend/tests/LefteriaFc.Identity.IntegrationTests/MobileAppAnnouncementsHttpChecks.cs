using System.Text;
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
        var pdfMediaId=Guid.NewGuid();
        var pdfContent=Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\n%%EOF");
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
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO club.app_announcement_media(
                      id,announcement_id,kind,file_name,content_type,content)
                    VALUES({pdfMediaId},{academyId},'attachment','academy-guide.pdf',
                      'application/pdf',{pdfContent})
                    """);
            }

            var academy=await GetAsync(parent,academyPath,200);
            Check.True(academy.GetProperty("announcements").GetArrayLength()==1 &&
                academy.GetProperty("announcements")[0].GetProperty("id").GetString()==academyId &&
                academy.GetProperty("next_offset").ValueKind==JsonValueKind.Null,
                "Guardian cannot read exactly their currently linked Academy notice.");
            Check.True(!academy.GetRawText().Contains("Internal unpublished secret",
                StringComparison.Ordinal),"Unpublished admin draft leaked to the mobile app.");
            Check.Equal(9,academy.GetProperty("announcements")[0].EnumerateObject().Count(),
                "App notice included draft, author, contact details or hidden internal fields.");
            Check.Equal(0,academy.GetProperty("announcements")[0]
                .GetProperty("reactions").GetArrayLength(),
                "Fresh announcement unexpectedly has reaction participants.");
            // Production/preview config is disabled in the disposable test
            // host; no subscription is persisted or sent without VAPID keys.
            var pushConfig=await GetAsync(parent,"/api/mobile/app/push",200);
            Check.True(!pushConfig.GetProperty("enabled").GetBoolean() &&
                pushConfig.GetProperty("public_key").ValueKind==JsonValueKind.Null &&
                pushConfig.GetProperty("active_devices").GetInt32()==0,
                "Unconfigured push incorrectly activated or exposed a secret.");
            await GetAsync(fan,"/api/mobile/app/push",403);
            await PostAsync(parent,"/api/mobile/app/push/subscriptions",
                new {endpoint="https://fcm.googleapis.com/fcm/send/test",
                    keys=new {p256dh="example",auth="example"}},503);
            const string inboxPath="/api/mobile/app/notifications";
            var memberInbox=await GetAsync(parent,inboxPath,200);
            Check.True(memberInbox.GetProperty("enabled").GetBoolean() &&
                memberInbox.GetProperty("notifications").GetArrayLength()==1 &&
                memberInbox.GetProperty("unread_count").GetInt32()==1 &&
                memberInbox.GetProperty("notifications")[0].GetProperty("announcement_id").GetString()==academyId &&
                memberInbox.GetProperty("notifications")[0].GetProperty("group_id").GetString()==fixture.GroupId &&
                !memberInbox.GetProperty("notifications")[0].GetProperty("seen").GetBoolean() &&
                !memberInbox.GetRawText().Contains("Private Academy only",StringComparison.Ordinal),
                "Notification inbox leaked content, missed a published event or inferred a read.");
            await GetAsync(fan,inboxPath,403);
            Check.True((await GetAsync(player,inboxPath,200))
                .GetProperty("notifications")[0].GetProperty("announcement_id").GetString()==teamId,
                "FC player saw another group's notification.");
            await GetAsync(parent,inboxPath+"?offset=-1",422);
            await GetAsync(parent,inboxPath+"?offset=0&offset=1",422);
            var opted=await PutProfileDetailsAsync(parent,
                new {notification_prefs=new Dictionary<string,bool> {["administrative"]=false}});
            Check.True(!opted.GetProperty("notification_prefs").GetProperty("administrative").GetBoolean(),
                "Notification preference opt-out did not persist.");
            var off=await GetAsync(parent,inboxPath,200);
            Check.True(!off.GetProperty("enabled").GetBoolean() &&
                off.GetProperty("notifications").GetArrayLength()==0 &&
                off.GetProperty("unread_count").GetInt32()==0,
                "Opt-out still delivered an in-app notification.");
            Check.True((await GetAsync(parent,academyPath,200))
                .GetProperty("announcements").GetArrayLength()==1,
                "Notification preference incorrectly revoked access to official announcements.");
            await PutProfileDetailsAsync(parent,
                new {notification_prefs=new Dictionary<string,bool> {["administrative"]=true}});
            var reactAcademy=academyPath+"/"+academyId+"/reactions";
            var reactTeam=teamPath+"/"+teamId+"/reactions";
            var added=await PostAsync(parent,reactAcademy,new {emoji="👍",active=true},200);
            Check.True(added.GetProperty("announcement_id").GetString()==academyId &&
                added.GetProperty("reactions").GetArrayLength()==1 &&
                added.GetProperty("reactions")[0].GetProperty("count").GetInt32()==1 &&
                added.GetProperty("reactions")[0].GetProperty("mine").GetBoolean(),
                "Guardian's persisted announcement reaction was not confirmed.");
            var repeat=await PostAsync(parent,reactAcademy,new {emoji="👍",active=true},200);
            Check.Equal(1,repeat.GetProperty("reactions")[0].GetProperty("count").GetInt32(),
                "Repeated reaction incorrectly double-counted the member.");
            var coachView=await GetAsync(coach,academyPath,200);
            Check.True(coachView.GetProperty("announcements")[0].GetProperty("reactions")[0]
                .GetProperty("count").GetInt32()==1 &&
                !coachView.GetProperty("announcements")[0].GetProperty("reactions")[0]
                    .GetProperty("mine").GetBoolean(),
                "Reaction count leaked another member's reaction ownership.");
            await PostAsync(fan,reactAcademy,new {emoji="👍",active=true},403);
            await PostAsync(parent,reactTeam,new {emoji="👍",active=true},403);
            await PostAsync(player,reactAcademy,new {emoji="👍",active=true},403);
            await PostAsync(parent,reactAcademy,new {emoji="💣",active=true},422);
            await PostAsync(parent,reactAcademy,new {emoji="👍",active=false},200);
            Check.Equal(0,(await GetAsync(parent,academyPath,200))
                .GetProperty("announcements")[0].GetProperty("reactions").GetArrayLength(),
                "Removed reaction was not cleared from member feed.");
            var seenAcademy=academyPath+"/"+academyId+"/seen";
            var seenTeam=teamPath+"/"+teamId+"/seen";
            var ack=await PostAsync(parent,seenAcademy,new{},200);
            Check.True(ack.GetProperty("announcement_id").GetString()==academyId &&
                ack.GetProperty("seen").GetBoolean(),
                "Explicit parent card acknowledgement did not persist.");
            var afterReading=await GetAsync(parent,inboxPath,200);
            Check.True(afterReading.GetProperty("unread_count").GetInt32()==0 &&
                afterReading.GetProperty("notifications")[0].GetProperty("seen").GetBoolean(),
                "Opening the visible announcement did not update its existing read receipt.");
            var duplicate=await PostAsync(parent,seenAcademy,new{},200);
            Check.True(duplicate.GetProperty("seen").GetBoolean(),
                "Repeat visible announcement read was not idempotent.");
            await PostAsync(fan,seenAcademy,new{},403);
            await PostAsync(player,seenAcademy,new{},403);
            await PostAsync(parent,seenTeam,new{},403);
            var pdfPath=academyPath+"/"+academyId+"/media/"+pdfMediaId;
            var media=academy.GetProperty("announcements")[0].GetProperty("media");
            Check.True(media.GetArrayLength()==1&&
                media[0].GetProperty("id").GetGuid()==pdfMediaId&&
                !academy.GetRawText().Contains("base64",StringComparison.Ordinal),
                "Announcement list leaked raw bytes or dropped private PDF metadata.");
            var downloaded=await GetAsync(parent,pdfPath,200);
            Check.True(downloaded.GetProperty("kind").GetString()=="attachment"&&
                downloaded.GetProperty("content_type").GetString()=="application/pdf"&&
                Convert.FromBase64String(downloaded.GetProperty("base64").GetString()!)
                    .SequenceEqual(pdfContent),
                "Current guardian could not download exact private PDF bytes.");
            await GetAsync(fan,pdfPath,403);
            await GetAsync(player,pdfPath,403);
            await GetAsync(parent,teamPath+"/"+teamId+"/media/"+pdfMediaId,403);
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
                try {await GetAsync(parent,academyPath,403);await GetAsync(parent,pdfPath,403);await PostAsync(parent,reactAcademy,new {emoji="⚽",active=true},403);await PostAsync(parent,seenAcademy,new{},403);}
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
            await PostAsync(parent,reactAcademy,new {emoji="👍",active=true},403);
            await PostAsync(parent,seenAcademy,new{},403);
            await GetAsync(parent,pdfPath,403);
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
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.app_announcement_read_receipts WHERE announcement_id={id}");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.app_announcement_reactions WHERE announcement_id={id}");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.app_announcement_notification_events WHERE announcement_id={id}");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.app_announcement_media WHERE announcement_id={id}");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.app_announcements WHERE id={id}");
            }
            foreach(var id in new[]{academyId,teamId,draftId})
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.admin_announcement_drafts WHERE id={id}");
        }
    }
}
