using System.Text.Json.Serialization;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.ClubCalendar;

public sealed record CalendarPlayerOption(string Id,string Name,string OrganizationId);
public sealed record CalendarOwnResponse(string PlayerId,string Status,DateTimeOffset? UpdatedAt);
public sealed record MobileCalendarItem(string Id,string Kind,string EventType,string OrganizationId,
    string Title,DateOnly Date,DateOnly EndDate,string? StartTime,DateTimeOffset? StartsAt,DateTimeOffset? EndsAt,
    string? ArrivalTime,int? DurationMinutes,string? Location,string? LocationUrl,string? Description,
    string? MemberSummary,string? SessionJourney,string? Preparation,string? Status,bool IsCancelled,
    string? CancelReason,string? ContentStatus,IReadOnlyList<CalendarOwnResponse> Responses);
public sealed record MobileCalendarResult(Guid IdentityId,string Role,string Month,string TimeZone,string? OrganizationId,
    IReadOnlyList<CalendarPlayerOption> Players,string? SelectedPlayerId,IReadOnlyList<MobileCalendarItem> Items,int? NextOffset);
public sealed record MobileAvailabilityCommand(string? EventId,string? PlayerId,string? EventType,string Status);
public sealed record MobileAvailabilityResult(string Message,string Status,string PlayerId,string EventId,string EventType);
public sealed record MobileAvailabilityRow(string EventId,string EventType,string PlayerId,string Status,DateTimeOffset? UpdatedAt);
public sealed record MobileCalendarResponsePlayer(string Id,string Name,int Number,string Position,string? ImageUrl,string? Status,DateTimeOffset? UpdatedAt);
public sealed record MobileCalendarResponseResult(string EventId,string EventType,string OrganizationId,
    IReadOnlyList<MobileCalendarResponsePlayer> Players,int? NextOffset);

public enum CalendarWriteDecision{Applied,NotFound,NotAssigned,Ambiguous,Cancelled}
public sealed record CalendarMonthSnapshot(bool IdentityAvailable,bool CalendarReady,bool OrganizationReady,
    string? OrganizationId,string TimeZone,IReadOnlyList<CalendarPlayerOption> Players,string? SelectedPlayerId,
    bool PlayerSelectionValid,IReadOnlyList<MobileCalendarItem> Items,int? NextOffset);
public sealed record CalendarWriteSnapshot(bool IdentityAvailable,bool CalendarReady,bool OrganizationReady,
    CalendarWriteDecision Decision,string? PlayerId,string? Kind);
public sealed record CalendarAvailabilitySnapshot(bool IdentityAvailable,bool CalendarReady,
    IReadOnlyList<MobileAvailabilityRow> Rows);
public sealed record CalendarResponsesSnapshot(bool IdentityAvailable,bool CalendarReady,bool OrganizationReady,
    CalendarWriteDecision Decision,MobileCalendarResponseResult? Result);

public interface IMobileCalendarRepository
{
    // Strictly read-only member projection for an OTP-verified delegated fan.
    // Repository MUST validate live grant in the same SQL statement as data.
    Task<CalendarMonthSnapshot> GetDelegatedMonthAsync(
        MobileSession session,DateOnly month,int offset,CancellationToken token);
    Task<CalendarMonthSnapshot> GetMonthAsync(MobileSession session,DateOnly month,string? playerId,int offset,CancellationToken token);
    Task<CalendarWriteSnapshot> SubmitAvailabilityAsync(MobileSession session,MobileAvailabilityCommand command,CancellationToken token);
    // Fan RSVP is authorized independently by the exact live family grant
    // in the same SQL statement that persists the response and audit.
    Task<CalendarWriteSnapshot> SubmitDelegatedAvailabilityAsync(MobileSession session,MobileAvailabilityCommand command,CancellationToken token);
    Task<CalendarAvailabilitySnapshot> GetOwnAvailabilityAsync(MobileSession session,CancellationToken token);
    Task<CalendarResponsesSnapshot> GetResponsesAsync(MobileSession session,string eventId,string? kind,int offset,CancellationToken token);
}
