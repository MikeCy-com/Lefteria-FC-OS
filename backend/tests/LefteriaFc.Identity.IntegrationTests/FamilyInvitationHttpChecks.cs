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
        // The recipient must prove the exact target number through the real
        // OTP login; an existing guardian/player/coach token cannot substitute.
        await database.SeedIdentityAsync(target, LefteriaFc.Domain.Identity.MobileRole.Fan);
        using var recipient=Client();
        await LoginAsync(recipient,target,"fan");
        var child=accounts.ChildId;
        var second=accounts.Prefix+"-child-two";
        var path="/api/mobile/family/children/"+child+"/invitations";
        var secondPath="/api/mobile/family/children/"+second+"/invitations";
        var parentId=accounts.Plan.Mobiles.Single(row=>row.Role=="parent").Id;
        Guid? acceptedRecipientId=null;
        async Task<JsonElement> IssueAsync(HttpClient client,string url,string phone,int status)=>
            await CalendarRequestAsync(client,HttpMethod.Post,url,new{phone},status);
        async Task<JsonElement> CancelAsync(HttpClient client,Guid id,int status)=>
            await CalendarRequestAsync(client,HttpMethod.Post,
                "/api/mobile/family/invitations/"+id.ToString("D")+"/cancel",null,status);
        async Task<JsonElement> OutgoingAsync(HttpClient client,int status)=>
            await CalendarRequestAsync(client,HttpMethod.Get,
                "/api/mobile/family/outgoing-invitations",null,status);
        async Task<JsonElement> RecipientAsync(HttpClient client,int status)=>
            await CalendarRequestAsync(client,HttpMethod.Get,
                "/api/mobile/family/invitations",null,status);
        async Task<JsonElement> DelegationStatusAsync(HttpClient client,int status)=>
            await CalendarRequestAsync(client,HttpMethod.Get,
                "/api/mobile/family/active-delegation",null,status);
        async Task<JsonElement> DeclineAsync(HttpClient client,Guid invitation,int status)=>
            await CalendarRequestAsync(client,HttpMethod.Post,
                "/api/mobile/family/invitations/"+invitation.ToString("D")+"/decline",null,status);

        try
        {
            using var unauthenticated=Client();
            await IssueAsync(unauthenticated,path,target.WireValue,401);
            await OutgoingAsync(unauthenticated,401);
            await RecipientAsync(unauthenticated,401);
            foreach(var otherRole in new[]{parent,player,coach,management})
                await RecipientAsync(otherRole,403);
            await DelegationStatusAsync(unauthenticated,401);
            foreach(var otherRole in new[]{parent,player,coach,management})
                await DelegationStatusAsync(otherRole,403);
            Check.True(!(await DelegationStatusAsync(recipient,200))
                .GetProperty("has_active_delegation").GetBoolean(),
                "Unaccepted pending invitations exposed a live delegated player grant.");
            Check.Equal(0,(await RecipientAsync(recipient,200)).GetProperty("items").GetArrayLength(),
                "The verified target fan saw a nonexistent invitation.");
            Check.Equal(0,(await RecipientAsync(fan,200)).GetProperty("items").GetArrayLength(),
                "The unrelated verified fan obtained another phone's invitation.");
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
            var firstAcceptPath="/api/mobile/family/invitations/"+invitationId.ToString("D")+"/accept";
            await CalendarRequestAsync(unauthenticated,HttpMethod.Post,firstAcceptPath,null,401);
            foreach(var nonRecipient in new[]{parent,player,coach,management})
                await CalendarRequestAsync(nonRecipient,HttpMethod.Post,firstAcceptPath,null,403);
            // A different OTP-verified fan has the correct *role*, but not
            // the invitation's phone. Conceal invitation existence via 404.
            await CalendarRequestAsync(fan,HttpMethod.Post,firstAcceptPath,null,404);
            // No acceptance here: cancellation and recipient inbox tests must
            // still cover an actually pending invitation.
            var recipientInbox=await RecipientAsync(recipient,200);
            var recipientRows=recipientInbox.GetProperty("items").EnumerateArray().ToArray();
            Check.Equal(1,recipientRows.Length,
                "The OTP-verified recipient phone could not retrieve its one pending reservation.");
            Check.Equal(invitationId,Guid.Parse(recipientRows[0].GetProperty("id").GetString()!),
                "Recipient inbox does not match the reserved invitation.");
            Check.Equal("pending",recipientRows[0].GetProperty("status").GetString(),
                "Recipient inbox claimed an invitation had already been accepted.");
            Check.Equal(OrganizationIds.Academy,
                recipientRows[0].GetProperty("organization_id").GetString(),
                "Recipient inbox lost the verified child's scoped organization.");
            Check.True(recipientRows[0].GetProperty("expires_at").GetDateTimeOffset() >
                DateTimeOffset.UtcNow,"Recipient inbox returned an expired record.");
            foreach(var privateKey in new[]{
                "player_id","parent_identity_id","parent_phone","target_phone",
                "child_name","guardian","linked_player_id","phone"})
                Check.True(!recipientInbox.GetRawText().Contains("\""+privateKey+"\"",StringComparison.OrdinalIgnoreCase),
                    "Recipient inbox exposed a private minor/guardian field: "+privateKey);
            Check.True(!recipientInbox.GetRawText().Contains(child,StringComparison.Ordinal)
                && !recipientInbox.GetRawText().Contains(target.Digits,StringComparison.Ordinal)
                && !recipientInbox.GetRawText().Contains(accounts.ParentPhone.Digits,StringComparison.Ordinal),
                "Recipient inbox leaked a raw child ID or someone's phone.");
            Check.Equal(0,(await RecipientAsync(fan,200)).GetProperty("items").GetArrayLength(),
                "A different phone's fan session saw the invitation.");
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
            Check.Equal(0,(await RecipientAsync(recipient,200)).GetProperty("items").GetArrayLength(),
                "Cancelled invitation was still exposed to the target phone.");
            Check.Equal(0,(await OutgoingAsync(parent,200)).GetProperty("items").GetArrayLength(),
                "Cancelled reservations remained visible as active pending invitations.");

            // A completed cancellation frees the slot for a new unique target
            // without inventing player-role ownership.
            var renewed=await IssueAsync(parent,path,nextTarget.WireValue,200);
            var renewedId=Guid.Parse(renewed.GetProperty("id").GetString()!);
            Check.True(renewedId!=invitationId,"Reissue did not rotate invitation ID.");
            // Reissue for another phone must never resurrect a stale record in
            // the original target's session. The new phone must independently
            // establish a real OTP-confirmed fan session.
            Check.Equal(0,(await RecipientAsync(recipient,200)).GetProperty("items").GetArrayLength(),
                "Reissue leaked a different phone's invitation.");
            await database.SeedIdentityAsync(nextTarget,LefteriaFc.Domain.Identity.MobileRole.Fan);
            using var renewedRecipient=Client();
            await LoginAsync(renewedRecipient,nextTarget,"fan");
            Check.Equal(renewedId,Guid.Parse((await RecipientAsync(renewedRecipient,200))
                .GetProperty("items").EnumerateArray().Single().GetProperty("id").GetString()!),
                "New target failed current phone proof or could not read its reservation.");
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
                    Check.Equal(0,(await RecipientAsync(renewedRecipient,200)).GetProperty("items").GetArrayLength(),
                        "Recipient obtained a reservation after the source guardian link was revoked.");
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
                    Check.Equal(0,(await RecipientAsync(renewedRecipient,200)).GetProperty("items").GetArrayLength(),
                        "Recipient could still read an invitation when the source guardian was deactivated.");
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
                    Check.Equal(0,(await RecipientAsync(renewedRecipient,200)).GetProperty("items").GetArrayLength(),
                        "Archived child reservation remained in recipient inbox.");
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

            // The phone-verified recipient may close ONLY their own pending
            // slot. A decline does not create or link a player identity.
            await DeclineAsync(unauthenticated,renewedId,401);
            foreach(var otherRole in new[]{parent,player,coach,management})
                await DeclineAsync(otherRole,renewedId,403);
            await DeclineAsync(recipient,renewedId,404);
            await DeclineAsync(renewedRecipient,Guid.NewGuid(),404);
            var declined=await DeclineAsync(renewedRecipient,renewedId,200);
            Check.True(declined.GetProperty("ok").GetBoolean(),
                "Verified recipient did not receive an actual decline confirmation.");
            await DeclineAsync(renewedRecipient,renewedId,404);
            Check.Equal(0,(await RecipientAsync(renewedRecipient,200))
                .GetProperty("items").GetArrayLength(),
                "Declined invitation remained visible in recipient inbox.");
            Check.Equal(0,(await OutgoingAsync(parent,200))
                .GetProperty("items").GetArrayLength(),
                "Declined invitation remained in source guardian's pending outbox.");
            var verifiedFan=(await database.IdentityAsync(nextTarget,
                LefteriaFc.Domain.Identity.MobileRole.Fan))!;
            await using(var context=database.CreateContext())
            {
                var declinedStatus=await context.Database.SqlQuery<string>($"""
                    SELECT status AS "Value" FROM identity.family_invitations
                    WHERE invitation_id={renewedId}
                    """).SingleAsync();
                Check.Equal("declined",declinedStatus,
                    "Decline did not transition the durable reserved slot.");
                var count=await context.Database.SqlQuery<int>($"""
                    SELECT count(*)::integer AS "Value" FROM identity.family_invitation_events
                    WHERE invitation_id={renewedId} AND action='declined'
                    """).SingleAsync();
                Check.Equal(1,count,"Replay wrote more than one private decline event.");
                Check.Equal(1,await context.SecurityAuditEvents.CountAsync(row=>
                    row.ActorIdentityId==verifiedFan.Id &&
                    row.Action=="mobile.family.invitation.decline"),
                    "Decline audit did not record the exact verified recipient.");
                Check.True(!await context.MobileIdentities.AnyAsync(row=>
                    row.NormalizedPhone==nextTarget.Digits && row.Role=="player"),
                    "Declining an invitation silently provisioned a player login.");
            }

            // Delegated grants are deliberately storage-only in this slice.
            // The table has two independent active-uniqueness boundaries and
            // cannot be used by the API runtime until accept/revoke and all
            // private player authorization are implemented and tested.
            await using(var context=database.CreateContext())
            {
                var uniqueIndexes=await context.Database.SqlQuery<int>($"""
                    SELECT count(*)::integer AS "Value" FROM pg_indexes
                    WHERE schemaname='identity'
                      AND tablename='family_delegated_player_grants'
                      AND indexname IN ('family_delegate_one_active_player',
                        'family_delegate_one_active_recipient')
                      AND indexdef LIKE '%UNIQUE INDEX%'
                    """).SingleAsync();
                Check.Equal(2,uniqueIndexes,
                    "Delegated family grant storage lacks active recipient/child uniqueness constraints.");
                var count=await context.Database.SqlQuery<int>($"""
                    SELECT count(*)::integer AS "Value"
                    FROM identity.family_delegated_player_grants
                    """).SingleAsync();
                Check.Equal(0,count,
                    "A pending invitation unexpectedly created an active or historical delegated grant.");
            }

            // Owner-only test fixture simulates a future accepted grant. The
            // public acceptance route remains absent; this verifies that the
            // narrowly granted revoke primitive itself is atomic and scoped.
            var syntheticGrantId=Guid.NewGuid();
            await using(var context=database.CreateContext())
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE identity.family_invitations SET status='active',
                      version=version+1,updated_at=now()
                    WHERE invitation_id={renewedId} AND status='declined'
                    """);
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO identity.family_delegated_player_grants
                      (grant_id,invitation_id,player_id,organization_id,
                       recipient_identity_id,granted_by_identity_id,status,granted_at)
                    VALUES ({syntheticGrantId},{renewedId},{child},{OrganizationIds.Academy},
                      {verifiedFan.Id},{parentId},'active',now())
                    """);
            }
            await using(var revokeConnection=new NpgsqlConnection(database.AppConnectionStringForTest()))
            {
                await revokeConnection.OpenAsync();
                async Task<bool> RevokeAsync(Guid actor,string role)
                {
                    await using var cmd=new NpgsqlCommand("""
                        SELECT identity.revoke_family_delegated_player_grant(
                          @actor,@phone,@role,@expires,@device,@player)
                        """,revokeConnection);
                    cmd.Parameters.AddWithValue("actor",actor);
                    cmd.Parameters.AddWithValue("phone",accounts.ParentPhone.Digits);
                    cmd.Parameters.AddWithValue("role",role);
                    cmd.Parameters.AddWithValue("expires",DateTimeOffset.UtcNow.AddMinutes(5));
                    cmd.Parameters.AddWithValue("device",Guid.NewGuid());
                    cmd.Parameters.AddWithValue("player",child);
                    return (bool)(await cmd.ExecuteScalarAsync() ?? false);
                }
                try { await RevokeAsync(parentId,"fan");
                    throw new InvalidOperationException("Fan session revoked a private player grant."); }
                catch(PostgresException e) when(e.SqlState=="P0001" &&
                    e.MessageText=="family_invitation_denied") { }
                Check.True(await RevokeAsync(parentId,"parent"),
                    "The verified guardian failed to revoke an active grant.");
                try { await RevokeAsync(parentId,"parent");
                    throw new InvalidOperationException("Revocation replay was treated as a fresh success."); }
                catch(PostgresException e) when(e.SqlState=="P0001" &&
                    e.MessageText=="family_invitation_missing") { }
            }
            await using(var context=database.CreateContext())
            {
                var revoked=await context.Database.SqlQuery<string>($"""
                    SELECT status AS "Value" FROM identity.family_delegated_player_grants
                    WHERE grant_id={syntheticGrantId}
                    """).SingleAsync();
                Check.Equal("revoked",revoked,"Revocation left a reusable active grant.");
                var slot=await context.Database.SqlQuery<string>($"""
                    SELECT status AS "Value" FROM identity.family_invitations
                    WHERE invitation_id={renewedId}
                    """).SingleAsync();
                Check.Equal("revoked",slot,"Invitation state was not revoked atomically.");
                var events=await context.Database.SqlQuery<int>($"""
                    SELECT count(*)::integer AS "Value" FROM identity.family_invitation_events
                    WHERE invitation_id={renewedId} AND action='revoked'
                    """).SingleAsync();
                Check.Equal(1,events,"A single grant revocation did not create exactly one event.");
            }

            // End-to-end SQL boundary: issue through the real parent API,
            // accept only as the OTP-backed fan, and verify that an accepted
            // grant does not silently promote that fan to a player identity.
            // The preceding invitation/cancel/decline/revoke scenarios spend
            // the real per-parent hourly rate-limit budget. Start a separate
            // owner-controlled fixture phase instead of weakening production
            // throttling or letting the acceptance regression fail with 429.
            await database.CleanupFamilyThrottleAsync(parentId);
            const string activePath="/api/mobile/family/active-children";
            await CalendarRequestAsync(unauthenticated,HttpMethod.Get,activePath,null,401);
            foreach(var nonParent in new[]{player,coach,management,fan,recipient})
                await CalendarRequestAsync(nonParent,HttpMethod.Get,activePath,null,403);
            var before=await CalendarRequestAsync(parent,HttpMethod.Get,activePath,null,200);
            Check.Equal(0,before.GetProperty("items").GetArrayLength(),
                "An unaccepted family invitation was counted as an active parent grant.");
            var candidate=await IssueAsync(parent,path,target.WireValue,200);
            var candidateId=Guid.Parse(candidate.GetProperty("id").GetString()!);
            var recipientFan=(await database.IdentityAsync(target,
                LefteriaFc.Domain.Identity.MobileRole.Fan))!;
            acceptedRecipientId=recipientFan.Id;
            await using(var connection=new NpgsqlConnection(database.AppConnectionStringForTest()))
            {
                await connection.OpenAsync();
                async Task<bool> AcceptAsync(string role)
                {
                    await using var command=new NpgsqlCommand("""
                        SELECT identity.accept_recipient_family_invitation(
                          @recipient,@phone,@role,@expires,@device,@invitation,@grant)
                        """,connection);
                    command.Parameters.AddWithValue("recipient",recipientFan.Id);
                    command.Parameters.AddWithValue("phone",target.Digits);
                    command.Parameters.AddWithValue("role",role);
                    command.Parameters.AddWithValue("expires",DateTimeOffset.UtcNow.AddMinutes(5));
                    command.Parameters.AddWithValue("device",Guid.NewGuid());
                    command.Parameters.AddWithValue("invitation",candidateId);
                    command.Parameters.AddWithValue("grant",Guid.NewGuid());
                    return (bool)(await command.ExecuteScalarAsync()??false);
                }
                try { await AcceptAsync("player");
                    throw new InvalidOperationException("Non-fan session activated a delegated grant."); }
                catch(PostgresException ex) when(ex.SqlState=="P0001" &&
                    ex.MessageText=="family_invitation_denied") { }
                // End-to-end acceptance uses the actual OTP bearer token,
                // current identity service, throttling and restricted DB role.
                var accepted=await CalendarRequestAsync(recipient,HttpMethod.Post,
                    "/api/mobile/family/invitations/"+candidateId.ToString("D")+"/accept",null,200);
                Check.True(accepted.GetProperty("ok").GetBoolean(),
                    "Verified recipient acceptance API did not persist the delegation.");
                Check.Equal(1,accepted.EnumerateObject().Count(),
                    "Acceptance response revealed player or guardian data.");
                try { await AcceptAsync("fan");
                    throw new InvalidOperationException("Acceptance replay provisioned another grant."); }
                catch(PostgresException ex) when(ex.SqlState=="P0001" &&
                    ex.MessageText=="family_invitation_missing") { }
            }
            await using(var context=database.CreateContext())
            {
                var activeCount=await context.Database.SqlQuery<int>($"""
                    SELECT count(*)::integer AS "Value" FROM identity.family_delegated_player_grants
                    WHERE invitation_id={candidateId} AND status='active'
                      AND recipient_identity_id={recipientFan.Id}
                    """).SingleAsync();
                Check.Equal(1,activeCount,
                    "Verified acceptance did not persist exactly one active delegated grant.");
                var status=await context.Database.SqlQuery<string>($"""
                    SELECT status AS "Value" FROM identity.family_invitations
                    WHERE invitation_id={candidateId}
                    """).SingleAsync();
                Check.Equal("active",status,"Accepted reservation remained pending.");
                Check.True(!await context.MobileIdentities.AnyAsync(row=>
                    row.NormalizedPhone==target.Digits && row.Role=="player"),
                    "Acceptance silently elevated the recipient fan to a player identity.");
                var eventCount=await context.Database.SqlQuery<int>($"""
                    SELECT count(*)::integer AS "Value" FROM identity.family_invitation_events
                    WHERE invitation_id={candidateId} AND action='accepted'
                    """).SingleAsync();
                Check.Equal(1,eventCount,"Acceptance replay added duplicate audit history.");
            }
            var parentActive=await CalendarRequestAsync(parent,HttpMethod.Get,activePath,null,200);
            var activeRows=parentActive.GetProperty("items").EnumerateArray().ToArray();
            Check.Equal(1,activeRows.Length,
                "A verified parent must see exactly one current accepted delegation.");
            Check.Equal(child,activeRows[0].GetProperty("player_id").GetString(),
                "Parent delegation listed an unrelated child.");
            Check.Equal(OrganizationIds.Academy,
                activeRows[0].GetProperty("organization_id").GetString(),
                "Parent delegation crossed the club scope.");
            Check.Equal(2,activeRows[0].EnumerateObject().Count(),
                "Parent delegation leaked recipient or grant metadata.");
            var acceptedStatus=await DelegationStatusAsync(recipient,200);
            Check.True(acceptedStatus.GetProperty("has_active_delegation").GetBoolean(),
                "Verified recipient status did not reflect a current accepted grant.");
            Check.Equal(1,acceptedStatus.EnumerateObject().Count(),
                "Recipient delegation status exposed child or guardian metadata.");
            Check.True(!(await DelegationStatusAsync(renewedRecipient,200))
                .GetProperty("has_active_delegation").GetBoolean(),
                "A different verified fan learned someone else's delegation status.");
            // Persisted grant is not itself a player login. The existing fan
            // token may not switch roles until the complete authorization
            // projection and revocation checks have been implemented.
            var fanMe=await CalendarRequestAsync(recipient,HttpMethod.Get,
                "/api/mobile/auth/me",null,200);
            Check.Equal("fan",fanMe.GetProperty("role").GetString(),
                "Acceptance changed the verified session's fan role.");
            Check.True(!fanMe.GetProperty("available_roles").EnumerateArray()
                .Any(role=>role.GetString()=="player"),
                "Unimplemented player authorization was advertised after acceptance.");
            await CalendarRequestAsync(recipient,HttpMethod.Post,
                "/api/mobile/auth/switch-role",new{role="player"},403);

            // Current delegated access is a revalidated projection, not a
            // stored active flag. The OTP recipient can inspect only their
            // own grant, which disappears as soon as the guardian or the
            // grant is revoked. No player session is issued by this query.
            await using(var connection=new NpgsqlConnection(database.AppConnectionStringForTest()))
            {
                await connection.OpenAsync();
                async Task<(Guid GrantId,string PlayerId,string OrganizationId)?> CurrentAsync(
                    Guid identity,string phone,string role)
                {
                    await using var command=new NpgsqlCommand("""
                        SELECT grant_id,player_id,organization_id
                        FROM identity.read_current_family_delegation(@identity,@phone,@role,@expires)
                        """,connection);
                    command.Parameters.AddWithValue("identity",identity);
                    command.Parameters.AddWithValue("phone",phone);
                    command.Parameters.AddWithValue("role",role);
                    command.Parameters.AddWithValue("expires",DateTimeOffset.UtcNow.AddMinutes(5));
                    await using var reader=await command.ExecuteReaderAsync();
                    if(!await reader.ReadAsync())return null;
                    var result=(reader.GetGuid(0),reader.GetString(1),reader.GetString(2));
                    Check.True(!await reader.ReadAsync(),"One fan saw overlapping active delegated grants.");
                    return result;
                }
                await using(var appDatabase=new LefteriaFc.Infrastructure.Persistence.LefteriaDbContext(
                    new DbContextOptionsBuilder<LefteriaFc.Infrastructure.Persistence.LefteriaDbContext>()
                        .UseNpgsql(database.AppConnectionStringForTest()).Options))
                {
                    var repository=new LefteriaFc.Infrastructure.Family.PostgresFamilyInvitationRepository(appDatabase);
                    var session=new LefteriaFc.Domain.Identity.MobileSession(
                        Guid.NewGuid(),recipientFan.Id,target.Digits,
                        LefteriaFc.Domain.Identity.MobileRole.Fan,DateTimeOffset.UtcNow.AddMinutes(5));
                    var snapshot=await repository.ReadCurrentDelegationAsync(session,CancellationToken.None);
                    Check.True(snapshot is not null && snapshot.PlayerId==child &&
                        snapshot.OrganizationId==OrganizationIds.Academy,
                        "The production repository failed to revalidate an accepted grant.");
                    Check.True(!(snapshot?.ToString() ?? string.Empty).Contains(child,StringComparison.Ordinal),
                        "Delegation logging exposed a private child identifier.");
                    var otherSession=new LefteriaFc.Domain.Identity.MobileSession(
                        Guid.NewGuid(),verifiedFan.Id,nextTarget.Digits,
                        LefteriaFc.Domain.Identity.MobileRole.Fan,DateTimeOffset.UtcNow.AddMinutes(5));
                    Check.True(await repository.ReadCurrentDelegationAsync(otherSession,CancellationToken.None) is null,
                        "A different recipient read the active grant through the production adapter.");
                }
                // This scoped authorization predicate must eventually be
                // inlined into each private calendar SELECT, never used as a
                // separate preflight followed by an unprotected query.
                async Task<bool> ScopeAsync(Guid identity,string phone,string role,
                    string player,string organization,DateTimeOffset? expiry=null)
                {
                    await using var command=new NpgsqlCommand("""
                        SELECT identity.current_family_calendar_scope(
                          @identity,@phone,@role,@expires,@player,@organization)
                        """,connection);
                    command.Parameters.AddWithValue("identity",identity);
                    command.Parameters.AddWithValue("phone",phone);
                    command.Parameters.AddWithValue("role",role);
                    command.Parameters.AddWithValue("expires",expiry ?? DateTimeOffset.UtcNow.AddMinutes(5));
                    command.Parameters.AddWithValue("player",player);
                    command.Parameters.AddWithValue("organization",organization);
                    return (bool)(await command.ExecuteScalarAsync() ?? false);
                }
                Check.True(await ScopeAsync(recipientFan.Id,target.Digits,"fan",child,OrganizationIds.Academy),
                    "Live delegated calendar scope rejected the valid accepted grant.");
                Check.True(!await ScopeAsync(recipientFan.Id,target.Digits,"fan",accounts.PlayerId,OrganizationIds.Academy),
                    "A delegated recipient gained access to an unrelated player.");
                Check.True(!await ScopeAsync(recipientFan.Id,target.Digits,"fan",child,OrganizationIds.FirstTeam),
                    "A delegated recipient crossed the child's organization scope.");
                Check.True(!await ScopeAsync(verifiedFan.Id,nextTarget.Digits,"fan",child,OrganizationIds.Academy),
                    "Another verified phone received the delegated player's calendar scope.");
                Check.True(!await ScopeAsync(recipientFan.Id,nextTarget.Digits,"fan",child,OrganizationIds.Academy),
                    "A forged session phone unlocked the delegated calendar scope.");
                Check.True(!await ScopeAsync(recipientFan.Id,target.Digits,"fan","",OrganizationIds.Academy),
                    "Empty player selector unlocked the delegated calendar scope.");
                try { await ScopeAsync(recipientFan.Id,target.Digits,"player",child,OrganizationIds.Academy);
                    throw new InvalidOperationException("A forged player-role claim unlocked delegated calendar scope."); }
                catch(PostgresException e) when(e.SqlState=="P0001" &&
                    e.MessageText=="family_invitation_denied") { }
                try { await ScopeAsync(recipientFan.Id,target.Digits,"fan",child,OrganizationIds.Academy,
                    DateTimeOffset.UtcNow.AddMinutes(-1));
                    throw new InvalidOperationException("Expired session unlocked delegated calendar scope."); }
                catch(PostgresException e) when(e.SqlState=="P0001" &&
                    e.MessageText=="family_invitation_denied") { }
                var current=await CurrentAsync(recipientFan.Id,target.Digits,"fan");
                Check.True(current.HasValue,"Accepted active grant was not revalidated.");
                Check.Equal(child,current!.Value.PlayerId,"Recipient grant returned another child.");
                Check.Equal(OrganizationIds.Academy,current.Value.OrganizationId,
                    "Recipient grant crossed the organization boundary.");
                Check.True((await CurrentAsync(verifiedFan.Id,nextTarget.Digits,"fan")) is null,
                    "A different OTP fan read the recipient's active grant.");
                try { await CurrentAsync(recipientFan.Id,target.Digits,"parent");
                    throw new InvalidOperationException("An unverified role read a private delegation."); }
                catch(PostgresException ex) when(ex.SqlState=="P0001" &&
                    ex.MessageText=="family_invitation_denied") { }
                await using(var context=database.CreateContext())
                    await context.MobileIdentities.Where(row=>row.Id==parentId)
                        .ExecuteUpdateAsync(setter=>setter.SetProperty(row=>row.IsActive,false));
                try {
                    Check.True((await CurrentAsync(recipientFan.Id,target.Digits,"fan")) is null,
                        "Deactivated guardian retained live delegated child access.");
                    Check.True(!await ScopeAsync(recipientFan.Id,target.Digits,"fan",child,OrganizationIds.Academy),
                        "A deactivated guardian still authorized delegated calendar access.");
                }
                finally {
                    await using var restore=database.CreateContext();
                    await restore.MobileIdentities.Where(row=>row.Id==parentId)
                        .ExecuteUpdateAsync(setter=>setter.SetProperty(row=>row.IsActive,true));
                }
                Check.True((await CurrentAsync(recipientFan.Id,target.Digits,"fan")).HasValue,
                    "Restored guardian did not recover the still-active grant.");
                var revokePath="/api/mobile/family/children/"+child+"/revoke";
                await CalendarRequestAsync(unauthenticated,HttpMethod.Post,revokePath,null,401);
                foreach(var otherRole in new[]{player,coach,management,fan,recipient})
                    await CalendarRequestAsync(otherRole,HttpMethod.Post,revokePath,null,403);
                // A guardian cannot revoke another child's player identity.
                await CalendarRequestAsync(parent,HttpMethod.Post,
                    "/api/mobile/family/children/"+accounts.PlayerId+"/revoke",null,404);
                var response=await CalendarRequestAsync(parent,HttpMethod.Post,revokePath,null,200);
                Check.True(response.GetProperty("ok").GetBoolean(),
                    "Parent API did not confirm the genuine delegated grant revocation.");
                await CalendarRequestAsync(parent,HttpMethod.Post,revokePath,null,404);
                Check.True((await CurrentAsync(recipientFan.Id,target.Digits,"fan")) is null,
                    "Revoked grant remained visible to the former recipient.");
                Check.True(!await ScopeAsync(recipientFan.Id,target.Digits,"fan",child,OrganizationIds.Academy),
                    "A revoked grant still authorized delegated calendar access.");
                var after=await CalendarRequestAsync(parent,HttpMethod.Get,activePath,null,200);
                Check.Equal(0,after.GetProperty("items").GetArrayLength(),
                    "Revoked delegation remained active in the parent's summary.");
                Check.True(!(await DelegationStatusAsync(recipient,200))
                    .GetProperty("has_active_delegation").GetBoolean(),
                    "Recipient status did not clear immediately after guardian revocation.");
                await using(var appDatabase=new LefteriaFc.Infrastructure.Persistence.LefteriaDbContext(
                    new DbContextOptionsBuilder<LefteriaFc.Infrastructure.Persistence.LefteriaDbContext>()
                        .UseNpgsql(database.AppConnectionStringForTest()).Options))
                {
                    var repository=new LefteriaFc.Infrastructure.Family.PostgresFamilyInvitationRepository(appDatabase);
                    var session=new LefteriaFc.Domain.Identity.MobileSession(
                        Guid.NewGuid(),recipientFan.Id,target.Digits,
                        LefteriaFc.Domain.Identity.MobileRole.Fan,DateTimeOffset.UtcNow.AddMinutes(5));
                    Check.True(await repository.ReadCurrentDelegationAsync(session,CancellationToken.None) is null,
                        "Production repository returned a grant after guardian revocation.");
                }

            }

            // Revoked acceptance is final; a stale fan token cannot replay
            // the old invitation to regain private family delegation.
            await CalendarRequestAsync(recipient,HttpMethod.Post,
                "/api/mobile/family/invitations/"+candidateId.ToString("D")+"/accept",null,404);
            Check.True(!(await DelegationStatusAsync(recipient,200))
                .GetProperty("has_active_delegation").GetBoolean(),
                "Stale acceptance replay restored a revoked delegation.");

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
                $@"DELETE FROM identity.family_delegated_player_grants WHERE player_id IN ({child},{second})");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM identity.family_invitation_events WHERE player_id IN ({child},{second})");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM identity.family_invitations WHERE player_id IN ({child},{second})");
            await context.SecurityAuditEvents.Where(row=>
                (row.ActorIdentityId==parentId && (row.Action=="mobile.family.invitation.issue" ||
                    row.Action=="mobile.family.invitation.cancel" || row.Action=="mobile.family.grant.revoke")) ||
                (row.ActorIdentityId==acceptedRecipientId && row.Action=="mobile.family.grant.accept")).ExecuteDeleteAsync();
            // This calendar test creates two independent OTP-verified fan
            // identities beyond the imported source manifest. Exact synthetic
            // phone cleanup is required before the next native identity suite.
            // Live users never run these fixture-only cleanup statements.
            var targetPhones = new[] { target.Digits, nextTarget.Digits };
            await context.OtpChallenges.Where(row =>
                targetPhones.Contains(row.NormalizedPhone)).ExecuteDeleteAsync();
            await context.MobileIdentities.Where(row =>
                targetPhones.Contains(row.NormalizedPhone)).ExecuteDeleteAsync();
            await database.CleanupFamilyThrottleAsync(parentId);
        }
    }
}
