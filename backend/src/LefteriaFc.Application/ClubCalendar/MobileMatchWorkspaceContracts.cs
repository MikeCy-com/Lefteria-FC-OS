namespace LefteriaFc.Application.ClubCalendar;

public sealed record MatchSquadPlayerDto(string PlayerId,string Assignment,string? Position);
public sealed record MatchStatisticDto(string PlayerId,int MinutesPlayed,int Goals,int Assists);
public sealed record MatchEligiblePlayerDto(string PlayerId,string Name,int Number,string? Position,
    string? LastAssignment,int Starts,int Reserves);
public sealed record MobileMatchWorkspaceDto(string MatchId,string OrganizationId,string HostId,string Mode,
    int Version,string State,bool Completed,bool RosterStale,IReadOnlyList<MatchEligiblePlayerDto> Roster,
    IReadOnlyList<MatchSquadPlayerDto> Squad,IReadOnlyList<MatchStatisticDto> Statistics,string RotationNote,string ReviewReason);
public sealed record MobileMatchWorkspaceBody(string? Action,int ExpectedVersion,
    IReadOnlyList<MatchSquadPlayerDto>? Squad,IReadOnlyList<MatchStatisticDto>? Statistics,
    string? RotationNote,string? ReviewReason);
