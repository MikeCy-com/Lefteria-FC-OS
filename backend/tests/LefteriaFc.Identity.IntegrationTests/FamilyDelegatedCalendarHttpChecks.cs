using System.Text.Json;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task FamilyDelegatedCalendarChecksAsync(
        CalendarDatabaseFixture input,HttpClient parent,HttpClient player,HttpClient fan)
    {
        var target=database.NewPhone();
        var child=input.Accounts.ChildId;
        var parentId=input.Accounts.Plan.Mobiles.Single(x=>x.Role=="parent").Id;
        var get="/api/mobile/family/calendar?month=2026-10";
        await database.SeedIdentityAsync(target,MobileRole.Fan);
        using var recipient=Client();
        await LoginAsync(recipient,target,"fan");
        Guid? recipientId=null;

        try
        {
            using var anonymous=Client();
            await CalendarRequestAsync(anonymous,HttpMethod.Get,get,null,401);
            await CalendarRequestAsync(parent,HttpMethod.Get,get,null,403);
            await CalendarRequestAsync(player,HttpMethod.Get,get,null,403);
            await CalendarRequestAsync(fan,HttpMethod.Get,get,null,403);
            await CalendarRequestAsync(recipient,HttpMethod.Get,get,null,403);
            var rsvpPath="/api/mobile/family/availability";
            var hostEvent=input.Id("host");
            var delegatedRsvp=new{event_id=hostEvent,event_type="event",status="maybe"};
            await CalendarRequestAsync(recipient,HttpMethod.Post,rsvpPath,delegatedRsvp,403);
            await CalendarRequestAsync(parent,HttpMethod.Post,rsvpPath,delegatedRsvp,403);
            await CalendarRequestAsync(fan,HttpMethod.Post,rsvpPath,delegatedRsvp,403);
            await CalendarRequestAsync(recipient,HttpMethod.Get,get+"&player_id="+child,null,400);
            await CalendarRequestAsync(recipient,HttpMethod.Get,
                "/api/mobile/family/calendar?month=2026-10&month=2026-11",null,400);
            await CalendarRequestAsync(recipient,HttpMethod.Get,
                "/api/mobile/family/calendar?month=2026-13",null,400);

            var issued=await CalendarRequestAsync(parent,HttpMethod.Post,
                "/api/mobile/family/children/"+child+"/invitations",
                new{phone=target.WireValue},200);
            var invite=issued.GetProperty("id").GetGuid();
            var identity=(await database.IdentityAsync(target,MobileRole.Fan))!;
            recipientId=identity.Id;
            await CalendarRequestAsync(recipient,HttpMethod.Get,get,null,403);
            var accepted=await CalendarRequestAsync(recipient,HttpMethod.Post,
                "/api/mobile/family/invitations/"+invite.ToString("D")+"/accept",null,200);
            Check.True(accepted.GetProperty("ok").GetBoolean(),
                "Delegated calendar fixture did not accept the phone-verified invitation.");

            var own=await CalendarRequestAsync(parent,HttpMethod.Get,
                "/api/mobile/calendar?month=2026-10&player_id="+child,null,200);
            var delegated=await CalendarRequestAsync(recipient,HttpMethod.Get,get,null,200);
            Check.Equal("fan",delegated.GetProperty("role").GetString(),
                "Read-only family calendar elevated the fan's role.");
            Check.Equal(child,delegated.GetProperty("selected_player_id").GetString(),
                "Calendar selected a different child than the accepted delegation.");
            Check.Equal(OrganizationIds.Academy,
                delegated.GetProperty("organization_id").GetString(),
                "Delegated calendar returned another organization.");
            Check.Equal(1,delegated.GetProperty("players").GetArrayLength(),
                "Delegated calendar exposed a roster rather than its sole child.");
            Check.True(CalendarIds(delegated).SequenceEqual(CalendarIds(own)),
                "Delegated calendar diverged from the guardian's child-level member events.");
            // A now-revoked child calendar must not be recoverable from a
            // private/shared HTTP cache even if the browser retains its URL.
            using(var http=await recipient.GetAsync(get))
            {
                Check.Equal(200,(int)http.StatusCode,"Read-only family calendar HTTP GET failed.");
                Check.True(http.Headers.CacheControl?.NoStore==true &&
                    http.Headers.CacheControl.Private,
                    "Delegated child calendar was served with cacheable headers.");
            }
            foreach(var item in delegated.GetProperty("items").EnumerateArray())
            {
                Check.Equal(OrganizationIds.Academy,item.GetProperty("organization_id").GetString(),
                    "Delegation returned a calendar event from another organization.");
                Check.True(item.GetProperty("content_status").ValueKind==JsonValueKind.Null,
                    "Delegation leaked a staff-only content workflow status.");
                foreach(var response in item.GetProperty("responses").EnumerateArray())
                    Check.Equal(child,response.GetProperty("player_id").GetString(),
                        "Delegation exposed another child's response.");
            }

            // A delegated fan can only RSVP for their one current child;
            // no caller-selected player ID or role switch is accepted.
            var delegatedResult=await CalendarRequestAsync(recipient,HttpMethod.Post,
                rsvpPath,delegatedRsvp,200);
            Check.Equal(child,delegatedResult.GetProperty("player_id").GetString(),
                "Delegated RSVP updated a child other than the accepted grant.");
            Check.Equal("maybe",delegatedResult.GetProperty("status").GetString(),
                "Delegated RSVP returned the wrong server-confirmed status.");
            await CalendarRequestAsync(recipient,HttpMethod.Post,rsvpPath,
                new{event_id=hostEvent,event_type="event",status="maybe",player_id=child},400);
            await CalendarRequestAsync(recipient,HttpMethod.Post,rsvpPath,
                new{event_id=input.Id("outside"),event_type="event",status="going"},403);
            await CalendarRequestAsync(recipient,HttpMethod.Post,rsvpPath,
                new{event_id=hostEvent,event_type="event",status="invalid"},400);
            await using(var verified=database.CreateContext())
            {
                var stored=await verified.CalendarResponses.AsNoTracking()
                    .Where(x=>x.EventId==hostEvent && x.PlayerId==child &&
                        (x.Collection=="availability" || x.Collection=="event_attendance"))
                    .ToArrayAsync();
                Check.Equal(2,stored.Length,"Delegated RSVP did not synchronize both response collections.");
                Check.True(stored.All(x=>x.Status=="maybe" && x.ActorIdentityId==identity.Id),
                    "Delegated RSVP was not attributed to the verified fan or diverged between collections.");
                Check.Equal(1,await verified.SecurityAuditEvents.CountAsync(x=>
                    x.ActorIdentityId==identity.Id &&
                    x.Action=="mobile.calendar.availability" &&
                    x.ReasonCode=="delegated_family_rsvp"),
                    "Delegated RSVP did not append exactly one scoped success audit.");
            }

            // The original parent/player RSVP endpoint must stay inaccessible
            // even when this new fan-only read endpoint is authorized.
            await CalendarRequestAsync(recipient,HttpMethod.Post,"/api/mobile/availability",
                new{event_id=input.Id("host"),player_id=child,status="going"},403);
            await CalendarRequestAsync(recipient,HttpMethod.Get,
                "/api/mobile/calendar?month=2026-10",null,403);

            // Prove the protected SQL checks authority AFTER the service's
            // detached profile preflight. This is the revocation race that a
            // separate app-side grant check would fail.
            async Task FinalQueryRevocationAsync(Func<Task> revoke)
            {
                var gate=factory.CalendarGate.HoldNext();
                var pending=CalendarRequestAsync(recipient,HttpMethod.Get,get,null,403);
                try
                {
                    var entered=gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    if(await Task.WhenAny(entered,pending)==pending)
                    {
                        await pending;
                        throw new InvalidOperationException(
                            "Delegated calendar completed before the final SQL query gate.");
                    }
                    await entered;
                    await revoke();
                    gate.Release.TrySetResult();
                    var response=await pending;
                    Check.True(!response.TryGetProperty("items",out _) &&
                        !response.TryGetProperty("players",out _),
                        "Revocation race exposed cached child event or roster data.");
                }
                finally
                {
                    gate.Release.TrySetResult();
                    await pending;
                }
            }

            await FinalQueryRevocationAsync(async()=>
            {
                await using var context=database.CreateContext();
                await context.MobileIdentities.Where(x=>x.Id==parentId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(y=>y.IsActive,false));
            });
            await using(var restored=database.CreateContext())
                await restored.MobileIdentities.Where(x=>x.Id==parentId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(y=>y.IsActive,true));

            await using(var context=database.CreateContext())
                await context.MobileIdentities.Where(x=>x.Id==parentId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(y=>y.IsActive,false));
            try
            {
                await CalendarRequestAsync(recipient,HttpMethod.Get,get,null,403);
                await CalendarRequestAsync(recipient,HttpMethod.Post,rsvpPath,
                    new{event_id=hostEvent,event_type="event",status="going"},403);
            }
            finally
            {
                await using var context=database.CreateContext();
                await context.MobileIdentities.Where(x=>x.Id==parentId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(y=>y.IsActive,true));
            }
            Check.True(CalendarIds(await CalendarRequestAsync(recipient,HttpMethod.Get,get,null,200))
                .SequenceEqual(CalendarIds(own)),
                "Restored active guardian relationship did not recover the scoped member calendar.");

            // Revocation committed while the delegated request is parked
            // immediately before its final SQL data SELECT must deny events.
            await FinalQueryRevocationAsync(async()=>
            {
                await CalendarRequestAsync(parent,HttpMethod.Post,
                    "/api/mobile/family/children/"+child+"/revoke",null,200);
            });
            await CalendarRequestAsync(recipient,HttpMethod.Get,get,null,403);
            await CalendarRequestAsync(recipient,HttpMethod.Post,rsvpPath,
                new{event_id=hostEvent,event_type="event",status="going"},403);
            Check.True(!(await CalendarRequestAsync(recipient,HttpMethod.Get,
                "/api/mobile/family/active-delegation",null,200))
                .GetProperty("has_active_delegation").GetBoolean(),
                "Revocation left the previous recipient's delegated calendar enabled.");
        }
        finally
        {
            await using var context=database.CreateContext();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM identity.family_delegated_player_grants WHERE player_id={child}");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM identity.family_invitation_events WHERE player_id={child}");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM identity.family_invitations WHERE player_id={child}");
            await context.SecurityAuditEvents.Where(x=>
                (x.ActorIdentityId==parentId && (x.Action=="mobile.family.invitation.issue"
                    || x.Action=="mobile.family.grant.revoke")) ||
                (x.ActorIdentityId==recipientId && (x.Action=="mobile.family.grant.accept" ||
                    x.Action=="mobile.calendar.availability")))
                .ExecuteDeleteAsync();
            await context.OtpChallenges.Where(x=>x.NormalizedPhone==target.Digits)
                .ExecuteDeleteAsync();
            await context.MobileIdentities.Where(x=>x.NormalizedPhone==target.Digits)
                .ExecuteDeleteAsync();
            await database.CleanupFamilyThrottleAsync(parentId);
        }
    }
}
