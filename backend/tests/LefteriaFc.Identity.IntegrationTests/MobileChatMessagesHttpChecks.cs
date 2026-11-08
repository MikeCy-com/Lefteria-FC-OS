using System.Text.Json;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private static string MessagePath(string organization,string group) =>
        "/api/mobile/chat/groups/"+Uri.EscapeDataString(organization)+"/"+
        Uri.EscapeDataString(group)+"/messages";


    private async Task<JsonElement> DeleteVersionAsync(HttpClient client,string url,long version,int status)
    {
        using var reply=await client.DeleteAsync(url+"?version="+version);
        await ExpectAsync(reply,status);
        using var document=JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
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
