using System.Text.Json;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private static string MessagePath(string organization,string group) =>
        "/api/mobile/chat/groups/"+Uri.EscapeDataString(organization)+"/"+
        Uri.EscapeDataString(group)+"/messages";

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
        }
        finally
        {
            // Only the fictional identities in this disposable fixture may be
            // cleaned up. The test cannot touch real club messages or users.
            await using var context=database.CreateContext();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM club.app_chat_messages
                WHERE sender_identity_id = ANY({sentByFixture})
                """);
        }
    }
}
