using System.Data.Common;
using System.Globalization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.ClubCalendar;

public sealed class MobileCalendarService(MobileAuthService identity,IMobileCalendarRepository calendar)
{
    private static readonly MobileRole[] CalendarRoles=[MobileRole.Parent,MobileRole.Player,MobileRole.Coach,MobileRole.Management];
    private static readonly MobileRole[] OwnRoles=[MobileRole.Parent,MobileRole.Player];
    private static readonly MobileRole[] StaffRoles=[MobileRole.Coach,MobileRole.Management];

    public async Task<MobileCalendarResult> GetMonthAsync(MobileSession session,string? month,string? playerId,int offset,CancellationToken token)
    {
        try
        {
            await PreflightAsync(session,CalendarRoles,token);
            var day=Month(month);playerId=Identifier(playerId);Offset(offset);
            var snapshot=await calendar.GetMonthAsync(session,day,playerId,offset,token);
            RequireReady(snapshot.IdentityAvailable,snapshot.CalendarReady,snapshot.OrganizationReady);
            if(!snapshot.PlayerSelectionValid)throw NotAssigned();
            return new(session.IdentityId,session.Role.ToWireValue(),day.ToString("yyyy-MM",CultureInfo.InvariantCulture),snapshot.TimeZone,
                snapshot.OrganizationId,snapshot.Players,snapshot.SelectedPlayerId,snapshot.Items,snapshot.NextOffset);
        }
        catch(DbException){throw Unavailable();}
    }

    public async Task<MobileAvailabilityResult> SubmitAvailabilityAsync(MobileSession session,MobileAvailabilityCommand command,CancellationToken token)
    {
        try
        {
            await PreflightAsync(session,OwnRoles,token);
            if(command is null)throw InvalidInput();
            var eventId=Identifier(command.EventId,true)!;var playerId=Identifier(command.PlayerId);var kind=Kind(command.EventType);
            if(command.Status is not ("going" or "not_going" or "maybe"))throw InvalidInput();
            var snapshot=await calendar.SubmitAvailabilityAsync(session,new(eventId,playerId,kind,command.Status),token);
            RequireReady(snapshot.IdentityAvailable,snapshot.CalendarReady,snapshot.OrganizationReady);
            RequireDecision(snapshot.Decision);
            if(snapshot.PlayerId is null||snapshot.Kind is not ("match" or "event" or "training"))throw Unavailable();
            return new("Availability submitted",command.Status,snapshot.PlayerId,eventId,snapshot.Kind);
        }
        catch(DbException){throw Unavailable();}
    }

    public async Task<IReadOnlyList<MobileAvailabilityRow>> GetOwnAvailabilityAsync(MobileSession session,CancellationToken token)
    {
        try
        {
            await PreflightAsync(session,OwnRoles,token);
            var snapshot=await calendar.GetOwnAvailabilityAsync(session,token);
            RequireReady(snapshot.IdentityAvailable,snapshot.CalendarReady);
            return snapshot.Rows;
        }
        catch(DbException){throw Unavailable();}
    }

    public async Task<MobileCalendarResponseResult> GetResponsesAsync(MobileSession session,string? eventId,string? kind,int offset,CancellationToken token)
    {
        try
        {
            await PreflightAsync(session,StaffRoles,token);
            eventId=Identifier(eventId,true)!;kind=Kind(kind);Offset(offset);
            var snapshot=await calendar.GetResponsesAsync(session,eventId,kind,offset,token);
            RequireReady(snapshot.IdentityAvailable,snapshot.CalendarReady,snapshot.OrganizationReady);
            RequireDecision(snapshot.Decision);
            return snapshot.Result??throw Unavailable();
        }
        catch(DbException){throw Unavailable();}
    }

    private async Task PreflightAsync(MobileSession session,IReadOnlyList<MobileRole> roles,CancellationToken token)
    {
        var profile=await identity.GetMeAsync(session,token);
        if(!profile.CurrentRoleAvailable)throw AccountUnavailable();
        if(!roles.Contains(session.Role))throw new MobileAuthException(MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.RoleUnavailable,
            "The current role cannot access this calendar operation.");
        if(session.Role==MobileRole.Management)
        {
            // This also persists the sole organization grant, using the identity
            // service's fresh durable selection path. The final repository query
            // must still rejoin current identity, grants and organization context.
            var organization=await identity.GetOrganizationAsync(session,token);
            if(organization.RequiresOrganizationSelection||organization.OrganizationId is null)throw SelectionRequired();
        }
    }

    private static DateOnly Month(string? value)
    {
        if(value is null||value.Length!=7||value[4]!='-'||value.Where((_,index)=>index!=4).Any(character=>character is <'0' or >'9')||
            !DateOnly.TryParseExact(value+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var month)||
            month.Year==9999&&month.Month==12)throw InvalidInput();
        return month;
    }
    private static string? Identifier(string? value,bool required=false)
    {
        if(value is null)return required?throw InvalidInput():null;
        if(value.Length is <1 or >128||value!=value.Trim()||value.Any(char.IsControl))throw InvalidInput();
        return value;
    }
    private static string? Kind(string? value)=>value switch
    {
        null=>null,"fixture" or "match"=>"match","event"=>"event","training"=>"training",_=>throw InvalidInput()
    };
    private static void Offset(int value){if(value is <0 or >100000)throw InvalidInput();}
    private static void RequireReady(bool identityAvailable,bool calendarReady,bool organizationReady=true)
    {
        if(!identityAvailable)throw AccountUnavailable();
        if(!calendarReady)throw Unavailable();
        if(!organizationReady)throw SelectionRequired();
    }
    private static void RequireDecision(CalendarWriteDecision decision)
    {
        switch(decision)
        {
            case CalendarWriteDecision.Applied:return;
            case CalendarWriteDecision.NotFound:throw new MobileAuthException(MobileAuthFaultKind.NotFound,MobileAuthErrorCode.InvalidCalendarInput,"Event not found.");
            case CalendarWriteDecision.NotAssigned:throw NotAssigned();
            case CalendarWriteDecision.Ambiguous:throw new MobileAuthException(MobileAuthFaultKind.BadRequest,MobileAuthErrorCode.CalendarEventAmbiguous,"Specify the event type for this event.");
            case CalendarWriteDecision.Cancelled:throw new MobileAuthException(MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.InvalidCalendarInput,
                "Availability is unavailable for cancelled or archived events.");
            default:throw Unavailable();
        }
    }
    private static MobileAuthException InvalidInput()=>new(MobileAuthFaultKind.BadRequest,MobileAuthErrorCode.InvalidCalendarInput,"Invalid calendar input.");
    private static MobileAuthException AccountUnavailable()=>new(MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.AccountUnavailable,"The current account is unavailable.");
    private static MobileAuthException NotAssigned()=>new(MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.InvalidCalendarInput,"The player or event is unavailable for this account.");
    private static MobileAuthException SelectionRequired()=>new(MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.OrganizationSelectionRequired,"Select an organization before accessing the calendar.");
    private static MobileAuthException Unavailable()=>new(MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.CalendarUnavailable,"The calendar is not available yet.");
}
