using System.Text.Json.Serialization;

namespace LefteriaFc.Application.ClubCalendar;

public sealed record MatchSquadPlayerDto([property:JsonRequired]string PlayerId,[property:JsonRequired]string Assignment,[property:JsonRequired]string? Position);
public sealed record MatchStatisticDto([property:JsonRequired]string PlayerId,[property:JsonRequired]int MinutesPlayed,[property:JsonRequired]int Goals,[property:JsonRequired]int Assists);
public sealed record MatchEligiblePlayerDto(string PlayerId,string Name,int Number,string? Position,
    string? LastAssignment,int Starts,int Reserves);
public sealed record MobileMatchWorkspaceDto(string MatchId,string OrganizationId,string HostId,string Mode,
    int Version,string State,bool Completed,bool RosterStale,IReadOnlyList<MatchEligiblePlayerDto> Roster,
    IReadOnlyList<MatchSquadPlayerDto> Squad,IReadOnlyList<MatchStatisticDto> Statistics,string RotationNote,string ReviewReason);
public sealed record MobileMatchWorkspaceBody([property:JsonRequired]string? Action,[property:JsonRequired]int ExpectedVersion,
    IReadOnlyList<MatchSquadPlayerDto>? Squad,IReadOnlyList<MatchStatisticDto>? Statistics,
    string? RotationNote,string? ReviewReason);
