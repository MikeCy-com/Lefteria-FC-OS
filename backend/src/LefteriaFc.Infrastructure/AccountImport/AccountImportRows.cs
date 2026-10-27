using System.Text.Json;
using LefteriaFc.Application.AccountImport;
using LefteriaFc.Infrastructure.Identity;

namespace LefteriaFc.Infrastructure.AccountImport;

internal sealed class AccountImportRows
{
    public required MobileIdentityRecord[] Mobiles { get; init; }
    public required AdminAccountRecord[] Admins { get; init; }
    public required FanRegistrationEvidenceRecord[] Fans { get; init; }
    public required CanonicalContactBindingRecord[] Bindings { get; init; }
    public required MobileRecordLinkRecord[] Links { get; init; }
    public required AdminStaffLinkRecord[] StaffLinks { get; init; }
    public required AdminOrganizationGrantRecord[] Grants { get; init; }
    public required ImportedAdminGrantRecord[] GrantOrigins { get; init; }
    public required AccountSourceDocumentRecord[] Sources { get; init; }
    public required LegacyAccountMappingRecord[] Mappings { get; init; }
    public required AccountImportIssueRecord[] Issues { get; init; }
    public required string CountsJson { get; init; }
    public static AccountImportRows From(AccountImportPlan plan)
    {
        var mappings=plan.Mappings.ToDictionary(row=>(row.Collection,row.Ordinal));
        return new()
        {
            Mobiles=plan.Mobiles.Select(row=>new MobileIdentityRecord
            {
                Id=row.Id,LegacyId=row.LegacyId,NormalizedPhone=row.Phone,Role=row.Role,DisplayName=row.Name,IsActive=row.IsActive,
                ManagementAdminId=row.AdminId,FirstName=row.FirstName,LastName=row.LastName,Birthday=row.Birthday,AvatarUrl=row.AvatarUrl,
                FavoriteTeam=row.FavoriteTeam,Email=row.Email,FavouritePlayerId=row.FavouritePlayerId,NotificationPreferencesJson=row.NotificationPreferencesJson,
                Platform=row.Platform,CreatedAt=row.CreatedAt,LastLogin=row.LastLogin,UpdatedAt=row.UpdatedAt
            }).ToArray(),
            Admins=plan.Admins.Select(row=>new AdminAccountRecord
            {
                Id=row.Id,LegacyId=row.LegacyId,Username=row.Username,DisplayName=row.Name,Role=row.Role,PasswordHash=row.PasswordHash,
                NormalizedPhone=row.Phone,IsActive=row.IsActive,InvitePending=row.InvitePending,Otp2faEnabled=row.Otp2faEnabled,
                ActiveOrganizationId=null // Establish accounts, then grants, then selected context.
            }).ToArray(),
            Fans=plan.FanEvidence.Select(row=>new FanRegistrationEvidenceRecord
            {
                Id=row.Id,LegacyId=row.LegacyId,NormalizedPhone=row.Phone,FirstName=row.FirstName,LastName=row.LastName,Birthday=row.Birthday,
                IsActive=row.IsActive,AvatarUrl=row.AvatarUrl,FavouritePlayerId=row.FavouritePlayerId,NotificationPreferencesJson=row.NotificationPreferencesJson
            }).ToArray(),
            Bindings=plan.ContactBindings.Select(row=>new CanonicalContactBindingRecord
            {
                Collection=row.Collection,SourceId=row.SourceId,Kind=row.Kind,NormalizedPhone=row.Phone,ImportDigest=plan.Manifest.Digest,
                PlayerId=row.Collection=="players"?row.SourceId:null,StaffId=row.Collection=="staff"?row.SourceId:null
            }).ToArray(),
            Links=plan.Links.Select(row=>new MobileRecordLinkRecord
            {IdentityId=row.IdentityId,Role=row.Role,NormalizedPhone=row.Phone,Collection=row.Kind=="staff_own"?"staff":"players",RecordId=row.RecordId,Kind=row.Kind,IsPrimary=row.IsPrimary}).ToArray(),
            StaffLinks=plan.Admins.Where(row=>row.StaffId is not null).Select(row=>new AdminStaffLinkRecord{AdminAccountId=row.Id,StaffId=row.StaffId!}).ToArray(),
            Grants=plan.Admins.SelectMany(row=>row.Grants.Select(grant=>new AdminOrganizationGrantRecord{AdminAccountId=row.Id,OrganizationId=grant.OrganizationId})).ToArray(),
            GrantOrigins=plan.Admins.SelectMany(row=>row.Grants.Select(grant=>new ImportedAdminGrantRecord{AdminAccountId=row.Id,OrganizationId=grant.OrganizationId,Provenance=grant.Provenance})).ToArray(),
            Sources=plan.Manifest.Rows.Select(row=>
            {
                var mapping=mappings.GetValueOrDefault((row.Collection,row.Ordinal));
                using var document=JsonDocument.Parse(row.Json);
                var legacy=document.RootElement.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.String?id.GetString():null;
                if(legacy is not null&&(legacy.Length>128||legacy.Any(char.IsControl)))legacy=null;
                return new AccountSourceDocumentRecord{Collection=row.Collection,Ordinal=row.Ordinal,LegacyId=legacy,TargetId=mapping?.TargetId,
                    Disposition=mapping?.Disposition??"quarantined",ImportDigest=plan.Manifest.Digest,SourceJson=row.Json};
            }).ToArray(),
            Mappings=plan.Mappings.Select(row=>new LegacyAccountMappingRecord{Collection=row.Collection,LegacyId=row.LegacyId,Ordinal=row.Ordinal,
                TargetId=row.TargetId,Disposition=row.Disposition,MobileIdentityId=row.Disposition=="mobile"?row.TargetId:null,
                AdminAccountId=row.Disposition=="admin"?row.TargetId:null,FanEvidenceId=row.Disposition=="fan_evidence"?row.TargetId:null}).ToArray(),
            Issues=plan.Issues.Select(row=>new AccountImportIssueRecord{Collection=row.Collection,Ordinal=row.Ordinal,Code=row.Code}).ToArray(),
            CountsJson=JsonSerializer.Serialize(new{mobile=plan.Mobiles.Count,admin=plan.Admins.Count,fan_evidence=plan.FanEvidence.Count,
                bindings=plan.ContactBindings.Count,links=plan.Links.Count,staff_links=plan.Admins.Count(row=>row.StaffId is not null),
                grants=plan.Admins.Sum(row=>row.Grants.Count),sources=plan.Manifest.Rows.Count,mappings=plan.Mappings.Count,
                issues=plan.Issues.Count,quarantined_rows=plan.QuarantinedRows})
        };
    }
}
