namespace LefteriaFc.Infrastructure.StaffRelationships;

public sealed class StaffRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string JobRole { get; set; } = "";
    public string TeamType { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsArchived { get; set; }
    public bool HasExplicitActive { get; set; }
    public bool HasExplicitArchived { get; set; }
}
public sealed class StaffTeamRecord
{
    public string StaffId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string TeamId { get; set; } = "";
    public bool FromPlural { get; set; }
    public bool FromSingular { get; set; }
}
public sealed class StaffAcademyGroupRecord
{
    public string StaffId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string AcademyGroupId { get; set; } = "";
    public bool FromPlural { get; set; }
    public bool FromSingular { get; set; }
    public bool FromGroupCoach { get; set; }
}
public sealed class AcademyGroupCoachRecord
{
    public string OrganizationId { get; set; } = "";
    public string AcademyGroupId { get; set; } = "";
    public string StaffId { get; set; } = "";
}
public sealed class StaffRelationshipImportStateRecord
{
    public string Id { get; set; } = "initial";
    public string Digest { get; set; } = "";
    public string RosterDigest { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string Environment { get; set; } = "";
    public int StaffCount { get; set; }
    public int TeamAssignmentsCount { get; set; }
    public int GroupAssignmentsCount { get; set; }
    public int GroupCoachesCount { get; set; }
    public int ContactsCount { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}
public sealed class StaffSourceDocumentRecord
{
    public string SourceId { get; set; } = "";
    public string ImportDigest { get; set; } = "";
    public string SourceJson { get; set; } = "";
}
public sealed class ContactProvenanceRecord
{
    public string SourceCollection { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Field { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Presence { get; set; } = "";
    public string? RawValue { get; set; }
    public string? PlayerId { get; set; }
    public string? StaffId { get; set; }
    public string SourceDigest { get; set; } = "";
    public string ImportDigest { get; set; } = "";
}
