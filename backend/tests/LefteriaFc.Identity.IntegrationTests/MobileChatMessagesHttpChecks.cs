using System.Text.Json;
using System.Net.Http.Json;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private static string MessagePath(string organization,string group) =>
        "/api/mobile/chat/groups/"+Uri.EscapeDataString(organization)+"/"+
        Uri.EscapeDataString(group)+"/messages";


    private static async Task<JsonElement> PutAsync(HttpClient client,string url,object body,int status)
    {
        using var reply=await client.PutAsJsonAsync(url,body);
        return await ExpectAsync(reply,status);
    }

    private static async Task<JsonElement> AcknowledgeAsync(HttpClient client,
        string messagePath,int status)
    {
        using var response=await client.PostAsync(messagePath+"/seen",null);
        return await ExpectAsync(response,status);
    }

    private static Task<JsonElement> ReactAsync(HttpClient client,string messagePath,
        string emoji,bool active,int status) =>
        PostAsync(client,messagePath+"/reactions",new{emoji,active},status);

    private async Task<JsonElement> DeleteVersionAsync(HttpClient client,string url,long version,int status)
    {
        using var reply=await client.DeleteAsync(url+"?version="+version);
        return await ExpectAsync(reply,status);
    }

    private async Task ChatMessageHttpChecksAsync(AccountImportFixture fixture,
        HttpClient parent,HttpClient player,HttpClient coach,HttpClient management,HttpClient fan)
    {
        var team=MessagePath(OrganizationIds.Club,fixture.TeamId);
        var academy=MessagePath(OrganizationIds.Academy,fixture.GroupId);
        var sentByFixture=fixture.Plan.Mobiles.Select(row=>row.Id).ToArray();
        try
        {
            var empty=await GetAsync(parent,academy,200);
            Check.Equal(0,empty.GetProperty("messages").GetArrayLength(),
                "New scoped chat should have no fabricated messages.");
            Check.True(empty.GetProperty("next_offset").ValueKind==JsonValueKind.Null,
                "Empty chat returned a false pagination cursor.");
            await GetAsync(parent,team,403);
            await GetAsync(player,academy,403);
            await GetAsync(fan,team,403);
            await PostAsync(fan,team,new{content="denied fan message"},403);
            await PostAsync(parent,team,new{content="cross-team denied"},403);
            await PostAsync(parent,academy,new{content=" "},422);

            var academySent=await PostAsync(parent,academy,
                new{content="Academy private group verification"},200);
            var academyId=Guid.Parse(academySent.GetProperty("id").GetString()!);
            Check.Equal(fixture.GroupId,academySent.GetProperty("group_id").GetString(),
                "Academy group text was stored outside its actual room.");
            var parentMe=await GetAsync(parent,"/api/mobile/auth/me",200);
            Check.Equal(parentMe.GetProperty("id").GetString(),
                academySent.GetProperty("sender_id").GetString(),
                "Message sender was not the current authenticated identity.");
            var teamSent=await PostAsync(player,team,
                new{content="First team member verification"},200);
            var teamId=Guid.Parse(teamSent.GetProperty("id").GetString()!);
            Check.True(academyId!=teamId,"Independent member messages shared an ID.");

            var coachAcademy=await GetAsync(coach,academy,200);
            Check.Equal(1,coachAcademy.GetProperty("messages").GetArrayLength(),
                "Currently assigned coach cannot read the Academy message.");
            Check.Equal(academyId.ToString(),coachAcademy.GetProperty("messages")[0]
                .GetProperty("id").GetString(),"Academy thread returned a wrong message.");
            var coachTeam=await GetAsync(coach,team,200);
            Check.Equal(teamId.ToString(),coachTeam.GetProperty("messages")[0]
                .GetProperty("id").GetString(),"Coach's team thread leaked an Academy message.");
            var managerTeam=await GetAsync(management,team,200);
            Check.Equal(1,managerTeam.GetProperty("messages").GetArrayLength(),
                "Current FC management cannot read an authorized team message.");
            await GetAsync(management,academy,403);

            // The selected organization is current DB state, never a forged
            // query-string owner. Switching must immediately revoke FC access.
            await PostAsync(management,"/api/mobile/management/organization",
                new{organization_id=OrganizationIds.Academy},200);
            try
            {
                await GetAsync(management,team,403);
                Check.Equal(1,(await GetAsync(management,academy,200))
                    .GetProperty("messages").GetArrayLength(),
                    "Management did not rebind chat to its selected Academy.");
            }
            finally
            {
                await PostAsync(management,"/api/mobile/management/organization",
                    new{organization_id=OrganizationIds.Club},200);
            }


            // Reading a thread is not a receipt. A distinct POST from a
            // current participant is required, and only the manager who
            // actually authored this particular message sees reader names.
            var managerSent=await PostAsync(management,team,
                new{content="Manager receipt verification"},200);
            var managerMessageId=Guid.Parse(managerSent.GetProperty("id").GetString()!);
            var managerPath=team+"/"+managerMessageId;
            Check.Equal(0,(await GetAsync(management,managerPath+"/readers",200))
                .GetProperty("readers").GetArrayLength(),
                "GET silently acknowledged unread messages.");
            await GetAsync(coach,managerPath+"/readers",403);
            await GetAsync(player,managerPath+"/readers",403);
            await GetAsync(fan,managerPath+"/readers",403);
            await GetAsync(management,team+"/"+teamId+"/readers",403);
            await AcknowledgeAsync(management,managerPath,403);
            await AcknowledgeAsync(parent,managerPath,403);
            await AcknowledgeAsync(fan,managerPath,403);
            var coachSeen=await AcknowledgeAsync(coach,managerPath,200);
            Check.True(coachSeen.GetProperty("seen").GetBoolean() &&
                coachSeen.GetProperty("message_id").GetString()==managerMessageId.ToString(),
                "Coach explicit read did not acknowledge the manager's exact message.");
            await AcknowledgeAsync(coach,managerPath,200);
            await AcknowledgeAsync(player,managerPath,200);
            var readers=(await GetAsync(management,managerPath+"/readers",200))
                .GetProperty("readers");
            Check.Equal(2,readers.GetArrayLength(),
                "Repeated acknowledgment or GET fabricated a reader.");
            var managerJson=(await GetAsync(management,team,200)).GetProperty("messages");
            Check.True(!managerJson[0].TryGetProperty("readers",out _),
                "Normal group messages exposed reader identities.");
            var currentReaderIds=new HashSet<string>{
                (await GetAsync(coach,"/api/mobile/auth/me",200)).GetProperty("id").GetString()!,
                (await GetAsync(player,"/api/mobile/auth/me",200)).GetProperty("id").GetString()!
            };
            foreach(var reader in readers.EnumerateArray())
            {
                Check.Equal(2,reader.EnumerateObject().Count(),
                    "Manager receipt leaked fields beyond identity ID and display name.");
                Check.True(currentReaderIds.Remove(reader.GetProperty("id").GetString()!) &&
                    !string.IsNullOrWhiteSpace(reader.GetProperty("name").GetString()),
                    "Reader name did not belong to a verified actor.");
            }
            Check.Equal(0,currentReaderIds.Count,
                "The recorded readers did not match actual authenticated participants.");

            // Only currently linked Academy parents may acknowledge. Their
            // stored receipt ceases to be revealed after roster removal.
            await PostAsync(management,"/api/mobile/management/organization",
                new{organization_id=OrganizationIds.Academy},200);
            try
            {
                var academyManagerSent=await PostAsync(management,academy,
                    new{content="Academy management read verification"},200);
                var academyManagerId=Guid.Parse(academyManagerSent.GetProperty("id").GetString()!);
                var academyManagerPath=academy+"/"+academyManagerId;
                await AcknowledgeAsync(parent,academyManagerPath,200);
                Check.Equal(1,(await GetAsync(management,academyManagerPath+"/readers",200))
                    .GetProperty("readers").GetArrayLength(),
                    "Academy parent acknowledgment was not recorded.");
                await using(var db=database.CreateContext())
                {
                    await db.RosterPlayers.Where(row=>row.Id==fixture.ChildId)
                        .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,true));
                    try
                    {
                        await AcknowledgeAsync(parent,academyManagerPath,403);
                        Check.Equal(0,(await GetAsync(management,academyManagerPath+"/readers",200))
                            .GetProperty("readers").GetArrayLength(),
                            "Removed guardian remained visible as a current reader.");
                    }
                    finally
                    {
                        await db.RosterPlayers.Where(row=>row.Id==fixture.ChildId)
                            .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,false));
                    }
                }
                Check.Equal(1,(await GetAsync(management,academyManagerPath+"/readers",200))
                    .GetProperty("readers").GetArrayLength(),
                    "Restored current room guardian was not included as a verified reader.");
            }
            finally
            {
                await PostAsync(management,"/api/mobile/management/organization",
                    new{organization_id=OrganizationIds.Club},200);
            }
            var before=(await GetAsync(parent,academy,200)).GetProperty("messages").GetArrayLength();
            await PostAsync(parent,academy,new{content=new string('X',10001)},422);
            await GetAsync(parent,academy+"?offset=100001",422);
            await GetAsync(parent,academy+"?offset=-1",422);
            await GetAsync(parent,academy+"?offset=0&offset=1",422);
            Check.Equal(before,(await GetAsync(parent,academy,200))
                .GetProperty("messages").GetArrayLength(),
                "Rejected chat write changed persisted conversation history.");
            var ownEditPath=academy+"/"+academyId;
            var otherEditPath=team+"/"+teamId;
            // Reactions must persist per exact account and emoji, never as
            // guessed UI counts or a list of children's identities.
            var parentReaction=await ReactAsync(parent,ownEditPath,"⚽",true,200);
            Check.Equal(academyId.ToString(),parentReaction.GetProperty("message_id").GetString(),
                "Reaction response did not bind to the selected Academy message.");
            var ownSummary=parentReaction.GetProperty("reactions");
            Check.Equal(1,ownSummary.GetArrayLength(),"Initial ⚽ reaction was not stored.");
            Check.Equal("⚽",ownSummary[0].GetProperty("emoji").GetString(),
                "Reaction emoji differed from the checked allowlist.");
            Check.Equal(1,ownSummary[0].GetProperty("count").GetInt32(),
                "Initial reaction did not count once.");
            Check.True(ownSummary[0].GetProperty("mine").GetBoolean(),
                "Parent's authenticated reaction was not marked as their own.");

            var repeated=await ReactAsync(parent,ownEditPath,"⚽",true,200);
            Check.Equal(1,repeated.GetProperty("reactions")[0].GetProperty("count").GetInt32(),
                "Idempotent re-add unexpectedly duplicated a reaction.");
            var coachAdded=await ReactAsync(coach,ownEditPath,"⚽",true,200);
            Check.Equal(2,coachAdded.GetProperty("reactions")[0].GetProperty("count").GetInt32(),
                "Coach reaction did not persist alongside the parent's own reaction.");

            var teamReaction=await ReactAsync(player,otherEditPath,"👍",true,200);
            Check.Equal(1,teamReaction.GetProperty("reactions")[0].GetProperty("count").GetInt32(),
                "First team reaction missing.");
            var managerTeamReaction=await ReactAsync(management,otherEditPath,"👍",true,200);
            Check.Equal(2,managerTeamReaction.GetProperty("reactions")[0].GetProperty("count").GetInt32(),
                "Management's selected FC room reaction missing.");
            await ReactAsync(parent,otherEditPath,"⚽",true,403);
            await ReactAsync(player,ownEditPath,"👍",true,403);
            await ReactAsync(fan,ownEditPath,"⚽",true,403);
            await ReactAsync(management,ownEditPath,"⚽",true,403);
            await ReactAsync(coach,ownEditPath,"👻",true,422);
            await ReactAsync(parent,ownEditPath,"⚽",false,200);
            var withoutParent=(await GetAsync(parent,academy,200)).GetProperty("messages")[0]
                .GetProperty("reactions")[0];
            Check.Equal(1,withoutParent.GetProperty("count").GetInt32(),
                "Removing the parent's reaction removed another member's reaction.");
            Check.True(!withoutParent.GetProperty("mine").GetBoolean(),
                "Removed parent's reaction still appeared as their own.");
            var visibleToCoach=(await GetAsync(coach,academy,200)).GetProperty("messages")[0]
                .GetProperty("reactions")[0];
            Check.True(visibleToCoach.GetProperty("mine").GetBoolean(),
                "Coach lost the personalized reaction flag.");
            await ReactAsync(parent,ownEditPath,"⚽",false,200);
            await ReactAsync(parent,ownEditPath,"⚽",true,200);
            var two=(await GetAsync(coach,academy,200)).GetProperty("messages")[0]
                .GetProperty("reactions")[0];
            Check.Equal(2,two.GetProperty("count").GetInt32(),
                "Persistence and idempotent toggles yielded a wrong shared count.");

            // The response never discloses the names or IDs of reactors.
            foreach(var user in new[]{parent,coach})
            {
                var view=(await GetAsync(user,academy,200)).GetProperty("messages")[0]
                    .GetProperty("reactions")[0];
                Check.Equal(3,view.EnumerateObject().Count(),
                    "A reaction summary leaked participant identities.");
            }

            await PutAsync(coach,ownEditPath,new{content="Not my message",version=1},403);
            await PutAsync(parent,otherEditPath,new{content="Wrong room",version=1},403);
            await PutAsync(fan,ownEditPath,new{content="Fan cannot edit",version=1},403);
            await PutAsync(parent,ownEditPath,new{content=" ",version=1},422);
            var edited=await PutAsync(parent,ownEditPath,
                new{content="Updated Academy message",version=1},200);
            Check.Equal("Updated Academy message",edited.GetProperty("content").GetString(),
                "Author edit did not persist the confirmed new content.");
            Check.Equal(2L,edited.GetProperty("version").GetInt64(),"First edit version mismatch.");
            Check.True(edited.GetProperty("edited_at").ValueKind==JsonValueKind.String &&
                !edited.GetProperty("is_deleted").GetBoolean(),"Edited text was marked deleted.");
            await PutAsync(parent,ownEditPath,new{content="Stale edit denied",version=1},409);
            Check.Equal("Updated Academy message",(await GetAsync(coach,academy,200))
                .GetProperty("messages")[0].GetProperty("content").GetString(),
                "Coach did not see the newly verified edit.");
            await DeleteVersionAsync(coach,ownEditPath,2,403);
            await DeleteVersionAsync(parent,ownEditPath,1,409);

            // Membership revoked in the canonical roster must immediately
            // block further author writes even with a still-valid JWT.
            await using(var db=database.CreateContext())
            {
                await db.RosterPlayers.Where(row=>row.Id==fixture.ChildId)
                    .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,true));
                try{await DeleteVersionAsync(parent,ownEditPath,2,403);}
                finally{await db.RosterPlayers.Where(row=>row.Id==fixture.ChildId)
                    .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,false));}
            }
            await using(var db=database.CreateContext())
            {
                await db.RosterPlayers.Where(row=>row.Id==fixture.ChildId)
                    .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,true));
                try{await ReactAsync(parent,ownEditPath,"🔥",true,403);}
                finally{await db.RosterPlayers.Where(row=>row.Id==fixture.ChildId)
                    .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,false));}
            }
            await AcknowledgeAsync(coach,ownEditPath,200);
            var removed=await DeleteVersionAsync(parent,ownEditPath,2,200);
            Check.True(removed.GetProperty("is_deleted").GetBoolean() &&
                removed.GetProperty("content").GetString()=="" &&
                removed.GetProperty("version").GetInt64()==3,
                "Author deletion did not return a safe versioned tombstone.");
            var tombstone=(await GetAsync(coach,academy,200)).GetProperty("messages")[0];
            Check.True(tombstone.GetProperty("is_deleted").GetBoolean() &&
                tombstone.GetProperty("content").GetString()=="" &&
                tombstone.GetProperty("version").GetInt64()==3,
                "Private deleted content remained in a member's normal read.");
            Check.Equal(0,tombstone.GetProperty("reactions").GetArrayLength(),
                "Deletion left visible reaction counts on a removed message.");
            await AcknowledgeAsync(coach,ownEditPath,403);
            await using(var receiptDb=database.CreateContext())
            {
                var lingering=await receiptDb.Database.SqlQueryRaw<long>(
                    "SELECT count(*) AS \"Value\" FROM club.app_chat_read_receipts WHERE message_id={0}",academyId)
                    .SingleAsync();
                Check.Equal(0L,lingering,"Tombstone retained reader identities.");
            }
            await ReactAsync(coach,ownEditPath,"⚽",true,403);
            await ReactAsync(parent,ownEditPath,"⚽",false,403);
            await using(var db=database.CreateContext())
            {
                var orphaned=await db.Database.SqlQueryRaw<long>(
                    "SELECT count(*) AS \"Value\" FROM club.app_chat_reactions WHERE message_id={0}",academyId)
                    .SingleAsync();
                Check.Equal(0L,orphaned,"Deleted message retained reactable personal records.");
            }

            await PutAsync(parent,ownEditPath,new{content="Reappear",version=3},403);
            await DeleteVersionAsync(parent,ownEditPath,3,403);
            await DeleteVersionAsync(parent,ownEditPath,0,422);
            await DeleteVersionAsync(parent,ownEditPath,2,403);

            // A different legitimate author in another organization retains
            // ownership of their separate message.
            var teamEdit=await PutAsync(player,otherEditPath,
                new{content="Revised first-team message",version=1},200);
            Check.Equal(2L,teamEdit.GetProperty("version").GetInt64(),
                "Player team message edit version did not advance.");
            await PutAsync(management,otherEditPath,
                new{content="Management cannot impersonate sender",version=2},403);
            await using(var db=database.CreateContext())
            {
                var revisions=await db.Database.SqlQueryRaw<long>(
                    "SELECT count(*) AS \"Value\" FROM club.app_chat_message_revisions")
                    .SingleAsync();
                Check.Equal(3L,revisions,"Message edits and deletion did not write exact revision audit rows.");
            }

        }
        finally
        {
            // Only the fictional identities in this disposable fixture may be
            // cleaned up. The test cannot touch real club messages or users.
            await using var context=database.CreateContext();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM club.app_chat_read_receipts
                WHERE reader_identity_id = ANY({sentByFixture})
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM club.app_chat_reactions
                WHERE actor_identity_id = ANY({sentByFixture})
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM club.app_chat_message_revisions
                WHERE actor_identity_id = ANY({sentByFixture})
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM club.app_chat_messages
                WHERE sender_identity_id = ANY({sentByFixture})
                """);
        }
    }
}
