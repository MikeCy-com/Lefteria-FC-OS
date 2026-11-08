using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.AccountImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using LefteriaFc.Infrastructure.AccountImport;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Roster;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task AccountImportEndToEndAsync()
    {
        var input=AccountImportFixture.Create(database);
        await using(var initial=database.CreateContext())
            Check.True(!await initial.MobileIdentities.AnyAsync()&&!await initial.AdminAccounts.AnyAsync()&&!await initial.RosterImportStates.AnyAsync()&&!await initial.StaffRelationshipImportStates.AnyAsync(),
                "Account end-to-end scenario must run first against the empty guarded fixture.");
        try
        {
            await database.ApplyRosterAsync(input.Roster);await database.ApplyStaffAsync(input.Staff);
            await AccountOwnerImportChecksAsync(input);
            await LegacyBridgeHttpChecksAsync(input);
            await AccountCanonicalHttpChecksAsync(input);
            await AccountFanEvidenceHttpChecksAsync(input);
            await database.AssertAccountRuntimePermissionsAsync();
            await ExpectAccountRefusalAsync(input,"account_target_reconciliation_failed");
        }
        finally
        {
            await database.CleanupAccountImportsAsync();
            await database.CleanupStaffImportAsync(input.Staff);
            await database.CleanupRosterImportAsync(input.Roster);
        }
        await using var cleaned=database.CreateContext();
        Check.True(!await cleaned.AccountImportStates.AnyAsync()&&!await cleaned.AccountSourceDocuments.AnyAsync()&&!await cleaned.MobileIdentities.AnyAsync()&&!await cleaned.AdminAccounts.AnyAsync(),
            "Exact account cleanup left imported data before the existing native-auth cases.");
    }

    private async Task AccountOwnerImportChecksAsync(AccountImportFixture input)
    {
        Check.True(input.Plan.Mobiles.Count==5&&input.Plan.QuarantinedRows==2,"Synthetic account plan did not keep five role identities and quarantine both duplicate rows.");
        await database.AssertAccountCliAsync(input,false);
        var review=await database.ApplyAccountsAsync(input,reviewOnly:true);
        Check.True(review.Reviewed&&!review.Applied&&!review.Noop,"Read-only target review reported a mutation.");
        await using var context=database.CreateContext();
        Check.True(!await context.MobileIdentities.AnyAsync()&&!await context.AdminAccounts.AnyAsync()&&!await context.AccountImportStates.AnyAsync()&&
            !await context.SecurityAuditEvents.AnyAsync(row=>row.SubjectDigest==input.Manifest.Digest),"Target review wrote accounts or audit.");
        await ExpectAccountRefusalAsync(input,"digest_mismatch",expectedDigest:new string('0',64));
        await ExpectAccountRefusalAsync(input,"owner_target_required",runtime:true);
        var wrongBase=input.Input.DeepClone().AsObject();wrongBase["staff_sha256"]=new string('a',64);
        await ExpectAccountRefusalAsync(input,"account_base_snapshot_mismatch",manifest:AccountImportValidator.Validate(RosterBytes(wrongBase)));
        var orphanPhone=database.NewPhone();var orphan=await database.SeedIdentityAsync(orphanPhone,MobileRole.Fan);
        try{await ExpectAccountRefusalAsync(input,"account_uninitialized_target_not_empty");}
        finally{await context.MobileIdentities.Where(row=>row.Id==orphan).ExecuteDeleteAsync();}

        var probe="account_failure_"+Guid.NewGuid().ToString("N");
        // The identifiers/digest are generated or SHA-256 validated fixture
        // values. No source JSON or private contact enters SQL text.
        var create="CREATE FUNCTION migration.PROBE() RETURNS trigger LANGUAGE plpgsql AS $failure$ BEGIN IF NEW.import_digest='DIGEST' THEN RAISE EXCEPTION 'Isolated account archive failure'; END IF; RETURN NEW; END; $failure$; CREATE TRIGGER PROBE BEFORE INSERT ON migration.account_source_documents FOR EACH ROW EXECUTE FUNCTION migration.PROBE();"
            .Replace("PROBE",probe,StringComparison.Ordinal).Replace("DIGEST",input.Manifest.Digest,StringComparison.Ordinal);
        await context.Database.ExecuteSqlRawAsync(create);
        try
        {
            var failed=false;try{await database.ApplyAccountsAsync(input);}catch(DbUpdateException){failed=true;}
            Check.True(failed,"Injected raw account archive failure did not abort import.");
            Check.True(!await context.AccountImportStates.AnyAsync()&&!await context.MobileIdentities.AnyAsync()&&!await context.AdminAccounts.AnyAsync()&&
                !await context.AdminOrganizationGrants.AnyAsync()&&!await context.LegacyAccountMappings.AnyAsync()&&!await context.AccountSourceDocuments.AnyAsync()&&
                !await context.SecurityAuditEvents.AnyAsync(row=>row.SubjectDigest==input.Manifest.Digest),"Failed import left partial account/grant/source/mapping/audit data.");
        }
        finally
        {
            var cleanup="DROP TRIGGER IF EXISTS PROBE ON migration.account_source_documents; DROP FUNCTION IF EXISTS migration.PROBE();".Replace("PROBE",probe,StringComparison.Ordinal);
            await context.Database.ExecuteSqlRawAsync(cleanup);
        }
        var concurrent=await Task.WhenAll(database.ApplyAccountsAsync(input),database.ApplyAccountsAsync(input));
        Check.Equal(1,concurrent.Count(row=>row.Applied),"Concurrent initial account import wrote twice.");
        Check.Equal(1,concurrent.Count(row=>row.Noop),"Concurrent exact account replay was not a no-op.");
        Check.True((await database.ApplyAccountsAsync(input)).Noop,"Exact seven-digit timestamp manifest did not replay after PG microsecond storage.");
        var state=await context.AccountImportStates.AsNoTracking().SingleAsync();
        Check.True(state.ManifestBytes.AsSpan().SequenceEqual(input.Manifest.CopySourceBytes()),"Account archive did not preserve exact original bytes.");
        Check.Equal(5,await context.MobileIdentities.CountAsync(),"Pre-OTP evidence or duplicate quarantine fabricated mobile subjects.");
        Check.Equal(2,await context.AccountSourceDocuments.CountAsync(row=>row.Disposition=="quarantined"),"Conflicting source rows were not retained owner-only.");
        Check.True((await context.AccountSourceDocuments.FirstAsync(row=>row.Collection=="mobile_users")).SourceJson.Contains("private-principal-marker",StringComparison.Ordinal),"Unknown private principal provenance was dropped.");
        var report=JsonSerializer.Serialize(concurrent[0]);
        Check.True(!report.Contains("$2b$",StringComparison.Ordinal)&&!report.Contains(input.PlayerPhone.Digits,StringComparison.Ordinal)&&!report.Contains("private-principal-marker",StringComparison.Ordinal),"Owner import report exposed credentials/source contacts.");
        var fan=input.Plan.Mobiles.Single(row=>row.Role=="fan");var admin=input.Plan.Admins.Single();
        var link=await context.MobileRecordLinks.AsNoTracking().SingleAsync(row=>row.Role=="player");
        var source=await context.AccountSourceDocuments.AsNoTracking().FirstAsync(row=>row.Collection=="mobile_users");
        await RejectMutationAsync(
            ()=>context.MobileIdentities.Where(row=>row.Id==fan.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.DisplayName,"Changed verified name")),
            ()=>context.MobileIdentities.Where(row=>row.Id==fan.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.DisplayName,fan.Name)));
        await RejectMutationAsync(
            ()=>context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.PasswordHash,"$2b$12$"+new string('B',53))),
            ()=>context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.PasswordHash,admin.PasswordHash)));
        await RejectMutationAsync(
            ()=>context.MobileRecordLinks.Where(row=>row.IdentityId==link.IdentityId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsPrimary,false)),
            ()=>context.MobileRecordLinks.Where(row=>row.IdentityId==link.IdentityId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsPrimary,true)));
        await RejectMutationAsync(
            ()=>context.AccountSourceDocuments.Where(row=>row.Collection==source.Collection&&row.Ordinal==source.Ordinal).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.SourceJson," "+source.SourceJson)),
            ()=>context.AccountSourceDocuments.Where(row=>row.Collection==source.Collection&&row.Ordinal==source.Ordinal).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.SourceJson,source.SourceJson)));
        Check.True((await database.ApplyAccountsAsync(input)).Noop,"Restored exact target no longer reconciled.");
        await database.AssertAccountCliAsync(input,true);
        await database.AssertAccountRollbackRefusedAsync();
        Check.True((await database.ApplyAccountsAsync(input)).Noop,"Refused schema rollback changed the imported snapshot.");
        var replacement=input.Input.DeepClone().AsObject();replacement["mobile_users"]![0]!["name"]="Forbidden replacement";
        await ExpectAccountRefusalAsync(input,"account_initial_snapshot_already_applied",manifest:AccountImportValidator.Validate(RosterBytes(replacement)));
        Check.Equal(1,await context.SecurityAuditEvents.CountAsync(row=>row.Action=="accounts.import"&&row.SubjectDigest==input.Manifest.Digest),"Replay or target review duplicated import audit.");

        async Task RejectMutationAsync(Func<Task<int>> mutate,Func<Task<int>> restore)
        {
            await mutate();
            try{await ExpectAccountRefusalAsync(input,"account_target_reconciliation_failed");}
            finally{await restore();}
        }
    }

    private async Task AccountCanonicalHttpChecksAsync(AccountImportFixture input)
    {
        using var player=Client();using var parent=Client();using var coach=Client();using var management=Client();using var fan=Client();
        await LoginAsync(player,input.PlayerPhone,"player");await LoginAsync(parent,input.ParentPhone,"parent");
        await LoginAsync(coach,input.StaffPhone,"coach");await LoginAsync(management,input.StaffPhone,"management");await LoginAsync(fan,input.PlayerPhone,"fan");
        await ChatGroupDirectoryHttpChecksAsync(input,parent,player,coach,management,fan);
        var playerMe=await GetAsync(player,"/api/mobile/auth/me",200);var parentMe=await GetAsync(parent,"/api/mobile/auth/me",200);
        var coachMe=await GetAsync(coach,"/api/mobile/auth/me",200);var managementMe=await GetAsync(management,"/api/mobile/auth/me",200);
        Check.Equal(input.PlayerId,playerMe.GetProperty("linked_player_id").GetString(),"Imported player self link was not freshly projected.");
        Check.True(parentMe.GetProperty("linked_player_ids").EnumerateArray().Select(row=>row.GetString()).SequenceEqual(new[]{input.ChildId}),"Guardian child links were not canonical.");
        Check.Equal(input.GroupId,parentMe.GetProperty("academy_group_ids")[0].GetString(),"Guardian group projection differs.");
        Check.True(coachMe.GetProperty("team_id").GetString()==input.TeamId&&coachMe.GetProperty("academy_group_ids")[0].GetString()==input.GroupId,"Shared coach did not project both canonical FC/Academy assignments.");
        foreach(var body in new[]{playerMe,parentMe,coachMe,managementMe})
            Check.True(!body.GetRawText().Contains("$2b$",StringComparison.Ordinal)&&!body.GetRawText().Contains("private-principal-marker",StringComparison.Ordinal)&&!body.GetRawText().Contains("private-staff@example.invalid",StringComparison.Ordinal),"HTTP profile leaked source credentials/private raw data.");
        foreach(var role in new[]{"player","parent","coach","management"})await AssertFinalPickerBindingAsync(role,true);
        await using var context=database.CreateContext();
        var parentId=input.Plan.Mobiles.Single(row=>row.Role=="parent").Id;var admin=input.Plan.Admins.Single();
        await context.RosterPlayers.Where(row=>row.Id==input.ChildId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,true));
        try{await AssertUnavailableAsync(parent,"parent");await GetAsync(parent,ChatDirectoryPath,403);Check.True((await context.MobileIdentities.AsNoTracking().SingleAsync(row=>row.Id==parentId)).IsActive,"Roster archival rewrote source mobile active flag.");}
        finally{await context.RosterPlayers.Where(row=>row.Id==input.ChildId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,false));}
        Check.True((await GetAsync(parent,"/api/mobile/auth/me",200)).GetProperty("current_role_available").GetBoolean(),"Restored canonical child did not recover availability.");
        await context.Staff.Where(row=>row.Id==input.StaffId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,false));
        try{await AssertUnavailableAsync(coach,"coach");await AssertUnavailableAsync(management,"management");await GetAsync(coach,ChatDirectoryPath,403);await GetAsync(management,ChatDirectoryPath,403);}
        finally{await context.Staff.Where(row=>row.Id==input.StaffId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,true));}
        await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.NormalizedPhone,input.ParentPhone.Digits));
        try{await AssertUnavailableAsync(management,"management");}
        finally{await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.NormalizedPhone,input.StaffPhone.Digits));}
        await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.InvitePending,true).SetProperty(row=>row.IsActive,false));
        try{await AssertUnavailableAsync(management,"management");}
        finally{await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.InvitePending,false).SetProperty(row=>row.IsActive,true));}
        var organization=await GetAsync(management,"/api/mobile/management/organization",200);
        Check.Equal(OrganizationIds.Club,organization.GetProperty("organization_id").GetString(),"Imported active organization was not preserved.");
        await database.RemoveGrantAsync(admin.Id,OrganizationIds.Academy);
        try
        {
            Check.Equal(1,await context.ImportedAdminGrants.CountAsync(row=>row.AdminAccountId==admin.Id&&row.OrganizationId==OrganizationIds.Academy),
                "Revoking a current grant deleted its historical import provenance.");
            await PostAsync(management,"/api/mobile/management/organization",new{organization_id=OrganizationIds.Academy},403);
        }
        finally
        {
            context.AdminOrganizationGrants.Add(new(){AdminAccountId=admin.Id,OrganizationId=OrganizationIds.Academy});
            await context.SaveChangesAsync();
        }
        await PutProfileDetailsAsync(fan,new{name="Verified independent fan",favourite_player_id=input.ChildId,notification_prefs=new Dictionary<string,bool>{{"club_news",true}}});
        var fanMe=await GetAsync(fan,"/api/mobile/auth/me",200);
        Check.True(!fanMe.GetProperty("available_roles").EnumerateArray().Any(row=>row.GetString()=="parent"),"Favourite player preference granted guardian authority.");
        Check.Equal("Imported player",(await database.IdentityAsync(input.PlayerPhone,MobileRole.Player))!.DisplayName,"Fan profile mutated its same-phone player role.");
        var selfLink=await context.MobileRecordLinks.AsNoTracking().SingleAsync(row=>row.Role=="player");
        await database.AssertRejectedSqlAsync("UPDATE identity.mobile_record_links SET normalized_phone=@phone WHERE identity_id=@id",PostgresErrorCodes.ForeignKeyViolation,
            new NpgsqlParameter("phone",input.ParentPhone.Digits),new NpgsqlParameter("id",selfLink.IdentityId));
        await database.AssertRejectedSqlAsync("UPDATE identity.mobile_record_links SET role='fan' WHERE identity_id=@id",PostgresErrorCodes.CheckViolation,new NpgsqlParameter("id",selfLink.IdentityId));
        await database.AssertRejectedSqlAsync("UPDATE identity.mobile_record_links SET kind='guardian' WHERE identity_id=@id",PostgresErrorCodes.CheckViolation,new NpgsqlParameter("id",selfLink.IdentityId));
        await context.MobileRecordLinks.Where(row=>row.IdentityId==selfLink.IdentityId).ExecuteDeleteAsync();
        try{await AssertUnavailableAsync(player,"player");}
        finally{context.MobileRecordLinks.Add(selfLink);await context.SaveChangesAsync();}

        async Task AssertUnavailableAsync(HttpClient client,string role)
        {
            var unavailable=await GetAsync(client,"/api/mobile/auth/me",200);
            Check.True(!unavailable.GetProperty("current_role_available").GetBoolean()&&unavailable.GetProperty("linked_player_ids").GetArrayLength()==0&&
                unavailable.GetProperty("academy_group_ids").GetArrayLength()==0,"Revoked canonical role retained private links/availability.");
            await AssertFinalPickerBindingAsync(role,false);
        }
        async Task AssertFinalPickerBindingAsync(string role,bool available)
        {
            var imported=input.Plan.Mobiles.Single(row=>row.Role==role);
            if(!MobileRoles.TryParse(role,out var parsed))throw new InvalidOperationException("Fixture mobile role was not canonical.");
            var session=new MobileSession(Guid.NewGuid(),imported.Id,imported.Phone,parsed,DateTimeOffset.UtcNow.AddMinutes(5));
            await using var direct=database.CreateContext();
            var snapshot=await new PostgresRosterQueries(direct).GetFirstTeamPickerAsync(session,CancellationToken.None);
            Check.Equal(available,snapshot.IdentityAvailable,"Final picker SQL did not rebind the fresh imported canonical role graph.");
            Check.True(available||snapshot.Players.Count==0,"Final picker returned rows after canonical role revocation.");
        }
    }

    private async Task AccountFanEvidenceHttpChecksAsync(AccountImportFixture input)
    {
        using var active=Client();await RequestAsync(active,input.EvidencePhone);
        Check.True(await database.IdentityAsync(input.EvidencePhone,MobileRole.Fan) is null,"Fan evidence created an identity before OTP proof.");
        var code=factory.Sms.Latest(input.EvidencePhone);await VerifyAsync(active,input.EvidencePhone,Wrong(code),400,"fan");
        Check.True(await database.IdentityAsync(input.EvidencePhone,MobileRole.Fan) is null,"Wrong OTP created a fan from evidence.");
        var verified=await VerifyAsync(active,input.EvidencePhone,code,200,"fan");
        var seeded=verified.GetProperty("user");
        Check.True(seeded.GetProperty("first_name").GetString()=="Evidence"&&seeded.GetProperty("birthday").GetString()=="1990-01-02"&&
            seeded.GetProperty("avatar_url").GetString()=="/api/uploads/avatars/legacy-fixture.jpg"&&seeded.GetProperty("favourite_player_id").GetString()==input.PlayerId&&
            !seeded.GetProperty("notification_prefs").GetProperty("club_news").GetBoolean(),"Verified evidence profile was not safely seeded.");
        using var inactive=Client();await RegisterAsync(inactive,input.InactivePhone,"Replacement","Attempt","1990-01-01",403);
        await RequestAsync(inactive,input.InactivePhone);await VerifyAsync(inactive,input.InactivePhone,factory.Sms.Latest(input.InactivePhone),404,"fan");
        Check.True(await database.IdentityAsync(input.InactivePhone,MobileRole.Fan) is null,"Inactive evidence reactivated through registration or OTP.");
        using var stale=Client();await RequestAsync(stale,input.StalePhone);
        var freshId=await database.SeedIdentityAsync(input.StalePhone,MobileRole.Fan,"Already verified new profile");
        await using(var context=database.CreateContext())await context.MobileIdentities.Where(row=>row.Id==freshId).ExecuteUpdateAsync(setters=>setters
            .SetProperty(row=>row.FirstName,"Verified").SetProperty(row=>row.LastName,"Fresh").SetProperty(row=>row.FavouritePlayerId,"new-choice")
            .SetProperty(row=>row.NotificationPreferencesJson,"{\"club_news\":true}"));
        await VerifyAsync(stale,input.StalePhone,factory.Sms.Latest(input.StalePhone),200,"fan");
        var preserved=(await database.IdentityAsync(input.StalePhone,MobileRole.Fan))!;
        Check.True(preserved.DisplayName=="Already verified new profile"&&preserved.FirstName=="Verified"&&preserved.FavouritePlayerId=="new-choice"&&
            preserved.NotificationPreferencesJson!.Contains("true",StringComparison.Ordinal),"Stale evidence challenge overwrote an already verified fan profile.");
    }

    private async Task ExpectAccountRefusalAsync(AccountImportFixture input,string code,bool runtime=false,string? expectedDigest=null,ValidatedAccountImport? manifest=null)
    {
        try{await database.ApplyAccountsAsync(input,runtime:runtime,expectedDigest:expectedDigest,manifest:manifest);}
        catch(RosterImportApplyException issue)when(issue.Code==code){return;}
        throw new InvalidOperationException("Owner account import did not refuse fixed code: "+code);
    }
}
