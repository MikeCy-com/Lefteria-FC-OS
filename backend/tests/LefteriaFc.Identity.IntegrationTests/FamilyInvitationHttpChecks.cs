using System.Text.Json;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task FamilyInvitationChecksAsync(AccountImportFixture accounts,
        HttpClient parent, HttpClient player, HttpClient coach, HttpClient management, HttpClient fan)
    {
        var target=database.NewPhone();
        var nextTarget=database.NewPhone();
        var child=accounts.ChildId;
        var second=accounts.Prefix+"-child-two";
        var path="/api/mobile/family/children/"+child+"/invitations";
        var secondPath="/api/mobile/family/children/"+second+"/invitations";
        var parentId=accounts.Plan.Mobiles.Single(row=>row.Role=="parent").Id;
        async Task<JsonElement> IssueAsync(HttpClient client,string url,string phone,int status)=>
            await CalendarRequestAsync(client,HttpMethod.Post,url,new{phone},status);
        async Task<JsonElement> CancelAsync(HttpClient client,Guid id,int status)=>
            await CalendarRequestAsync(client,HttpMethod.Post,
                "/api/mobile/family/invitations/"+id.ToString("D")+"/cancel",null,status);
        async Task<JsonElement> OutgoingAsync(HttpClient client,int status)=>
            await CalendarRequestAsync(client,HttpMethod.Get,
                "/api/mobile/family/outgoing-invitations",null,status);

        try
        {
            using var unauthenticated=Client();
            await IssueAsync(unauthenticated,path,target.WireValue,401);
            await OutgoingAsync(unauthenticated,401);
            foreach(var client in new[]{player,coach,management,fan})
                await OutgoingAsync(client,403);
            Check.Equal(0, (await OutgoingAsync(parent,200)).GetProperty("items").GetArrayLength(),
                "Parent invitation outbox is not initially empty.");
            foreach(var client in new[]{player,coach,management,fan})
                await IssueAsync(client,path,target.WireValue,403);
            await IssueAsync(parent,"/api/mobile/family/children/"+accounts.PlayerId+"/invitations",
                target.WireValue,403);
            // StringContent sets a real Content-Length header; the regular
            // JsonContent streaming test client does not supply this header.
            using(var oversized=new StringContent("{\"phone\":\""+new string('8',17000)+"\"}",
                System.Text.Encoding.UTF8,"application/json"))
            using(var rejected=await parent.PostAsync(path,oversized))
                await CalendarResponseAsync(rejected,413);
            await IssueAsync(parent,path,accounts.ParentPhone.WireValue,422);
            await IssueAsync(parent,path,accounts.PlayerPhone.WireValue,409);

            var invitation=await IssueAsync(parent,path,target.WireValue,200);
            var invitationId=Guid.Parse(invitation.GetProperty("id").GetString()!);
            var outbox=await OutgoingAsync(parent,200);
            var pendingRows=outbox.GetProperty("items").EnumerateArray().ToArray();
            Check.Equal(1,pendingRows.Length,"Newly issued pending invitation is absent from guardian outbox.");
            var row=pendingRows[0];
            Check.Equal(invitationId,Guid.Parse(row.GetProperty("id").GetString()!),
                "Outbox invitation receipt did not match issued reservation.");
            Check.Equal(child,row.GetProperty("player_id").GetString(),
                "Outbox lost the intended child scope.");
            Check.Equal(OrganizationIds.Academy,row.GetProperty("organization_id").GetString(),
                "Outbox lost the child's current Academy organization.");
            Check.Equal("pending",row.GetProperty("status").GetString(),
                "Outbox reported an inactive invitation as a pending reservation.");
            Check.True(row.GetProperty("expires_at").GetDateTimeOffset() >
                row.GetProperty("created_at").GetDateTimeOffset(),
                "Outbox reported an invalid reservation validity period.");
            Check.True(!outbox.GetRawText().Contains(target.Digits,StringComparison.Ordinal) &&
                !outbox.GetRawText().Contains(accounts.ParentPhone.Digits,StringComparison.Ordinal) &&
                !outbox.TryGetProperty("target_phone",out _),
                "Outbox leaked contact or target data.");
            Check.Equal("pending",invitation.GetProperty("status").GetString(),"New invitation did not remain pending.");
            Check.True(!invitation.GetRawText().Contains(target.Digits,StringComparison.Ordinal) &&
                !invitation.GetRawText().Contains(child,StringComparison.Ordinal),
                "Invitation receipt exposed a minor's phone or raw child identifier.");
            var repeat=await IssueAsync(parent,path,target.WireValue,200);
            Check.Equal(invitationId,Guid.Parse(repeat.GetProperty("id").GetString()!),
                "Identical invitation retries generated a different capability.");
            await IssueAsync(parent,secondPath,target.WireValue,409);

            await using(var context=database.CreateContext())
            {
                await context.Database.OpenConnectionAsync();
                await using var command=context.Database.GetDbConnection().CreateCommand();
                command.CommandText="SELECT status, version, target_phone, organization_id FROM identity.family_invitations WHERE player_id=@id";
                command.Parameters.Add(new NpgsqlParameter("id",child));
                await using var reader=await command.ExecuteReaderAsync();
                Check.True(await reader.ReadAsync(),"Issued invitation has no durable reservation.");
                Check.Equal("pending",reader.GetString(0),"Pending slot was not persisted.");
                Check.Equal(1L,reader.GetInt64(1),"Identical request mutated version or audit history.");
                Check.Equal(target.Digits,reader.GetString(2),"Target was not normalized.");
                Check.Equal(OrganizationIds.Academy,reader.GetString(3),"Child scope was not persisted.");
                Check.True(!await reader.ReadAsync(),"More than one slot was created for the child.");
            }
            await CancelAsync(fan,invitationId,403);
            var cancelled=await CancelAsync(parent,invitationId,200);
            Check.True(cancelled.GetProperty("ok").GetBoolean(),"Parent cancellation was not confirmed.");
            await CancelAsync(parent,invitationId,200);
            Check.Equal(0,(await OutgoingAsync(parent,200)).GetProperty("items").GetArrayLength(),
                "Cancelled reservations remained visible as active pending invitations.");

            // A completed cancellation frees the slot for a new unique target
            // without inventing player-role ownership.
            var renewed=await IssueAsync(parent,path,nextTarget.WireValue,200);
            var renewedId=Guid.Parse(renewed.GetProperty("id").GetString()!);
            Check.True(renewedId!=invitationId,"Reissue did not rotate invitation ID.");
            var renewedOutbox=await OutgoingAsync(parent,200);
            var renewedRow=renewedOutbox.GetProperty("items").EnumerateArray().Single();
            Check.Equal(renewedId,Guid.Parse(renewedRow.GetProperty("id").GetString()!),
                "Outbox leaked stale reservation after new issue.");

            await using(var context=database.CreateContext())
            {
                var link=await context.MobileRecordLinks.AsNoTracking().SingleAsync(row=>
                    row.IdentityId==parentId&&row.RecordId==child);
                await context.MobileRecordLinks.Where(row=>row.IdentityId==parentId&&row.RecordId==child).ExecuteDeleteAsync();
                try
                {
                    await IssueAsync(parent,path,database.NewPhone().WireValue,403);
                    Check.Equal(0,(await OutgoingAsync(parent,200)).GetProperty("items").GetArrayLength(),
                        "Revoked guardian still received a private reservation.");
                }
                finally
                {
                    context.ChangeTracker.Clear();context.MobileRecordLinks.Add(link);await context.SaveChangesAsync();
                }
                await context.MobileIdentities.Where(row=>row.Id==parentId)
                    .ExecuteUpdateAsync(setter=>setter.SetProperty(row=>row.IsActive,false));
                try {
                    await IssueAsync(parent,path,database.NewPhone().WireValue,403);
                    await OutgoingAsync(parent,403);
                }
                finally
                {
                    await context.MobileIdentities.Where(row=>row.Id==parentId)
                        .ExecuteUpdateAsync(setter=>setter.SetProperty(row=>row.IsActive,true));
                }
                await context.RosterPlayers.Where(row=>row.Id==child)
                    .ExecuteUpdateAsync(setter=>setter.SetProperty(row=>row.IsArchived,true));
                try {
                    Check.Equal(0,(await OutgoingAsync(parent,200)).GetProperty("items").GetArrayLength(),
                        "Archived child invitation was visible in outgoing parent view.");
                }
                finally {
                    await context.RosterPlayers.Where(row=>row.Id==child)
                        .ExecuteUpdateAsync(setter=>setter.SetProperty(row=>row.IsArchived,false));
                }
                Check.True(!await context.MobileIdentities.AnyAsync(row=>
                    row.Role=="player"&&row.NormalizedPhone==target.Digits),
                    "Pending invitation silently provisioned a player identity.");
                Check.True(await context.SecurityAuditEvents.CountAsync(row=>
                    row.ActorIdentityId==parentId&&row.Action=="mobile.family.invitation.issue")==2,
                    "Issuance did not append exactly one audit entry per new reservation.");
                Check.True(await context.SecurityAuditEvents.CountAsync(row=>
                    row.ActorIdentityId==parentId&&row.Action=="mobile.family.invitation.cancel")==1,
                    "Cancellation replay generated a second successful audit entry.");
            }

            await using var restricted=new NpgsqlConnection(database.AppConnectionStringForTest());
            await restricted.OpenAsync();
            foreach(var statement in new[]{
                "SELECT target_phone FROM identity.family_invitations LIMIT 1",
                "UPDATE identity.family_invitations SET status='active' WHERE false",
                "DELETE FROM identity.family_invitation_events WHERE false"})
            {
                try
                {
                    await using var command=new NpgsqlCommand(statement,restricted);
                    await command.ExecuteNonQueryAsync();
                }
                catch(PostgresException denied)when(denied.SqlState==PostgresErrorCodes.InsufficientPrivilege){continue;}
                throw new InvalidOperationException("Application role bypassed protected invitation table grants.");
            }
        }
        finally
        {
            // Synthetic fixture cleanup is owner-only and scoped to this
            // test's unique child records; production has no such endpoint.
            await using var context=database.CreateContext();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM identity.family_invitation_events WHERE player_id IN ({child},{second})");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM identity.family_invitations WHERE player_id IN ({child},{second})");
            await context.SecurityAuditEvents.Where(row=>
                row.ActorIdentityId==parentId && (row.Action=="mobile.family.invitation.issue" ||
                    row.Action=="mobile.family.invitation.cancel")).ExecuteDeleteAsync();
            await database.CleanupFamilyThrottleAsync(parentId);
        }
    }
}
