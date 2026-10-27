using LefteriaFc.Application.Roster;
using LefteriaFc.Application.StaffRelationships;

namespace LefteriaFc.Application.AccountImport;

public sealed record AccountSourceRow(string Collection, int Ordinal, string Json)
{
    public override string ToString() => "Protected account source row";
}
public sealed record AccountImportIssue(string Collection, int Ordinal, string Code);
public sealed class ValidatedAccountImport
{
    private readonly byte[] sourceBytes;
    internal ValidatedAccountImport(string digest, string commit, string roster, string staff,
        DateTimeOffset exportedAt, IReadOnlyList<AccountSourceRow> rows, byte[] originalBytes)
    { Digest=digest; SourceCommit=commit; RosterDigest=roster; StaffDigest=staff; ExportedAt=exportedAt; Rows=rows; sourceBytes=originalBytes; }
    public string Digest { get; }
    public string SourceCommit { get; }
    public string RosterDigest { get; }
    public string StaffDigest { get; }
    public DateTimeOffset ExportedAt { get; }
    public IReadOnlyList<AccountSourceRow> Rows { get; }
    public byte[] CopySourceBytes() => sourceBytes.ToArray();
    public override string ToString() => "Validated protected account manifest";
}
public sealed record AccountImportTrustedContext(string RosterDigest, string StaffDigest,
    string SourceCommit, ValidatedRosterImport Roster, ValidatedStaffRelationshipImport Staff)
{
    public override string ToString() => "Verified account import base snapshots";
}
public sealed record PlannedAccountMapping(string Collection, int Ordinal, string LegacyId,
    Guid TargetId, string Disposition)
{
    public override string ToString() => "Protected legacy account mapping";
}
public sealed record PlannedContactBinding(string Collection, string SourceId, string Kind, string Phone)
{
    public override string ToString() => "Protected canonical contact binding";
}
public sealed record PlannedAccountLink(Guid IdentityId, string Role, string Phone,
    string RecordId, string Kind, bool IsPrimary)
{
    public override string ToString() => "Protected canonical identity relationship";
}
// IsActive preserves the source account status. Current roster/admin availability
// is evaluated independently at runtime and must not rewrite this flag.
public sealed record PlannedMobileAccount(Guid Id, string LegacyId, string Phone, string Role,
    string Name, bool IsActive, Guid? AdminId, string? FirstName, string? LastName,
    DateOnly? Birthday, string? AvatarUrl, string? FavoriteTeam, string? Email,
    string? FavouritePlayerId, string? NotificationPreferencesJson, string Platform,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLogin, DateTimeOffset? UpdatedAt)
{
    public override string ToString() => "Protected mobile account";
}
public sealed record PlannedAdminGrant(string OrganizationId, string Provenance);
public sealed record PlannedAdminAccount(Guid Id, string LegacyId, string Username, string Name,
    string Role, string? PasswordHash, string? Phone, bool IsActive, bool InvitePending,
    bool Otp2faEnabled, string? StaffId, IReadOnlyList<PlannedAdminGrant> Grants,
    string? ActiveOrganizationId)
{
    public override string ToString() => "Protected admin account";
}
public sealed record PlannedFanEvidence(Guid Id, string LegacyId, string Phone, string FirstName,
    string LastName, DateOnly Birthday, bool IsActive, string? AvatarUrl, string? FavouritePlayerId,
    string? NotificationPreferencesJson)
{
    public override string ToString() => "Protected pre-verification fan evidence";
}
public sealed class AccountImportPlan
{
    public required ValidatedAccountImport Manifest { get; init; }
    public required IReadOnlyList<PlannedMobileAccount> Mobiles { get; init; }
    public required IReadOnlyList<PlannedAdminAccount> Admins { get; init; }
    public required IReadOnlyList<PlannedFanEvidence> FanEvidence { get; init; }
    public required IReadOnlyList<PlannedAccountMapping> Mappings { get; init; }
    public required IReadOnlyList<PlannedContactBinding> ContactBindings { get; init; }
    public required IReadOnlyList<PlannedAccountLink> Links { get; init; }
    public required IReadOnlyList<AccountImportIssue> Issues { get; init; }
    public int QuarantinedRows => Issues.Select(row => (row.Collection,row.Ordinal)).Distinct().Count();
    public override string ToString() => "Protected reconciled account import plan";
}
