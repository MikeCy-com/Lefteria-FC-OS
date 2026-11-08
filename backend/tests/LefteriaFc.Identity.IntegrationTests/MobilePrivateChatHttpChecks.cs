using System.Net.Http.Json;
using System.Text.Json;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private const string PrivateBase="/api/mobile/private";
    private static string PrivateThread(Guid conversation)=>PrivateBase+"/conversations/"+conversation;
    private static async Task<JsonElement> PrivateDeleteAsync(HttpClient client,string uri,int status)
    {
        using var response=await client.DeleteAsync(uri);
        return await ExpectAsync(response,status);
    }

    private async Task PrivateChatHttpChecksAsync(AccountImportFixture fixture,
        HttpClient parent,HttpClient player,HttpClient coach,HttpClient management,HttpClient fan)
    {
        var parentId=fixture.Plan.Mobiles.Single(m=>m.Role=="parent").Id;
        var coachId=fixture.Plan.Mobiles.Single(m=>m.Role=="coach").Id;
        var managementId=fixture.Plan.Mobiles.Single(m=>m.Role=="management").Id;
        var playerId=fixture.Plan.Mobiles.Single(m=>m.Role=="player").Id;
        var scope=Uri.EscapeDataString(OrganizationIds.Academy);
        var group=Uri.EscapeDataString(fixture.GroupId);
        var contactsPath=PrivateBase+"/contacts?organization="+scope+"&group="+group;
        var created=new List<Guid>();
        try
        {
            await GetAsync(fan,PrivateBase+"/conversations",403);
            await GetAsync(player,PrivateBase+"/conversations",403);
            await GetAsync(fan,contactsPath,403);
            await GetAsync(player,contactsPath,403);
            await GetAsync(parent,PrivateBase+"/contacts?organization="+
                Uri.EscapeDataString(OrganizationIds.Club)+"&group="+group,403);
            var contacts=await GetAsync(parent,contactsPath,200);
            var rows=contacts.GetProperty("contacts");
            Check.True(rows.GetArrayLength()>=1,"Linked guardian cannot locate the real assigned coach.");
            Check.True(rows.EnumerateArray().Any(item=>item.GetProperty("id").GetGuid()==coachId),
                "Adult contact directory omitted current Academy coach.");
            foreach(var contact in rows.EnumerateArray())
            {
                Check.Equal(3,contact.EnumerateObject().Count(),
                    "Private discovery revealed phone/source data or additional fields.");
                Check.True(contact.GetProperty("role").GetString() is "coach" or "management",
                    "Guardian was offered a player, fan or unapproved peer.");
            }
            await PostAsync(parent,PrivateBase+"/invitations",
                new{organization_id=OrganizationIds.Academy,group_id=fixture.GroupId,
                    recipient_id=playerId},403);
            await PostAsync(player,PrivateBase+"/invitations",
                new{organization_id=OrganizationIds.Club,group_id=fixture.TeamId,
                    recipient_id=coachId},403);
            await PostAsync(fan,PrivateBase+"/invitations",
                new{organization_id=OrganizationIds.Club,group_id=fixture.TeamId,
                    recipient_id=coachId},403);
            await PostAsync(parent,PrivateBase+"/invitations",
                new{organization_id=OrganizationIds.Academy,group_id=fixture.GroupId,
                    recipient_id=managementId},403);

            var invite=await PostAsync(parent,PrivateBase+"/invitations",
                new{organization_id=OrganizationIds.Academy,group_id=fixture.GroupId,
                    recipient_id=coachId},200);
            var id=invite.GetProperty("id").GetGuid();
            created.Add(id);
            var path=PrivateThread(id);
            Check.True(invite.GetProperty("status").GetString()=="pending"&&
                invite.GetProperty("is_initiator").GetBoolean()&&
                invite.GetProperty("partner_id").GetGuid()==coachId,
                "New private invitation was not a recipient-consent gate.");
            var repeated=await PostAsync(parent,PrivateBase+"/invitations",
                new{organization_id=OrganizationIds.Academy,group_id=fixture.GroupId,
                    recipient_id=coachId},200);
            Check.Equal(id,repeated.GetProperty("id").GetGuid(),
                "Repeated invitation fabricated a duplicate conversation.");
            await GetAsync(parent,path+"/messages",403);
            await PostAsync(parent,path+"/messages",new{content="No approval yet"},403);
            await PostAsync(coach,path+"/messages",new{content="No approval yet"},403);
            await PostAsync(parent,path+"/respond",new{accept=true},403);
            await PostAsync(player,path+"/respond",new{accept=true},403);
            await PostAsync(fan,path+"/respond",new{accept=true},403);
            await PrivateDeleteAsync(parent,path+"/for-me",403);

            var inboxParent=await GetAsync(parent,PrivateBase+"/conversations",200);
            var inboxCoach=await GetAsync(coach,PrivateBase+"/conversations",200);
            Check.True(inboxParent.GetProperty("conversations").EnumerateArray().Any(c=>
                c.GetProperty("id").GetGuid()==id&&c.GetProperty("is_initiator").GetBoolean()),
                "Sender cannot see their pending invitation.");
            Check.True(inboxCoach.GetProperty("conversations").EnumerateArray().Any(c=>
                c.GetProperty("id").GetGuid()==id&&!c.GetProperty("is_initiator").GetBoolean()),
                "Recipient cannot see the pending consent request.");

            var accepted=await PostAsync(coach,path+"/respond",new{accept=true},200);
            Check.True(accepted.GetProperty("status").GetString()=="accepted" &&
                accepted.GetProperty("partner_id").GetGuid()==parentId &&
                !accepted.GetProperty("is_initiator").GetBoolean(),
                "Only recipient acceptance should enable private messages.");
            await PostAsync(coach,path+"/respond",new{accept=true},403);
            await GetAsync(player,path+"/messages",403);
            await GetAsync(fan,path+"/messages",403);
            await GetAsync(management,path+"/messages",403);
            Check.Equal(0,(await GetAsync(parent,path+"/messages?offset=0",200))
                .GetProperty("messages").GetArrayLength(),
                "Consent alone invented a private text message.");

            var sent=await PostAsync(parent,path+"/messages",new{content="Private Academy note"},200);
            var messageId=sent.GetProperty("id").GetGuid();
            Check.True(sent.GetProperty("sender_id").GetGuid()==parentId &&
                sent.GetProperty("conversation_id").GetGuid()==id,
                "Sender was not derived from the authenticated identity.");
            var messagePath=path+"/messages/"+messageId;
            var fromCoach=(await GetAsync(coach,path+"/messages",200)).GetProperty("messages");
            Check.Equal(1,fromCoach.GetArrayLength(),"Accepted coach cannot read approved message.");
            Check.Equal(messageId,fromCoach[0].GetProperty("id").GetGuid(),
                "Private history returned a message from the wrong room.");
            await PutAsync(coach,messagePath,new{content="Impersonation",version=1},403);
            await PrivateDeleteAsync(coach,messagePath+"?version=1",403);
            await PutAsync(parent,messagePath,new{content="Edited private Academy note",version=1},200);
            await PutAsync(parent,messagePath,new{content="Stale update",version=1},409);
            await PrivateDeleteAsync(parent,messagePath+"?version=1",409);
            await PutAsync(parent,messagePath,new{content="Bad",version=0},422);
            await PostAsync(parent,path+"/messages",new{content=" "},422);
            await GetAsync(parent,path+"/messages?offset=-1",422);
            await GetAsync(parent,path+"/messages?offset=0&offset=1",422);
            var deleted=await PrivateDeleteAsync(parent,messagePath+"?version=2",200);
            Check.True(deleted.GetProperty("is_deleted").GetBoolean() &&
                deleted.GetProperty("content").GetString()=="" &&
                deleted.GetProperty("version").GetInt64()==3,
                "Private sender deletion did not clear saved content.");
            await PrivateDeleteAsync(parent,messagePath+"?version=3",403);
            var otherView=(await GetAsync(coach,path+"/messages",200)).GetProperty("messages")[0];
            Check.True(otherView.GetProperty("is_deleted").GetBoolean()&&
                otherView.GetProperty("content").GetString()=="",
                "Recipient still received deleted private content.");

            var coachSent=await PostAsync(coach,path+"/messages",new{content="Verified reply"},200);
            var coachMessageId=coachSent.GetProperty("id").GetGuid();
            Check.True(coachSent.GetProperty("sender_id").GetGuid()==coachId,
                "Coach private sender ID was not source-bound.");
            var hidden=await PrivateDeleteAsync(parent,path+"/for-me",200);
            Check.True(hidden.GetProperty("conversation_id").GetGuid()==id&&
                hidden.GetProperty("hidden").GetBoolean(),
                "Delete-for-me did not confirm viewer-only visibility cursor.");
            Check.True(!(await GetAsync(parent,PrivateBase+"/conversations",200))
                .GetProperty("conversations").EnumerateArray().Any(c=>c.GetProperty("id").GetGuid()==id),
                "Conversation stayed visible to the account that hid its history.");
            Check.Equal(0,(await GetAsync(parent,path+"/messages",200))
                .GetProperty("messages").GetArrayLength(),
                "Delete-for-me returned the viewer's pre-hide history.");
            Check.Equal(2,(await GetAsync(coach,path+"/messages",200))
                .GetProperty("messages").GetArrayLength(),
                "Viewer-only hide erased the recipient's conversation.");

            await PostAsync(coach,path+"/messages",new{content="New message after hide"},200);
            Check.True((await GetAsync(parent,PrivateBase+"/conversations",200))
                .GetProperty("conversations").EnumerateArray().Any(c=>c.GetProperty("id").GetGuid()==id),
                "A new recipient message did not reopen the hidden chat.");
            var newParentHistory=(await GetAsync(parent,path+"/messages",200))
                .GetProperty("messages");
            Check.Equal(1,newParentHistory.GetArrayLength(),
                "Hidden history resurfaced after an incoming message.");
            Check.Equal("New message after hide",newParentHistory[0].GetProperty("content").GetString(),
                "A hidden account did not see only post-hide messages.");

            // Revoking roster membership immediately revokes BOTH private
            // reads and writes; older private authorization is not cached.
            await using(var db=database.CreateContext())
            {
                await db.RosterPlayers.Where(row=>row.Id==fixture.ChildId)
                    .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,true));
                try
                {
                    await GetAsync(parent,path+"/messages",403);
                    await PostAsync(parent,path+"/messages",new{content="Forbidden after transfer"},403);
                    await GetAsync(parent,PrivateBase+"/conversations",403);
                }
                finally
                {
                    await db.RosterPlayers.Where(row=>row.Id==fixture.ChildId)
                        .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,false));
                }
            }

            // A separate First Team pair needs new consent. Declining is
            // terminal for this invitation; sending must remain denied.
            var teamInvite=await PostAsync(coach,PrivateBase+"/invitations",
                new{organization_id=OrganizationIds.Club,group_id=fixture.TeamId,
                    recipient_id=managementId},200);
            var teamId=teamInvite.GetProperty("id").GetGuid();
            created.Add(teamId);
            var rejected=await PostAsync(management,PrivateThread(teamId)+"/respond",
                new{accept=false},200);
            Check.Equal("declined",rejected.GetProperty("status").GetString(),
                "Recipient decline unexpectedly accepted the chat.");
            await PostAsync(coach,PrivateThread(teamId)+"/messages",
                new{content="Rejected consent bypass"},403);
            await GetAsync(management,PrivateThread(teamId)+"/messages",403);
            await PostAsync(coach,PrivateBase+"/invitations",
                new{organization_id=OrganizationIds.Club,group_id=fixture.TeamId,
                    recipient_id=managementId},403);

            // IDs of readers and user-uploaded source contacts must not
            // spill through private chat message projections.
            Check.Equal(9,sent.EnumerateObject().Count(),
                "Private message projection included unreviewed personal fields.");
        }
        finally
        {
            await using var db=database.CreateContext();
            foreach(var id in created)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.app_private_conversation_views WHERE conversation_id={id}");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.app_private_message_revisions WHERE message_id IN (SELECT id FROM club.app_private_messages WHERE conversation_id={id})");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.app_private_messages WHERE conversation_id={id}");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM club.app_private_conversations WHERE id={id}");
            }
        }
    }
}
