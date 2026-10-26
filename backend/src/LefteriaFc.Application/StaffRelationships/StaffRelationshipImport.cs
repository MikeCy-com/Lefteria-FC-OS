using LefteriaFc.Application.Roster;

namespace LefteriaFc.Application.StaffRelationships;

public sealed record StaffAssignmentInput(string Id, bool FromPlural, bool FromSingular);
public sealed record StaffRelationshipInput(string Id, string Name, string JobRole, string TeamType,
    bool IsActive, bool IsArchived, bool HasExplicitActive, bool HasExplicitArchived,
    IReadOnlyList<StaffAssignmentInput> Teams, IReadOnlyList<StaffAssignmentInput> Groups);
public sealed record ContactProvenanceInput(string Collection, string SourceId, string Field,
    string Kind, string Presence, string? RawValue);
public sealed class ValidatedStaffRelationshipImport
{
    internal ValidatedStaffRelationshipImport(string digest, string rosterDigest, string sourceCommit,
        List<StaffRelationshipInput> staff, List<RosterSourceInput> source, List<ContactProvenanceInput> contacts)
    {
        Digest = digest; RosterDigest = rosterDigest; SourceCommit = sourceCommit;
        Staff = staff.AsReadOnly(); Source = source.AsReadOnly(); Contacts = contacts.AsReadOnly();
    }
    public string Digest { get; }
    public string RosterDigest { get; }
    public string SourceCommit { get; }
    public IReadOnlyList<StaffRelationshipInput> Staff { get; }
    public IReadOnlyList<RosterSourceInput> Source { get; }
    public IReadOnlyList<ContactProvenanceInput> Contacts { get; }
    public override string ToString() => "Validated owner-only staff relationship import";
}

public sealed record StaffRelationshipApplyResult(bool Applied, bool Noop, string Digest,
    int Staff, int TeamAssignments, int GroupAssignments, int GroupCoaches, int Contacts);
