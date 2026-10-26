namespace LefteriaFc.Infrastructure.Roster;

public sealed class RosterTeamRecord
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string Name { get; set; } = "";
}
public sealed class RosterAcademyGroupRecord
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string Name { get; set; } = "";
}
public sealed class RosterPlayerRecord
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Number { get; set; }
    public string Position { get; set; } = "";
    public string TeamType { get; set; } = "";
    public string? TeamId { get; set; }
    public string? PrimaryAcademyGroupId { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public bool IsArchived { get; set; }
    public bool IsTransferred { get; set; }
    public bool HasExplicitPickerFields { get; set; }
}
public sealed class RosterPlayerAcademyGroupRecord
{
    public string OrganizationId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string AcademyGroupId { get; set; } = "";
}
public sealed class RosterImportStateRecord
{
    public string Id { get; set; } = "initial";
    public string Digest { get; set; } = "";
    public string SourceRepository { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string Environment { get; set; } = "";
    public int TeamsCount { get; set; }
    public int AcademyGroupsCount { get; set; }
    public int PlayersCount { get; set; }
    public int MembershipsCount { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}
public sealed class RosterSourceDocumentRecord
{
    public string Collection { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string ImportDigest { get; set; } = "";
    public string SourceJson { get; set; } = "";
}
