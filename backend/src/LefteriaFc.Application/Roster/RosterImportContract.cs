namespace LefteriaFc.Application.Roster;

public sealed record RosterImportIssue(string Code, string? Collection = null, int? Row = null);
public sealed class RosterImportValidationException(RosterImportIssue issue) : Exception("Roster manifest validation failed.")
{
    public RosterImportIssue Issue { get; } = issue;
}
public sealed record RosterTeamInput(string Id, string OrganizationId, string Name);
public sealed record RosterGroupInput(string Id, string OrganizationId, string Name);
public sealed record RosterPlayerInput(string Id, string OrganizationId, string Name, int Number,
    string Position, string TeamType, string? TeamId, string? PrimaryGroupId, IReadOnlyList<string> Groups,
    string? ImageUrl, bool IsActive, bool IsArchived, bool IsTransferred, bool HasExplicitPickerFields);
public sealed class RosterSourceInput(string collection, string sourceId, string json)
{
    public string Collection { get; } = collection;
    public string SourceId { get; } = sourceId;
    public string Json { get; } = json;
    public override string ToString() => "Owner-only roster source document";
}
public sealed class ValidatedRosterImport
{
    internal ValidatedRosterImport(string digest, string commit, List<RosterTeamInput> teams,
        List<RosterGroupInput> groups, List<RosterPlayerInput> players, List<RosterSourceInput> source)
    {
        Digest = digest; SourceCommit = commit;
        Teams = teams.AsReadOnly(); Groups = groups.AsReadOnly(); Players = players.AsReadOnly(); Source = source.AsReadOnly();
    }
    public const string Repository = "MikeCy-com/Lefteria-FC";
    public string Digest { get; }
    public string SourceCommit { get; }
    public IReadOnlyList<RosterTeamInput> Teams { get; }
    public IReadOnlyList<RosterGroupInput> Groups { get; }
    public IReadOnlyList<RosterPlayerInput> Players { get; }
    public IReadOnlyList<RosterSourceInput> Source { get; }
    public int MembershipCount => Players.Sum(player => player.Groups.Count);
    public override string ToString() => "Validated roster import (owner-only source retained)";
}
public sealed class RosterImportApplyException(string code) : Exception("Roster import was refused.")
{
    public string Code { get; } = code;
}
public sealed record RosterImportApplyResult(bool Applied, bool Noop, string Digest);
