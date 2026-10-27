namespace LefteriaFc.Infrastructure.AccountImport;

public sealed class AccountImportStateRecord
{
    public string Id { get; set; }="initial";
    public string Digest { get; set; }="";
    public string RosterDigest { get; set; }="";
    public string StaffDigest { get; set; }="";
    public string SourceCommit { get; set; }="";
    public string Environment { get; set; }="";
    public DateTimeOffset ExportedAt { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    public string CountsJson { get; set; }="{}";
    public byte[] ManifestBytes { get; set; }=[];
}
public sealed class AccountSourceDocumentRecord
{
    public string Collection { get; set; }="";
    public int Ordinal { get; set; }
    public string? LegacyId { get; set; }
    public Guid? TargetId { get; set; }
    public string Disposition { get; set; }="";
    public string ImportDigest { get; set; }="";
    public string SourceJson { get; set; }="";
}
public sealed class AccountImportIssueRecord
{
    public string Collection { get; set; }="";
    public int Ordinal { get; set; }
    public string Code { get; set; }="";
}
public sealed class LegacyAccountMappingRecord
{
    public string Collection { get; set; }="";
    public string LegacyId { get; set; }="";
    public int Ordinal { get; set; }
    public Guid TargetId { get; set; }
    public string Disposition { get; set; }="";
    public Guid? MobileIdentityId { get; set; }
    public Guid? AdminAccountId { get; set; }
    public Guid? FanEvidenceId { get; set; }
}
public sealed class FanRegistrationEvidenceRecord
{
    public Guid Id { get; set; }
    public string LegacyId { get; set; }="";
    public string NormalizedPhone { get; set; }="";
    public string FirstName { get; set; }="";
    public string LastName { get; set; }="";
    public DateOnly Birthday { get; set; }
    public bool IsActive { get; set; }
    public string? AvatarUrl { get; set; }
    public string? FavouritePlayerId { get; set; }
    public string? NotificationPreferencesJson { get; set; }
}
public sealed class CanonicalContactBindingRecord
{
    public string Collection { get; set; }="";
    public string SourceId { get; set; }="";
    public string Kind { get; set; }="";
    public string NormalizedPhone { get; set; }="";
    public string? PlayerId { get; set; }
    public string? StaffId { get; set; }
    public string ImportDigest { get; set; }="";
}
public sealed class MobileRecordLinkRecord
{
    public Guid IdentityId { get; set; }
    public string Role { get; set; }="";
    public string NormalizedPhone { get; set; }="";
    public string Collection { get; set; }="";
    public string RecordId { get; set; }="";
    public string Kind { get; set; }="";
    public bool IsPrimary { get; set; }
}
public sealed class AdminStaffLinkRecord
{
    public Guid AdminAccountId { get; set; }
    public string StaffId { get; set; }="";
}
public sealed class ImportedAdminGrantRecord
{
    public Guid AdminAccountId { get; set; }
    public string OrganizationId { get; set; }="";
    public string Provenance { get; set; }="";
}
