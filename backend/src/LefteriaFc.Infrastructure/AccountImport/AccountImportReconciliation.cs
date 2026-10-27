using System.Text.Json;
using LefteriaFc.Application.AccountImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.AccountImport;

internal static class AccountImportReconciliation
{
    public static async Task VerifyAsync(LefteriaDbContext database,AccountImportPlan plan,
        AccountImportRows expected,AccountImportStateRecord state,string environment,CancellationToken token)
    {
        if(state.Digest!=plan.Manifest.Digest)throw Refused("account_initial_snapshot_already_applied");
        if(state.RosterDigest!=plan.Manifest.RosterDigest||state.StaffDigest!=plan.Manifest.StaffDigest||
            state.SourceCommit!=plan.Manifest.SourceCommit||state.ExportedAt!=plan.Manifest.ExportedAt||state.Environment!=environment||
            !state.ManifestBytes.AsSpan().SequenceEqual(plan.Manifest.CopySourceBytes())||NormalizeObject(state.CountsJson)!=NormalizeObject(expected.CountsJson))throw Refused("account_target_reconciliation_failed");
        var selections=plan.Admins.ToDictionary(row=>row.Id,row=>row.ActiveOrganizationId);
        foreach(var admin in expected.Admins)admin.ActiveOrganizationId=selections[admin.Id];
        var mobiles=await database.MobileIdentities.AsNoTracking().ToArrayAsync(token);
        var admins=await database.AdminAccounts.AsNoTracking().ToArrayAsync(token);
        var fans=await database.FanRegistrationEvidence.AsNoTracking().ToArrayAsync(token);
        if(!Same(mobiles.Select(row=>(row.Id,row.LegacyId,row.NormalizedPhone,row.Role,row.DisplayName,row.IsActive,row.ManagementAdminId,
                row.FirstName,row.LastName,row.Birthday,row.AvatarUrl,row.FavoriteTeam,row.Email,row.FavouritePlayerId,NormalizeObject(row.NotificationPreferencesJson),row.Platform,row.CreatedAt,row.LastLogin,row.UpdatedAt)),
            expected.Mobiles.Select(row=>(row.Id,row.LegacyId,row.NormalizedPhone,row.Role,row.DisplayName,row.IsActive,row.ManagementAdminId,
                row.FirstName,row.LastName,row.Birthday,row.AvatarUrl,row.FavoriteTeam,row.Email,row.FavouritePlayerId,NormalizeObject(row.NotificationPreferencesJson),row.Platform,row.CreatedAt,row.LastLogin,row.UpdatedAt)))||
            !Same(admins.Select(row=>(row.Id,row.LegacyId,row.Username,row.DisplayName,row.Role,row.PasswordHash,row.NormalizedPhone,row.IsActive,row.InvitePending,row.Otp2faEnabled,row.ActiveOrganizationId)),
                expected.Admins.Select(row=>(row.Id,row.LegacyId,row.Username,row.DisplayName,row.Role,row.PasswordHash,row.NormalizedPhone,row.IsActive,row.InvitePending,row.Otp2faEnabled,row.ActiveOrganizationId)))||
            !Same(fans.Select(row=>(row.Id,row.LegacyId,row.NormalizedPhone,row.FirstName,row.LastName,row.Birthday,row.IsActive,row.AvatarUrl,row.FavouritePlayerId,NormalizeObject(row.NotificationPreferencesJson))),
                expected.Fans.Select(row=>(row.Id,row.LegacyId,row.NormalizedPhone,row.FirstName,row.LastName,row.Birthday,row.IsActive,row.AvatarUrl,row.FavouritePlayerId,NormalizeObject(row.NotificationPreferencesJson)))))throw Refused("account_target_reconciliation_failed");
        var bindings=await database.CanonicalContactBindings.AsNoTracking().ToArrayAsync(token);
        var links=await database.MobileRecordLinks.AsNoTracking().ToArrayAsync(token);
        var staff=await database.AdminStaffLinks.AsNoTracking().ToArrayAsync(token);
        var grants=await database.AdminOrganizationGrants.AsNoTracking().ToArrayAsync(token);
        var origins=await database.ImportedAdminGrants.AsNoTracking().ToArrayAsync(token);
        if(!Same(bindings.Select(row=>(row.Collection,row.SourceId,row.Kind,row.NormalizedPhone,row.PlayerId,row.StaffId,row.ImportDigest)),expected.Bindings.Select(row=>(row.Collection,row.SourceId,row.Kind,row.NormalizedPhone,row.PlayerId,row.StaffId,row.ImportDigest)))||
            !Same(links.Select(row=>(row.IdentityId,row.Role,row.NormalizedPhone,row.Collection,row.RecordId,row.Kind,row.IsPrimary)),expected.Links.Select(row=>(row.IdentityId,row.Role,row.NormalizedPhone,row.Collection,row.RecordId,row.Kind,row.IsPrimary)))||
            !Same(staff.Select(row=>(row.AdminAccountId,row.StaffId)),expected.StaffLinks.Select(row=>(row.AdminAccountId,row.StaffId)))||
            !Same(grants.Select(row=>(row.AdminAccountId,row.OrganizationId)),expected.Grants.Select(row=>(row.AdminAccountId,row.OrganizationId)))||
            !Same(origins.Select(row=>(row.AdminAccountId,row.OrganizationId,row.Provenance)),expected.GrantOrigins.Select(row=>(row.AdminAccountId,row.OrganizationId,row.Provenance))))throw Refused("account_target_reconciliation_failed");
        var sources=await database.AccountSourceDocuments.AsNoTracking().ToArrayAsync(token);
        var mappings=await database.LegacyAccountMappings.AsNoTracking().ToArrayAsync(token);
        var issues=await database.AccountImportIssues.AsNoTracking().ToArrayAsync(token);
        if(!Same(sources.Select(row=>(row.Collection,row.Ordinal,row.LegacyId,row.TargetId,row.Disposition,row.ImportDigest,row.SourceJson)),expected.Sources.Select(row=>(row.Collection,row.Ordinal,row.LegacyId,row.TargetId,row.Disposition,row.ImportDigest,row.SourceJson)))||
            !Same(mappings.Select(row=>(row.Collection,row.LegacyId,row.Ordinal,row.TargetId,row.Disposition,row.MobileIdentityId,row.AdminAccountId,row.FanEvidenceId)),expected.Mappings.Select(row=>(row.Collection,row.LegacyId,row.Ordinal,row.TargetId,row.Disposition,row.MobileIdentityId,row.AdminAccountId,row.FanEvidenceId)))||
            !Same(issues.Select(row=>(row.Collection,row.Ordinal,row.Code)),expected.Issues.Select(row=>(row.Collection,row.Ordinal,row.Code)))||
            await database.SecurityAuditEvents.CountAsync(row=>row.Action=="accounts.import"&&row.Outcome=="success"&&row.SubjectDigest==state.Digest&&row.OccurredAt==state.AppliedAt,token)!=1)throw Refused("account_target_reconciliation_failed");
    }
    private static bool Same<T>(IEnumerable<T> actual,IEnumerable<T> expected)
    {var left=actual.ToArray();var right=expected.ToArray();return left.Length==right.Length&&left.ToHashSet().SetEquals(right);}
    private static string? NormalizeObject(string? json)
    {
        if(json is null)return null;
        try
        {
            using var document=JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement.EnumerateObject().OrderBy(row=>row.Name,StringComparer.Ordinal)
                .Select(row=>new{key=row.Name,value=row.Value.GetRawText()}));
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException){return "\0invalid";}
    }
    private static RosterImportApplyException Refused(string code)=>new(code);
}
