using LefteriaFc.Application.AccountImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Roster;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.AccountImport;

public sealed record AccountImportOutcome(bool Applied,bool Noop,bool Reviewed,string Digest,
    string CountsJson,IReadOnlyDictionary<string,int> IssueCounts)
{
    public override string ToString()=>"Protected account import outcome";
}

// Explicit owner executable only. Never registered in the API service collection.
public static class PostgresAccountImport
{
    public static async Task<AccountImportOutcome> ApplyAsync(ValidatedAccountImport manifest,
        string connectionString,string expectedDigest,string expectedDatabase,string environment,
        bool reviewOnly=false,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        await using var database=await OwnerImportTarget.OpenAsync(connectionString,manifest.Digest,expectedDigest,expectedDatabase,environment,token);
        await using var transaction=await database.Database.BeginTransactionAsync(token);
        await database.Database.ExecuteSqlRawAsync("""
            SET LOCAL lock_timeout='10s';
            SELECT pg_advisory_xact_lock(5495884701258);
            LOCK TABLE identity.mobile_identities,identity.admin_accounts,identity.admin_organization_grants,
              identity.fan_registration_evidence,identity.canonical_contact_bindings,identity.mobile_record_links,
              identity.admin_staff_links,migration.account_import_state,migration.account_source_documents,
              migration.account_import_issues,migration.legacy_account_mappings,migration.imported_admin_grants
              IN SHARE ROW EXCLUSIVE MODE;
            LOCK TABLE club.teams,club.academy_groups,club.players,club.player_academy_groups,
              club.staff,club.staff_teams,club.staff_academy_groups,club.academy_group_coaches,
              club.roster_import_state,migration.roster_source_documents,migration.staff_relationship_import_state,
              migration.staff_source_documents,migration.contact_provenance IN SHARE MODE;
            """,token);
        var previous=await database.AccountImportStates.AsNoTracking().SingleOrDefaultAsync(token);
        if(previous is null&&(await database.MobileIdentities.AnyAsync(token)||await database.AdminAccounts.AnyAsync(token)||
            await database.AdminOrganizationGrants.AnyAsync(token)||await database.FanRegistrationEvidence.AnyAsync(token)||
            await database.CanonicalContactBindings.AnyAsync(token)||await database.MobileRecordLinks.AnyAsync(token)||
            await database.AdminStaffLinks.AnyAsync(token)||await database.AccountSourceDocuments.AnyAsync(token)||
            await database.AccountImportIssues.AnyAsync(token)||await database.LegacyAccountMappings.AnyAsync(token)||
            await database.ImportedAdminGrants.AnyAsync(token)))throw Refused("account_uninitialized_target_not_empty");
        var context=await AccountImportSnapshotReader.ReadAsync(database,manifest,environment,token);
        var plan=AccountImportPlanner.Build(manifest,context);
        var rows=AccountImportRows.From(plan);
        if(previous is not null)
        {
            await AccountImportReconciliation.VerifyAsync(database,plan,rows,previous,environment,token);
            await transaction.CommitAsync(token);
            return Outcome(false,true,reviewOnly);
        }
        if(reviewOnly)
        {
            await transaction.RollbackAsync(token);
            return Outcome(false,false,true);
        }
        var now=DateTimeOffset.UtcNow;now=now.AddTicks(-(now.Ticks%10));
        database.AccountImportStates.Add(new()
        {
            Digest=manifest.Digest,RosterDigest=manifest.RosterDigest,StaffDigest=manifest.StaffDigest,
            SourceCommit=manifest.SourceCommit,Environment=environment,ExportedAt=manifest.ExportedAt,
            AppliedAt=now,CountsJson=rows.CountsJson,ManifestBytes=manifest.CopySourceBytes()
        });
        database.AdminAccounts.AddRange(rows.Admins);database.MobileIdentities.AddRange(rows.Mobiles);
        database.FanRegistrationEvidence.AddRange(rows.Fans);database.CanonicalContactBindings.AddRange(rows.Bindings);
        database.MobileRecordLinks.AddRange(rows.Links);database.AdminStaffLinks.AddRange(rows.StaffLinks);
        database.AccountSourceDocuments.AddRange(rows.Sources);database.LegacyAccountMappings.AddRange(rows.Mappings);
        database.AccountImportIssues.AddRange(rows.Issues);
        await database.SaveChangesAsync(token);
        database.AdminOrganizationGrants.AddRange(rows.Grants);database.ImportedAdminGrants.AddRange(rows.GrantOrigins);
        await database.SaveChangesAsync(token);
        var selections=plan.Admins.ToDictionary(row=>row.Id,row=>row.ActiveOrganizationId);
        foreach(var admin in rows.Admins)admin.ActiveOrganizationId=selections[admin.Id];
        database.SecurityAuditEvents.Add(new SecurityAuditRecord{Id=Guid.NewGuid(),OccurredAt=now,Action="accounts.import",Outcome="success",ReasonCode="initial_snapshot",SubjectDigest=manifest.Digest});
        await database.SaveChangesAsync(token);await transaction.CommitAsync(token);
        return Outcome(true,false,false);

        AccountImportOutcome Outcome(bool applied,bool noop,bool reviewed)=>new(applied,noop,reviewed,manifest.Digest,rows.CountsJson,
            plan.Issues.GroupBy(row=>row.Code,StringComparer.Ordinal).ToDictionary(group=>group.Key,group=>group.Count(),StringComparer.Ordinal));
    }
    private static RosterImportApplyException Refused(string code)=>new(code);
}
