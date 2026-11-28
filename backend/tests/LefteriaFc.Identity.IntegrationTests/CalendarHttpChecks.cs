using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.CalendarImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using LefteriaFc.Infrastructure.ClubCalendar;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task CalendarEndToEndAsync()
    {
        var accounts=AccountImportFixture.Create(database,calendarScenario:true);
        await using(var initial=database.CreateContext())
            Check.True(!await initial.MobileIdentities.AnyAsync()&&!await initial.AdminAccounts.AnyAsync()&&!await initial.RosterImportStates.AnyAsync()&&
                !await initial.StaffRelationshipImportStates.AnyAsync()&&!await initial.CalendarImportStates.AnyAsync()&&!await initial.CalendarItems.AnyAsync(),
                "Calendar end-to-end scenario must run first against the empty guarded fixture.");
        try
        {
            await database.AssertCalendarEmptyMigrationRecoveryAsync();
            await database.ApplyRosterAsync(accounts.Roster);await database.ApplyStaffAsync(accounts.Staff);await database.ApplyAccountsAsync(accounts);
            var input=CalendarDatabaseFixture.Create(accounts);
            Check.Equal(0,input.Plan.QuarantinedRows,"Clean calendar fixture unexpectedly quarantined source rows.");
            using var player=Client();using var parent=Client();using var coach=Client();using var management=Client();using var fan=Client();
            await LoginAsync(player,accounts.PlayerPhone,"player");await LoginAsync(parent,accounts.ParentPhone,"parent");
            await LoginAsync(coach,accounts.StaffPhone,"coach");await LoginAsync(management,accounts.StaffPhone,"management");await LoginAsync(fan,accounts.PlayerPhone,"fan");
            // Every /api/mobile response, including a failed authorization,
            // contains private auth context and must never be cached by an
            // intermediary, browser history, or a shared proxy.
            using(var authorized=await parent.GetAsync("/api/mobile/auth/me"))
            {
                Check.Equal(200,(int)authorized.StatusCode,"Mobile profile cache-policy probe failed.");
                Check.True(authorized.Headers.CacheControl?.Private==true &&
                    authorized.Headers.CacheControl.NoStore &&
                    authorized.Headers.Pragma.Any(x=>x.Name=="no-cache"),
                    "Authorized mobile profile did not prohibit caching.");
            }
            using(var rejected=await fan.GetAsync("/api/mobile/calendar?month=2026-10"))
            {
                Check.Equal(403,(int)rejected.StatusCode,"Mobile role denial cache-policy probe failed.");
                Check.True(rejected.Headers.CacheControl?.Private==true &&
                    rejected.Headers.CacheControl.NoStore,
                    "Denied mobile calendar response could be cached.");
            }

            await FamilyReadChecksAsync(accounts,parent,player,coach,management,fan);
            await FamilyInvitationChecksAsync(accounts,parent,player,coach,management,fan);
            await CalendarRequestAsync(player,HttpMethod.Get,"/api/mobile/calendar?month=2026-10",null,503);
            await CalendarReadinessChecksAsync(input,player);
            await CalendarOwnerChecksAsync(input);
            await CalendarScopeChecksAsync(input,player,parent,coach,management,fan);
            await CalendarRoleSwitchBoundaryChecksAsync(input);
            await CalendarAvailabilityChecksAsync(input,player,parent,coach,management);
            await CalendarFinalBindingChecksAsync(input,player,parent,coach,management);
            await FamilyDelegatedCalendarChecksAsync(input,parent,player,fan);
            await database.AssertCalendarRuntimePermissionsAsync(input);
            await MatchWorkspaceHttpChecksAsync(input,player,parent,coach,management,fan);
        }
        finally
        {
            await database.CleanupCalendarImportsAsync();await database.CleanupAccountImportsAsync();
            await database.CleanupStaffImportAsync(accounts.Staff);await database.CleanupRosterImportAsync(accounts.Roster);
        }
        await using var cleaned=database.CreateContext();
        Check.True(!await cleaned.CalendarImportStates.AnyAsync()&&!await cleaned.CalendarItems.AnyAsync()&&!await cleaned.CalendarSourceDocuments.AnyAsync()&&
            !await cleaned.CalendarResponses.AnyAsync()&&!await cleaned.MobileIdentities.AnyAsync()&&!await cleaned.AdminAccounts.AnyAsync(),
            "Exact calendar cleanup left imported data before subsequent native-auth scenarios.");
    }

    private async Task MatchWorkspaceHttpChecksAsync(CalendarDatabaseFixture input,HttpClient player,
        HttpClient parent,HttpClient coach,HttpClient management,HttpClient fan)
    {
        string Path(string suffix)=>"/api/mobile/calendar/match/"+input.Id(suffix)+"/workspace";
        var first=Path("rotation-one");var next=Path("rotation-two");
        var child=input.Accounts.ChildId;var guest=input.SecondChild;
        var chosen=new[]{new{player_id=child,assignment="starter",position=(string?)null},
            new{player_id=guest,assignment="reserve",position=(string?)null}};
        var rotated=new[]{new{player_id=child,assignment="reserve",position=(string?)null},
            new{player_id=guest,assignment="starter",position=(string?)null}};
        object Squad(int version,object rows,string note="")=>new
        {action="select_squad",expected_version=version,squad=rows,statistics=(object?)null,
            rotation_note=note,review_reason=(string?)null};
        await using var db=database.CreateContext();
        try
        {
            await CalendarRequestAsync(parent,HttpMethod.Get,first,null,403);
            await CalendarRequestAsync(player,HttpMethod.Get,first,null,403);
            await CalendarRequestAsync(fan,HttpMethod.Get,first,null,403);
            await CalendarRequestAsync(coach,HttpMethod.Get,Path("rotation-outside"),null,403);
            var view=await CalendarRequestAsync(coach,HttpMethod.Get,first,null,200);
            Check.Equal("grassroots",view.GetProperty("mode").GetString(),"Academy fixture used First Team mode.");
            Check.True(view.GetProperty("roster").EnumerateArray().Any(p=>p.GetProperty("player_id").GetString()==guest),
                "Explicit current same-organization guest missing from host match roster.");
            Check.True(view.GetProperty("roster").EnumerateArray().All(p=>p.GetProperty("position").ValueKind==JsonValueKind.Null),
                "Academy roster exposed permanent playing positions.");
            await CalendarRequestAsync(coach,HttpMethod.Post,first,Squad(0,new[]{new{player_id=child,assignment="starter",position="GK"}}),422);
            await CalendarRequestAsync(coach,HttpMethod.Post,first,Squad(0,new[]{chosen[0],chosen[0]}),422);
            await CalendarRequestAsync(coach,HttpMethod.Post,first,Squad(0,new[]{new{player_id=input.FcChild,assignment="starter",position=(string?)null}}),422);
            var saved=await CalendarRequestAsync(coach,HttpMethod.Post,first,Squad(0,chosen),200);
            Check.Equal(1,saved.GetProperty("version").GetInt32(),"First selection was not persisted.");
            Check.Equal("selected",saved.GetProperty("state").GetString(),"Selection was not confirmed.");
            await CalendarRequestAsync(coach,HttpMethod.Post,first,Squad(0,chosen),409);
            var persisted=await CalendarRequestAsync(coach,HttpMethod.Get,first,null,200);
            Check.Equal(2,persisted.GetProperty("squad").GetArrayLength(),"Reload lost saved squad.");
            var nextView=await CalendarRequestAsync(coach,HttpMethod.Get,next,null,200);
            var priorReserve=nextView.GetProperty("roster").EnumerateArray().Single(p=>p.GetProperty("player_id").GetString()==guest);
            Check.Equal("reserve",priorReserve.GetProperty("last_assignment").GetString(),"Previous confirmed reserve not projected.");
            await CalendarRequestAsync(coach,HttpMethod.Post,next,Squad(0,chosen),422);
            var changed=await CalendarRequestAsync(coach,HttpMethod.Post,next,Squad(0,rotated),200);
            Check.Equal("starter",changed.GetProperty("squad").EnumerateArray().Single(p=>p.GetProperty("player_id").GetString()==guest).GetProperty("assignment").GetString(),
                "Next match did not persist rotated starter.");
            object Attendance(int version,params string[] ids)=>new{action="confirm_attendance",expected_version=version,
                squad=(object?)null,statistics=(object?)null,rotation_note=(string?)null,review_reason=(string?)null,attendance_ids=ids};
            await CalendarRequestAsync(parent,HttpMethod.Post,first,Attendance(1,child),403);
            var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Asia/Nicosia").DateTime);
            // Set a synthetic fixture to today's date only inside this isolated test.
            await db.CalendarItems.Where(row=>row.Kind=="match"&&row.Id==input.Id("rotation-one"))
                .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.StaffDay,today).SetProperty(row=>row.StaffEndDay,today));
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO club.group_match_timing(organization_id,host_id,duration_minutes,players_on_field,version,updated_by) SELECT 'lefteria_fc_academy',{input.Accounts.GroupId},45,7,1,id FROM identity.admin_accounts WHERE username={input.Accounts.Prefix+"-user"}");
            var inherited=await CalendarRequestAsync(coach,HttpMethod.Get,first,null,200);
            Check.Equal(45,inherited.GetProperty("timing").GetProperty("duration_minutes").GetInt32(),"Group duration not inherited.");
            Check.Equal(7,inherited.GetProperty("timing").GetProperty("players_on_field").GetInt32(),"Group format not inherited.");
            Check.True(inherited.GetProperty("timing").GetProperty("attendance_ids").ValueKind==JsonValueKind.Null,
                "Attendance was fabricated from the selection.");
            await CalendarRequestAsync(coach,HttpMethod.Post,first,Attendance(1,child,child),422);
            await CalendarRequestAsync(coach,HttpMethod.Post,first,Attendance(1,input.FcChild),422);
            var attendance=await CalendarRequestAsync(coach,HttpMethod.Post,first,Attendance(1,child),200);
            Check.Equal(2,attendance.GetProperty("version").GetInt32(),"Attendance not versioned.");
            Check.True(attendance.GetProperty("timing").GetProperty("frozen").GetBoolean(),"Timing was not frozen on match day.");
            Check.Equal(1,attendance.GetProperty("timing").GetProperty("attendance_ids").GetArrayLength(),"Actual attendance not persisted.");
            await CalendarRequestAsync(coach,HttpMethod.Post,first,Attendance(1,child),409);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE club.group_match_timing SET duration_minutes=60,players_on_field=9,version=2 WHERE organization_id='lefteria_fc_academy' AND host_id={input.Accounts.GroupId}");
            var frozen=await CalendarRequestAsync(coach,HttpMethod.Get,first,null,200);
            Check.Equal(45,frozen.GetProperty("timing").GetProperty("duration_minutes").GetInt32(),"Later group settings changed the match snapshot.");
            var corrected=await CalendarRequestAsync(coach,HttpMethod.Post,first,Attendance(2,child,guest),200);
            Check.Equal(3,corrected.GetProperty("version").GetInt32(),"Attendance correction not persisted.");
            Check.Equal(7,corrected.GetProperty("timing").GetProperty("players_on_field").GetInt32(),"Attendance correction changed frozen format.");
            object Stats(int version)=>new{action="submit_statistics",expected_version=version,squad=(object?)null,
                statistics=new[]{new{player_id=child,minutes_played=20,goals=0,assists=0},
                    new{player_id=guest,minutes_played=20,goals=1,assists=0}},rotation_note=(string?)null,review_reason=(string?)null};
            await CalendarRequestAsync(coach,HttpMethod.Post,first,Stats(3),409);
            await db.CalendarItems.Where(row=>row.Kind=="match"&&row.Id==input.Id("rotation-one"))
                .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.Status,"completed"));
            var submitted=await CalendarRequestAsync(coach,HttpMethod.Post,first,Stats(3),200);
            Check.Equal("submitted",submitted.GetProperty("state").GetString(),"Coach statistics were not submitted for review.");
            Check.Equal(2,submitted.GetProperty("statistics").GetArrayLength(),"Statistics were not persisted.");
            var approve=new{action="approve",expected_version=4,squad=(object?)null,statistics=(object?)null,
                rotation_note=(string?)null,review_reason=(string?)null};
            await CalendarRequestAsync(coach,HttpMethod.Post,first,approve,403);
            await CalendarRequestAsync(management,HttpMethod.Post,first,approve,403); // same verified phone; no self-review
            await CalendarRequestAsync(coach,HttpMethod.Post,first,Stats(1),409);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM club.match_workspace_revisions WHERE match_id IN ({input.Id("rotation-one")},{input.Id("rotation-two")})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM club.match_workspaces WHERE match_id IN ({input.Id("rotation-one")},{input.Id("rotation-two")})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM club.group_match_timing WHERE organization_id='lefteria_fc_academy' AND host_id={input.Accounts.GroupId}");
            await db.CalendarItems.Where(row=>row.Kind=="match"&&row.Id==input.Id("rotation-one"))
                .ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.Status,"scheduled")
                    .SetProperty(row=>row.StaffDay,new DateOnly(2027,1,3)).SetProperty(row=>row.StaffEndDay,new DateOnly(2027,1,3)));
        }
    }

    private async Task CalendarReadinessChecksAsync(CalendarDatabaseFixture input,HttpClient player)
    {
        var empty=input.Input.DeepClone().AsObject();
        foreach(var collection in new[]{"fixtures","events","training_sessions","availability","event_attendance","attendance"})empty[collection]=new JsonArray();
        var verifiedEmpty=CalendarDatabaseFixture.Create(input.Accounts,empty);
        await database.ApplyCalendarAsync(verifiedEmpty);
        try
        {
            var page=await CalendarRequestAsync(player,HttpMethod.Get,"/api/mobile/calendar?month=2026-10",null,200);
            Check.Equal(0,page.GetProperty("items").GetArrayLength(),"A verified empty calendar fabricated events.");
            var own=await CalendarRequestAsync(player,HttpMethod.Get,"/api/mobile/my-availability",null,200);
            Check.True(own.ValueKind==JsonValueKind.Array&&own.GetArrayLength()==0,"Verified empty own availability was not a direct empty array.");
            await ExpectCalendarRefusalAsync(input,"calendar_initial_snapshot_already_applied");
        }
        finally{await database.CleanupCalendarImportsAsync();}
        var quarantine=empty.DeepClone().AsObject();
        quarantine["events"]!.AsArray().Add(new JsonObject{["id"]=input.Id("quarantined"),["title"]="PRIVATE_QUARANTINE_MARKER",["date"]="invalid"});
        var incomplete=CalendarDatabaseFixture.Create(input.Accounts,quarantine);
        Check.Equal(1,incomplete.Plan.QuarantinedRows,"Incomplete fixture did not quarantine its invalid schedule.");
        await database.ApplyCalendarAsync(incomplete);
        try
        {
            await using var context=database.CreateContext();
            Check.True(!(await context.CalendarImportStates.AsNoTracking().SingleAsync()).IsComplete&&
                await context.CalendarSourceDocuments.CountAsync(row=>row.Disposition=="quarantined")==1&&await context.CalendarImportIssues.AnyAsync(),
                "Quarantined source was discarded or marked API-ready.");
            var unavailable=await CalendarRequestAsync(player,HttpMethod.Get,"/api/mobile/calendar?month=2026-10",null,503);
            Check.True(!unavailable.GetRawText().Contains("PRIVATE_QUARANTINE_MARKER",StringComparison.Ordinal),"Unavailable calendar exposed quarantined source.");
            await CalendarRequestAsync(player,HttpMethod.Get,"/api/mobile/my-availability",null,503);
        }
        finally{await database.CleanupCalendarImportsAsync();}
    }

    private async Task CalendarOwnerChecksAsync(CalendarDatabaseFixture input)
    {
        var review=await database.ApplyCalendarAsync(input,reviewOnly:true);
        Check.True(review.Reviewed&&!review.Applied&&!review.Noop,"Calendar review reported a mutation.");
        await using var context=database.CreateContext();
        Check.True(!await context.CalendarImportStates.AnyAsync()&&!await context.CalendarItems.AnyAsync()&&!await context.CalendarSourceDocuments.AnyAsync()&&
            !await context.SecurityAuditEvents.AnyAsync(row=>row.SubjectDigest==input.Manifest.Digest),"Calendar review wrote snapshot, source or audit data.");
        await ExpectCalendarRefusalAsync(input,"digest_mismatch",expectedDigest:new string('0',64));
        await ExpectCalendarRefusalAsync(input,"owner_target_required",runtime:true);
        await ExpectCalendarRefusalAsync(input,"environment_not_allowed",environment:"production");
        await ExpectCalendarRefusalAsync(input,"database_not_allowed",expectedDatabase:"postgres");
        var wrongBase=input.Input.DeepClone().AsObject();wrongBase["roster_sha256"]=new string('a',64);
        await ExpectCalendarRefusalAsync(input,"roster_digest_mismatch",manifest:CalendarImportValidator.Validate(RosterBytes(wrongBase)));
        wrongBase["roster_sha256"]=input.Accounts.Roster.Digest;wrongBase["source_commit"]=new string('a',40);
        await ExpectCalendarRefusalAsync(input,"source_commit_mismatch",manifest:CalendarImportValidator.Validate(RosterBytes(wrongBase)));

        var probe="calendar_failure_"+Guid.NewGuid().ToString("N");
        var create="CREATE FUNCTION migration.PROBE() RETURNS trigger LANGUAGE plpgsql AS $failure$ BEGIN IF NEW.import_digest='DIGEST' THEN RAISE EXCEPTION 'Isolated calendar archive failure'; END IF; RETURN NEW; END; $failure$; CREATE TRIGGER PROBE BEFORE INSERT ON migration.calendar_source_documents FOR EACH ROW EXECUTE FUNCTION migration.PROBE();"
            .Replace("PROBE",probe,StringComparison.Ordinal).Replace("DIGEST",input.Manifest.Digest,StringComparison.Ordinal);
        await context.Database.ExecuteSqlRawAsync(create);
        try
        {
            var failed=false;try{await database.ApplyCalendarAsync(input);}catch(DbUpdateException){failed=true;}
            Check.True(failed,"Injected calendar source archive failure did not abort owner apply.");
            Check.True(!await context.CalendarImportStates.AnyAsync()&&!await context.CalendarItems.AnyAsync()&&!await context.CalendarItemScopes.AnyAsync()&&
                !await context.CalendarItemGuests.AnyAsync()&&!await context.CalendarResponses.AnyAsync()&&!await context.CalendarSourceDocuments.AnyAsync()&&!await context.CalendarImportIssues.AnyAsync()&&
                !await context.SecurityAuditEvents.AnyAsync(row=>row.SubjectDigest==input.Manifest.Digest),"Failed calendar apply left partial typed, source or audit rows.");
        }
        finally{await context.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS PROBE ON migration.calendar_source_documents; DROP FUNCTION IF EXISTS migration.PROBE();".Replace("PROBE",probe,StringComparison.Ordinal));}
        var concurrent=await Task.WhenAll(database.ApplyCalendarAsync(input),database.ApplyCalendarAsync(input));
        Check.Equal(1,concurrent.Count(row=>row.Applied),"Concurrent calendar apply wrote twice.");
        Check.Equal(1,concurrent.Count(row=>row.Noop),"Concurrent exact calendar replay was not a no-op.");
        Check.True((await database.ApplyCalendarAsync(input)).Noop,"Seven-digit calendar source timestamps did not replay after PostgreSQL precision.");
        var state=await context.CalendarImportStates.AsNoTracking().SingleAsync();
        Check.True(state.IsComplete&&state.ExportedAt.Ticks%10==0&&state.ManifestBytes.AsSpan().SequenceEqual(input.Manifest.CopySourceBytes()),
            "Calendar readiness, microsecond timestamp or exact manifest bytes changed.");
        Check.Equal(input.Manifest.Rows.Count,await context.CalendarSourceDocuments.CountAsync(),"Calendar raw source archive lost a collection or source row.");
        var item=await context.CalendarItems.AsNoTracking().SingleAsync(row=>row.Kind=="event"&&row.Id==input.Id("host"));
        var source=await context.CalendarSourceDocuments.AsNoTracking().FirstAsync(row=>row.Collection=="events");
        await RejectMutationAsync(
            ()=>context.CalendarItems.Where(row=>row.Kind==item.Kind&&row.Id==item.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.StaffProjectionJson,item.StaffProjectionJson.Replace("Fictional host","Changed title",StringComparison.Ordinal))),
            ()=>context.CalendarItems.Where(row=>row.Kind==item.Kind&&row.Id==item.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.StaffProjectionJson,item.StaffProjectionJson)));
        await RejectMutationAsync(
            ()=>context.CalendarItemScopes.Where(row=>row.EventId==input.Id("host")).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.RecordId,input.SecondGroup).SetProperty(row=>row.AcademyGroupId,input.SecondGroup)),
            ()=>context.CalendarItemScopes.Where(row=>row.EventId==input.Id("host")).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.RecordId,input.Accounts.GroupId).SetProperty(row=>row.AcademyGroupId,input.Accounts.GroupId)));
        await RejectMutationAsync(
            ()=>context.CalendarItemGuests.Where(row=>row.EventId==input.Id("host")).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.PlayerId,input.Accounts.ChildId)),
            ()=>context.CalendarItemGuests.Where(row=>row.EventId==input.Id("host")).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.PlayerId,input.SecondChild)));
        await RejectMutationAsync(
            ()=>context.CalendarResponses.Where(row=>row.Collection=="availability").ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.Status,"maybe")),
            ()=>context.CalendarResponses.Where(row=>row.Collection=="availability").ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.Status,"going")));
        await RejectMutationAsync(
            ()=>context.CalendarSourceDocuments.Where(row=>row.Collection==source.Collection&&row.Ordinal==source.Ordinal).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.SourceJson," "+source.SourceJson)),
            ()=>context.CalendarSourceDocuments.Where(row=>row.Collection==source.Collection&&row.Ordinal==source.Ordinal).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.SourceJson,source.SourceJson)));
        var changedBytes=state.ManifestBytes.ToArray();changedBytes[0]=(byte)' ';
        await RejectMutationAsync(
            ()=>context.CalendarImportStates.ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.ManifestBytes,changedBytes)),
            ()=>context.CalendarImportStates.ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.ManifestBytes,state.ManifestBytes)));
        Check.True((await database.ApplyCalendarAsync(input)).Noop,"Restored calendar target did not reconcile exactly.");
        await database.AssertCalendarRollbackRefusedAsync(input);
        var replacement=input.Input.DeepClone().AsObject();replacement["events"]![0]!["title"]="Forbidden replacement";
        await ExpectCalendarRefusalAsync(input,"calendar_initial_snapshot_already_applied",manifest:CalendarImportValidator.Validate(RosterBytes(replacement)));
        Check.Equal(1,await context.SecurityAuditEvents.CountAsync(row=>row.Action=="calendar.import"&&row.SubjectDigest==input.Manifest.Digest),"Exact replay duplicated calendar import audit.");
        AssertCalendarSafe(JsonSerializer.Serialize(concurrent[0]),input);
        async Task RejectMutationAsync(Func<Task<int>> mutate,Func<Task<int>> restore)
        {await mutate();try{await ExpectCalendarRefusalAsync(input,"calendar_target_reconciliation_failed");}finally{await restore();}}
    }

    private async Task CalendarScopeChecksAsync(CalendarDatabaseFixture input,HttpClient player,HttpClient parent,HttpClient coach,HttpClient management,HttpClient fan)
    {
        var path="/api/mobile/calendar?month=2026-10";
        var playerMonth=await CalendarRequestAsync(player,HttpMethod.Get,path,null,200);
        Check.True(CalendarIds(playerMonth).Contains(input.Id("shared"))&&!CalendarIds(playerMonth).Contains(input.Id("host")),"Player calendar crossed organization/group boundaries.");
        var parentMonth=await CalendarRequestAsync(parent,HttpMethod.Get,path,null,200);
        Check.Equal(3,parentMonth.GetProperty("players").GetArrayLength(),"Parent child chooser lost a second child or FC child.");
        Check.Equal(input.Accounts.ChildId,parentMonth.GetProperty("selected_player_id").GetString(),"Parent default child did not retain canonical primary binding.");
        var published=parentMonth.GetProperty("items").EnumerateArray().Single(row=>row.GetProperty("id").GetString()==input.Id("published"));
        Check.True(published.GetProperty("title").GetString()=="Approved frozen training"&&published.GetProperty("date").GetString()=="2026-10-30"&&
            published.GetProperty("member_summary").GetString()=="Approved summary"&&published.GetProperty("preparation").GetString()=="Approved preparation", "Member training did not use the frozen publication.");
        Check.True(!CalendarIds(parentMonth).Contains(input.Id("draft"))&&!CalendarIds(parentMonth).Contains(input.Id("archived")),"Unpublished or archived item was exposed to a member.");
        var second=await CalendarRequestAsync(parent,HttpMethod.Get,path+"&player_id="+input.SecondChild,null,200);
        Check.True(CalendarIds(second).Contains(input.Id("group-two"))&&!CalendarIds(second).Contains(input.Id("published")),"Second child selection used the first child's group.");
        var fc=await CalendarRequestAsync(parent,HttpMethod.Get,path+"&player_id="+input.FcChild,null,200);
        Check.Equal(OrganizationIds.Club,fc.GetProperty("organization_id").GetString(),"Parent FC child did not select its own organization.");
        Check.True(CalendarIds(fc).Contains(input.Id("shared"))&&!CalendarIds(fc).Contains(input.Id("host")),"Parent FC selection unioned Academy data.");
        await CalendarRequestAsync(parent,HttpMethod.Get,path+"&player_id="+input.Accounts.PlayerId,null,403);
        await CalendarRequestAsync(player,HttpMethod.Get,path+"&player_id="+input.Accounts.ChildId,null,403);
        await CalendarRequestAsync(parent,HttpMethod.Get,path+"&player_id=%20",null,400);
        var coachMonth=await CalendarRequestAsync(coach,HttpMethod.Get,path,null,200);
        Check.True(CalendarIds(coachMonth).Contains(input.Id("shared"))&&CalendarIds(coachMonth).Contains(input.Id("host"))&&CalendarIds(coachMonth).Contains(input.Id("outside"))&&
            CalendarIds(coachMonth).Contains(input.Id("draft"))&&!CalendarIds(coachMonth).Contains(input.Id("group-two")),"Coach calendar lost full current assignments or gained an unrelated group.");
        var coachNovember=await CalendarRequestAsync(coach,HttpMethod.Get,"/api/mobile/calendar?month=2026-11",null,200);
        var draft=coachNovember.GetProperty("items").EnumerateArray().Single(row=>row.GetProperty("id").GetString()==input.Id("published"));
        Check.Equal("Staff draft training title",draft.GetProperty("title").GetString(),"Staff calendar did not use current draft date/title.");
        Check.True(!CalendarIds(await CalendarRequestAsync(parent,HttpMethod.Get,"/api/mobile/calendar?month=2026-11",null,200)).Contains(input.Id("published")),"Member month filtering used draft date instead of publication date.");
        var club=await CalendarRequestAsync(management,HttpMethod.Get,path,null,200);
        Check.True(CalendarIds(club).Contains(input.Id("shared"))&&!CalendarIds(club).Contains(input.Id("host")),"Management calendar unioned its multiple organization grants.");
        var unsigned=await CalendarRequestAsync(management,HttpMethod.Get,path+"&organization_id="+OrganizationIds.Academy+"&role=management",null,200);
        Check.True(CalendarIds(unsigned).SequenceEqual(CalendarIds(club)),"Unsigned organization selector changed management authority.");
        await PostAsync(management,"/api/mobile/management/organization",new{organization_id=OrganizationIds.Academy},200);
        var academy=await CalendarRequestAsync(management,HttpMethod.Get,path,null,200);
        Check.True(CalendarIds(academy).Contains(input.Id("host"))&&CalendarIds(academy).Contains(input.Id("group-two"))&&!CalendarIds(academy).Contains(input.Id("shared")),"Management organization selection was ignored.");
        await CalendarRequestAsync(management,HttpMethod.Get,path+"&player_id="+input.Accounts.ChildId,null,403);
        foreach(var invalid in new[]{"", "2026-1","2026-13","9999-12","2026-10&month=2026-11","2026-10&offset=-1","2026-10&offset=100001","2026-10&offset=1&offset=2"})
            await CalendarRequestAsync(parent,HttpMethod.Get,"/api/mobile/calendar?month="+invalid,null,400);
        await CalendarRequestAsync(fan,HttpMethod.Get,path,null,403);
        using var anonymous=Client();await CalendarRequestAsync(anonymous,HttpMethod.Get,path,null,401);
        using var wrongRole=Client();wrongRole.DefaultRequestHeaders.Authorization=new("Bearer",SignedToken(input.Accounts.Plan.Mobiles.Single(row=>row.Role=="player").Id,
            input.Accounts.PlayerPhone,IdentityApplicationFactory.Issuer,IdentityApplicationFactory.Audience,"mobile_access","coach",false));
        // A signed token whose role disagrees with its persisted identity is an
        // invalid session, before calendar role authorization is evaluated.
        var wrongRoleResult=await CalendarRequestAsync(wrongRole,HttpMethod.Get,path,null,401);
        Check.Equal("session_invalid",wrongRoleResult.GetProperty("code").GetString(),"Mismatched role claims were not rejected as an invalid session.");
        var first=await CalendarRequestAsync(parent,HttpMethod.Get,"/api/mobile/calendar?month=2026-12",null,200);
        var last=await CalendarRequestAsync(parent,HttpMethod.Get,"/api/mobile/calendar?month=2026-12&offset=200",null,200);
        Check.True(first.GetProperty("items").GetArrayLength()==200&&first.GetProperty("next_offset").GetInt32()==200&&last.GetProperty("items").GetArrayLength()==1&&
            last.GetProperty("next_offset").ValueKind==JsonValueKind.Null&&CalendarIds(first).Concat(CalendarIds(last)).Distinct().Count()==201,"Calendar paging lost or duplicated the 201st item.");
        foreach(var body in new[]{playerMonth,parentMonth,second,fc,coachMonth,coachNovember,club,academy,first,last})AssertCalendarSafe(body.GetRawText(),input);
    }

    private async Task CalendarAvailabilityChecksAsync(CalendarDatabaseFixture input,HttpClient player,HttpClient parent,HttpClient coach,HttpClient management)
    {
        const string path="/api/mobile/availability";
        var own=await CalendarRequestAsync(parent,HttpMethod.Post,path,new{event_id=input.Id("host")},200);
        Check.True(own.GetProperty("status").GetString()=="going"&&own.GetProperty("player_id").GetString()==input.Accounts.ChildId,"Omitted RSVP status/default player did not preserve the legacy contract.");
        await CalendarRequestAsync(parent,HttpMethod.Post,path,new{event_id=input.Id("host"),player_id=input.Accounts.ChildId,status="maybe"},200);
        await CalendarRequestAsync(parent,HttpMethod.Post,path,new{event_id=input.Id("group-two"),player_id=input.SecondChild,status="not_going"},200);
        await CalendarRequestAsync(parent,HttpMethod.Post,path,new{event_id=input.Id("past"),player_id=input.Accounts.ChildId,status="going"},200);
        await CalendarRequestAsync(parent,HttpMethod.Post,path,new{event_id=input.Id("published"),status="going"},200);
        var alias=await CalendarRequestAsync(player,HttpMethod.Post,path,new{event_id=input.Id("shared"),event_type="fixture",status="maybe"},200);
        Check.Equal("match",alias.GetProperty("event_type").GetString(),"Fixture alias was not canonicalized to match.");
        await CalendarRequestAsync(player,HttpMethod.Post,path,new{event_id=input.Id("shared"),status="going"},400);
        await CalendarRequestAsync(player,HttpMethod.Post,path,new{event_id=input.Id("shared"),event_type="event",status="going"},200);
        foreach(var status in new object?[]{null,"", "present","absent","no_response","Going",true,42})
            await CalendarRequestAsync(parent,HttpMethod.Post,path,new Dictionary<string,object?>{{"event_id",input.Id("host")},{"status",status}},400);
        foreach(var suffix in new[]{"cancelled","archived","draft"})
            await CalendarRequestAsync(parent,HttpMethod.Post,path,new{event_id=input.Id(suffix),status="going"},403);
        await CalendarRequestAsync(parent,HttpMethod.Post,path,new{event_id=input.Id("host"),player_id=input.Accounts.PlayerId,status="going"},403);
        await CalendarRequestAsync(player,HttpMethod.Post,path,new{event_id=input.Id("host"),status="going",organization_id=OrganizationIds.Academy,role="parent",linked_player_ids=new[]{input.Accounts.ChildId}},403);
        await CalendarRequestAsync(coach,HttpMethod.Post,path,new{event_id=input.Id("host"),player_id=input.Accounts.ChildId,status="going"},403);
        await CalendarRequestAsync(management,HttpMethod.Post,path,new{event_id=input.Id("host"),player_id=input.Accounts.ChildId,status="going"},403);
        await CalendarRequestAsync(parent,HttpMethod.Post,path,new{event_id=input.Id("missing"),status="going"},404);
        await CalendarRawRequestAsync(parent,path,"{\"event_id\":\""+input.Id("host")+"\",\"status\":\"going\",\"status\":\"maybe\"}",400);
        await CalendarRawRequestAsync(parent,path,"{\"event_id\":\""+input.Id("host")+"\",\"ignored\":\""+new string('x',17*1024)+"\"}",413);
        var mine=await CalendarRequestAsync(parent,HttpMethod.Get,"/api/mobile/my-availability",null,200);
        Check.True(mine.ValueKind==JsonValueKind.Array&&mine.EnumerateArray().Any(row=>row.GetProperty("event_id").GetString()==input.Id("host"))&&
            mine.EnumerateArray().All(row=>new[]{input.Accounts.ChildId,input.SecondChild,input.FcChild}.Contains(row.GetProperty("player_id").GetString())),"Own availability was not a direct array restricted to canonical children.");
        Check.True(mine.EnumerateArray().All(row=>row.EnumerateObject().All(property=>new[]{"event_id","event_type","player_id","status","updated_at"}.Contains(property.Name))),"Own availability exposed a private source or roster field.");
        await CalendarRequestAsync(coach,HttpMethod.Get,"/api/mobile/my-availability",null,403);
        var staff=await CalendarRequestAsync(coach,HttpMethod.Get,CalendarResponsesPath(input,"host"),null,200);
        var final=await CalendarRequestAsync(coach,HttpMethod.Get,CalendarResponsesPath(input,"host")+"?offset=200",null,200);
        Check.True(staff.GetProperty("players").GetArrayLength()==200&&staff.GetProperty("next_offset").GetInt32()==200&&final.GetProperty("players").GetArrayLength()==1&&
            final.GetProperty("next_offset").ValueKind==JsonValueKind.Null,"Staff responses did not page the 201st currently eligible player.");
        var players=staff.GetProperty("players").EnumerateArray().Concat(final.GetProperty("players").EnumerateArray()).ToArray();
        Check.Equal(201,players.Select(row=>row.GetProperty("id").GetString()).Distinct().Count(),"Staff response paging duplicated or omitted a player.");
        Check.True(players.Any(row=>row.GetProperty("id").GetString()==input.SecondChild),"Assigned host coach did not receive its explicit outside-group guest.");
        Check.True(players.Single(row=>row.GetProperty("id").GetString()==input.Accounts.ChildId).GetProperty("status").GetString()=="maybe","Staff response exposed physical attendance instead of RSVP.");
        var outside=await CalendarRequestAsync(coach,HttpMethod.Get,CalendarResponsesPath(input,"outside"),null,200);
        Check.True(outside.GetProperty("players").GetArrayLength()==1&&outside.GetProperty("players")[0].GetProperty("id").GetString()==input.Accounts.ChildId,
            "Guest invitation gave an outside coach the host group's roster names.");
        await CalendarRequestAsync(coach,HttpMethod.Get,CalendarResponsesPath(input,"group-two"),null,403);
        await CalendarRequestAsync(parent,HttpMethod.Get,CalendarResponsesPath(input,"host"),null,403);
        await CalendarRequestAsync(player,HttpMethod.Get,CalendarResponsesPath(input,"host"),null,403);
        await CalendarRequestAsync(coach,HttpMethod.Get,"/api/mobile/calendar/fixture/"+input.Id("shared")+"/responses",null,200);
        await CalendarRequestAsync(management,HttpMethod.Get,CalendarResponsesPath(input,"host"),null,200);
        foreach(var row in players)
            Check.True(row.EnumerateObject().All(property=>new[]{"id","name","number","position","image_url","status","updated_at"}.Contains(property.Name)),"Staff response returned a private roster field.");
        await using var context=database.CreateContext();
        var actor=input.Accounts.Plan.Mobiles.Single(row=>row.Role=="parent").Id;
        var rows=await context.CalendarResponses.AsNoTracking().Where(row=>row.EventId==input.Id("host")&&row.PlayerId==input.Accounts.ChildId).ToArrayAsync();
        Check.True(rows.Count(row=>(row.Collection is "availability" or "event_attendance")&&row.Status=="maybe"&&row.ActorIdentityId==actor)==2&&
            rows.Single(row=>row.Collection=="attendance").Status=="present", "RSVP did not atomically upsert both response collections while preserving physical attendance.");
        var audit=await context.SecurityAuditEvents.AsNoTracking().Where(row=>row.Action=="mobile.calendar.availability"&&row.ActorIdentityId==actor).ToArrayAsync();
        Check.True(audit.Length==5&&audit.All(row=>row.DeviceSessionId.HasValue&&row.OrganizationId==OrganizationIds.Academy&&row.ReasonCode=="own_player_rsvp"&&row.SubjectDigest is null),"Successful own RSVP audit lost actor/session/organization binding or invalid writes added audit.");
        AssertCalendarSafe(JsonSerializer.Serialize(audit),input);
        var before=await context.CalendarResponses.AsNoTracking().Where(row=>row.EventId==input.Id("shared")&&row.Kind=="match").Select(row=>new{row.Collection,row.Status,row.UpdatedAt,row.ActorIdentityId}).ToArrayAsync();
        var playerActor=input.Accounts.Plan.Mobiles.Single(row=>row.Role=="player").Id;
        var count=await context.SecurityAuditEvents.CountAsync(row=>row.Action=="mobile.calendar.availability"&&row.ActorIdentityId==playerActor);
        var probe="calendar_audit_failure_"+Guid.NewGuid().ToString("N");
        await context.Database.ExecuteSqlRawAsync("CREATE FUNCTION audit.PROBE() RETURNS trigger LANGUAGE plpgsql AS $failure$ BEGIN IF NEW.action='mobile.calendar.availability' AND NEW.actor_identity_id='ACTOR'::uuid THEN RAISE EXCEPTION 'Isolated calendar audit failure'; END IF; RETURN NEW; END; $failure$; CREATE TRIGGER PROBE BEFORE INSERT ON audit.security_events FOR EACH ROW EXECUTE FUNCTION audit.PROBE();".Replace("PROBE",probe,StringComparison.Ordinal).Replace("ACTOR",playerActor.ToString(),StringComparison.Ordinal));
        try
        {
            await CalendarRequestAsync(player,HttpMethod.Post,path,new{event_id=input.Id("shared"),event_type="match",status="not_going"},503);
            var after=await context.CalendarResponses.AsNoTracking().Where(row=>row.EventId==input.Id("shared")&&row.Kind=="match").Select(row=>new{row.Collection,row.Status,row.UpdatedAt,row.ActorIdentityId}).ToArrayAsync();
            Check.True(before.OrderBy(row=>row.Collection).SequenceEqual(after.OrderBy(row=>row.Collection))&&
                await context.SecurityAuditEvents.CountAsync(row=>row.Action=="mobile.calendar.availability"&&row.ActorIdentityId==playerActor)==count,
                "Failed final RSVP audit left a response or availability mutation.");
        }
        finally{await context.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS PROBE ON audit.security_events; DROP FUNCTION IF EXISTS audit.PROBE();".Replace("PROBE",probe,StringComparison.Ordinal));}
        foreach(var value in new[]{mine,staff,final,outside})AssertCalendarSafe(value.GetRawText(),input);
    }

    private async Task CalendarFinalBindingChecksAsync(CalendarDatabaseFixture input,HttpClient player,HttpClient parent,HttpClient coach,HttpClient management)
    {
        await using var context=database.CreateContext();
        var playerId=input.Accounts.Plan.Mobiles.Single(row=>row.Role=="player").Id;
        var parentId=input.Accounts.Plan.Mobiles.Single(row=>row.Role=="parent").Id;
        var admin=input.Accounts.Plan.Admins.Single();
        var writePath="/api/mobile/availability";
        var month="/api/mobile/calendar?month=2026-10";
        var membership=await context.RosterPlayerAcademyGroups.AsNoTracking().SingleAsync(row=>row.PlayerId==input.Accounts.ChildId&&row.AcademyGroupId==input.Accounts.GroupId);
        await AtFinalQueryAsync(()=>CalendarRequestAsync(parent,HttpMethod.Post,writePath,new{event_id=input.Id("host"),player_id=input.Accounts.ChildId,status="not_going"},403),
            ()=>context.RosterPlayerAcademyGroups.Where(row=>row.PlayerId==membership.PlayerId&&row.AcademyGroupId==membership.AcademyGroupId).ExecuteDeleteAsync(),
            async()=>{context.RosterPlayerAcademyGroups.Add(membership);await context.SaveChangesAsync();});
        context.ChangeTracker.Clear();
        var link=await context.MobileRecordLinks.AsNoTracking().SingleAsync(row=>row.IdentityId==parentId&&row.RecordId==input.Accounts.ChildId);
        await AtFinalQueryAsync(()=>CalendarRequestAsync(parent,HttpMethod.Get,month+"&player_id="+input.Accounts.ChildId,null,403),
            ()=>context.MobileRecordLinks.Where(row=>row.IdentityId==parentId&&row.RecordId==input.Accounts.ChildId).ExecuteDeleteAsync(),
            async()=>{context.MobileRecordLinks.Add(link);await context.SaveChangesAsync();});
        var assignment=await context.StaffAcademyGroups.AsNoTracking().SingleAsync(row=>row.StaffId==input.Accounts.StaffId&&row.AcademyGroupId==input.Accounts.GroupId);
        await AtFinalQueryAsync(()=>CalendarRequestAsync(coach,HttpMethod.Get,CalendarResponsesPath(input,"host"),null,403),
            ()=>context.StaffAcademyGroups.Where(row=>row.StaffId==assignment.StaffId&&row.AcademyGroupId==assignment.AcademyGroupId).ExecuteDeleteAsync(),
            async()=>{context.StaffAcademyGroups.Add(assignment);await context.SaveChangesAsync();});
        await AtFinalQueryAsync(()=>CalendarRequestAsync(coach,HttpMethod.Get,month,null,403),
            ()=>context.Staff.Where(row=>row.Id==input.Accounts.StaffId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,false)),
            ()=>context.Staff.Where(row=>row.Id==input.Accounts.StaffId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,true)));
        await AtFinalQueryAsync(()=>CalendarRequestAsync(management,HttpMethod.Get,CalendarResponsesPath(input,"host"),null,403),
            async()=>
            {
                await using var transaction=await context.Database.BeginTransactionAsync();
                await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.ActiveOrganizationId,(string?)null));
                await context.AdminOrganizationGrants.Where(row=>row.AdminAccountId==admin.Id&&row.OrganizationId==OrganizationIds.Academy).ExecuteDeleteAsync();
                await transaction.CommitAsync();
            },
            async()=>
            {
                context.AdminOrganizationGrants.Add(new(){AdminAccountId=admin.Id,OrganizationId=OrganizationIds.Academy});await context.SaveChangesAsync();
                await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.ActiveOrganizationId,OrganizationIds.Academy));
            });
        await AtFinalQueryAsync(()=>CalendarRequestAsync(management,HttpMethod.Get,month,null,403),
            ()=>context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.NormalizedPhone,input.Accounts.ParentPhone.Digits)),
            ()=>context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.NormalizedPhone,input.Accounts.StaffPhone.Digits)));
        await AtFinalQueryAsync(()=>CalendarRequestAsync(management,HttpMethod.Get,month,null,403),
            ()=>context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,false)),
            ()=>context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,true)));
        await AtFinalQueryAsync(()=>CalendarRequestAsync(player,HttpMethod.Get,"/api/mobile/my-availability",null,403),
            ()=>context.MobileIdentities.Where(row=>row.Id==playerId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,false)),
            ()=>context.MobileIdentities.Where(row=>row.Id==playerId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,true)));
        await AtFinalQueryAsync(()=>CalendarRequestAsync(parent,HttpMethod.Post,writePath,new{event_id=input.Id("host"),status="not_going"},403),
            ()=>context.MobileIdentities.Where(row=>row.Id==parentId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,false)),
            ()=>context.MobileIdentities.Where(row=>row.Id==parentId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,true)));
        await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.ActiveOrganizationId,(string?)null));
        try{await CalendarRequestAsync(management,HttpMethod.Get,month,null,403);}
        finally{await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.ActiveOrganizationId,OrganizationIds.Academy));}
        Check.Equal("maybe",(await context.CalendarResponses.AsNoTracking().SingleAsync(row=>row.Collection=="availability"&&row.EventId==input.Id("host")&&row.PlayerId==input.Accounts.ChildId)).Status,
            "A revoked final binding still changed RSVP state.");
        Check.Equal(5,await context.SecurityAuditEvents.CountAsync(row=>row.ActorIdentityId==parentId&&row.Action=="mobile.calendar.availability"),"Denied final-query mutations appended a success audit.");

        async Task AtFinalQueryAsync(Func<Task<JsonElement>> request,Func<Task> revoke,Func<Task> restore)
        {
            var gate=factory.CalendarGate.HoldNext();var pending=request();var revoked=false;
            try
            {
                var entered=gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if(await Task.WhenAny(entered,pending)==pending){await pending;throw new InvalidOperationException("Calendar request completed before the final-query gate.");}
                await entered;await revoke();revoked=true;gate.Release.TrySetResult();
                var denied=await pending;AssertCalendarSafe(denied.GetRawText(),input);
                Check.True(!denied.TryGetProperty("items",out _)&&!denied.TryGetProperty("players",out _),"Final authorization refusal returned stale private calendar data.");
            }
            finally{gate.Release.TrySetResult();try{await pending;}finally{if(revoked)await restore();}}
        }
    }

    private async Task ExpectCalendarRefusalAsync(CalendarDatabaseFixture input,string code,bool runtime=false,string? expectedDigest=null,
        ValidatedCalendarImport? manifest=null,string? expectedDatabase=null,string environment="development")
    {
        try{await database.ApplyCalendarAsync(input,runtime:runtime,expectedDigest:expectedDigest,manifest:manifest,expectedDatabase:expectedDatabase,environment:environment);}
        catch(RosterImportApplyException issue)when(issue.Code==code){return;}
        throw new InvalidOperationException("Owner calendar import did not refuse fixed code: "+code);
    }

    private static string CalendarResponsesPath(CalendarDatabaseFixture input,string suffix)=>"/api/mobile/calendar/event/"+input.Id(suffix)+"/responses";
    private static string[] CalendarIds(JsonElement page)=>page.GetProperty("items").EnumerateArray().Select(row=>row.GetProperty("id").GetString()!).ToArray();
    private static void AssertCalendarSafe(string output,CalendarDatabaseFixture input)
    {
        Check.True(!output.Contains("PRIVATE_CALENDAR_SOURCE_MARKER",StringComparison.Ordinal)&&!output.Contains("UNPUBLISHED_SUMMARY_MARKER",StringComparison.Ordinal)&&
            !output.Contains("private-principal-marker",StringComparison.Ordinal)&&!output.Contains("$2b$",StringComparison.Ordinal)&&!output.Contains("source_json",StringComparison.Ordinal)&&
            !output.Contains("manifest_bytes",StringComparison.Ordinal)&&!input.Accounts.Phones.Any(phone=>output.Contains(phone,StringComparison.Ordinal)),"Calendar output leaked protected source, credential or contact data.");
    }
    private static async Task<JsonElement> CalendarRequestAsync(HttpClient client,HttpMethod method,string path,object? body,int expectedStatus)
    {
        using var request=new HttpRequestMessage(method,path);if(body is not null)request.Content=JsonContent.Create(body);
        using var response=await client.SendAsync(request);return await CalendarResponseAsync(response,expectedStatus);
    }
    private static async Task CalendarRawRequestAsync(HttpClient client,string path,string json,int expectedStatus)
    {
        using var body=new StringContent(json,Encoding.UTF8,"application/json");using var response=await client.PostAsync(path,body);
        await CalendarResponseAsync(response,expectedStatus);
    }
    private static async Task<JsonElement> CalendarResponseAsync(HttpResponseMessage response,int expectedStatus)
    {
        var content=await response.Content.ReadAsByteArrayAsync();
        var actual=(int)response.StatusCode;
        var body=default(JsonElement);var code="none";
        if(content.Length>0)
        {
            try
            {
                using var document=JsonDocument.Parse(content);body=document.RootElement.Clone();
                if(body.ValueKind==JsonValueKind.Object&&body.TryGetProperty("code",out var value)&&value.ValueKind==JsonValueKind.String)
                {
                    var candidate=value.GetString();
                    code=candidate is {Length:>0 and <=64}&&candidate.All(character=>character is >= 'a' and <= 'z' or '_')?candidate:"redacted";
                }
            }
            catch(JsonException){code="non_json";}
        }
        var request=response.RequestMessage;
        Check.Equal(expectedStatus,actual,$"Calendar HTTP {request?.Method.Method??"unknown"} {CalendarSafeRequestPath(request?.RequestUri)} expected={expectedStatus} observed={actual} code={code}.");
        Check.True(response.Headers.CacheControl?.NoStore==true,"Private calendar response did not carry no-store.");
        if(content.Length==0)return default;
        Check.True(body.ValueKind!=JsonValueKind.Undefined,"Calendar HTTP returned a non-JSON response.");
        return body;
    }
    private static string CalendarSafeRequestPath(Uri? uri)
    {
        if(uri is null||!uri.IsAbsoluteUri)return "{unknown_path}";
        var path=uri.AbsolutePath;
        if(path.StartsWith("/api/mobile/calendar/",StringComparison.Ordinal))
        {
            var parts=path.Split('/',StringSplitOptions.RemoveEmptyEntries);
            var kind=parts.Length>3&&parts[3] is "event" or "match" or "fixture" or "training"?parts[3]:"{event_type}";
            return "/api/mobile/calendar/"+kind+"/{event_id}/responses";
        }
        if(path is not ("/api/mobile/calendar" or "/api/mobile/availability" or "/api/mobile/my-availability"))return "/api/mobile/{route}";
        if(uri.Query.Length==0)return path;
        var query=uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(part=>
        {
            var pair=part.Split('=',2);var key=pair[0];var value=pair.Length==2?pair[1]:"";
            return key switch
            {
                "month"=>"month="+(value.Length==7&&value[4]=='-'&&value.Where((_,index)=>index!=4).All(character=>character is >= '0' and <= '9')?value:"{invalid}"),
                "offset"=>"offset="+(value.Length is >0 and <=6&&value.All(character=>character is >= '0' and <= '9')?value:"{invalid}"),
                "player_id"=>"player_id={selected}","organization_id"=>"organization_id={selected}","role"=>"role={selected}",_=>"{selector}"
            };
        });
        return path+"?"+string.Join('&',query);
    }
}
