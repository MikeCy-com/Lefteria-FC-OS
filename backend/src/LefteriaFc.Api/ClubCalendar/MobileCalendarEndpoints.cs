using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.Identity;
using LefteriaFc.Application.ClubCalendar;
using LefteriaFc.Application.MobileAuth;
using Microsoft.AspNetCore.Mvc;

namespace LefteriaFc.Api.ClubCalendar;

public sealed record MobileAvailabilityBody(
    [property:JsonPropertyName("event_id")]string EventId,
    [property:JsonPropertyName("player_id")]string? PlayerId=null,
    [property:JsonPropertyName("event_type")]string? EventType=null,
    string Status="going")
{
    public override string ToString()=>"Protected mobile availability request";
}

public static class MobileCalendarEndpoints
{
    private const int MaximumBodyBytes=16*1024;

    public static IEndpointRouteBuilder MapMobileCalendar(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/mobile/calendar",(string? month,[FromQuery(Name="player_id")]string? playerId,string? offset,
                HttpContext context,MobileCalendarService service,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>service.GetMonthAsync(MobilePrincipal.Read(context.User),
                SingleQuery(context,"month",month),SingleQuery(context,"player_id",playerId),
                ParseOffset(SingleQuery(context,"offset",offset)),token),context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile calendar").WithName("GetMobileCalendar")
            .WithDescription("Returns a scoped calendar month. month is required in YYYY-MM form; offset is an integer from 0 to 100000. Each page contains at most 200 items. player_id selects an already authorized player and grants no access.")
            .Produces<MobileCalendarResult>().WithMobileErrors();

        // Separate read-only member-calendar view for an OTP-verified fan with
        // exactly one current, revocable guardian-delegated player. This
        // endpoint never accepts player_id or grants player-role RSVP rights.
        endpoints.MapGet("/api/mobile/family/calendar",
            (string? month,string? offset,HttpContext context,
                MobileCalendarService service,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>
            {
                if(context.Request.Query.Keys.Any(key=>key is not ("month" or "offset")))
                    throw InvalidInput();
                // This is child-specific information. Never allow a browser,
                // proxy, or shared cache to reuse a now-revoked response.
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";
                return service.GetDelegatedMonthAsync(MobilePrincipal.Read(context.User),
                    SingleQuery(context,"month",month),
                    ParseOffset(SingleQuery(context,"offset",offset)),token);
            },context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile family")
            .WithName("GetDelegatedFamilyCalendar")
            .WithDescription("Read-only member calendar for the exact player whose accepted family delegation is live at the same SQL statement as event visibility. Requires OTP fan role. No player selector, staff projection, RSVP write, or player-role elevation.")
            .Produces<MobileCalendarResult>().WithMobileErrors();

        endpoints.MapPost("/api/mobile/availability",(HttpContext context,MobileCalendarService service,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(async()=>
            {
                var session=MobilePrincipal.Read(context.User);
                var command=await ReadAvailabilityAsync(context.Request,token);
                return await service.SubmitAvailabilityAsync(session,command,token);
            },context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile calendar").WithName("SubmitMobileAvailability")
            .WithDescription("Submits the current parent/player's RSVP. Status is going, not_going or maybe and defaults to going only when omitted. event_type is match, fixture (match alias), event or training; omission requires a unique event ID. Cancelled and archived events reject new RSVPs; past events otherwise remain eligible. Unknown properties are ignored; duplicate JSON keys and bodies over 16 KiB are rejected.")
            .Accepts<MobileAvailabilityBody>("application/json").Produces<MobileAvailabilityResult>()
            .Produces<MobileAuthError>(StatusCodes.Status413PayloadTooLarge).WithMobileErrors();

        // OTP-verified and parent-authorized delegate RSVP. Separate from
        // regular /availability so a fan cannot impersonate a player role or
        // choose a child in the request body; final write checks live grant.
        endpoints.MapPost("/api/mobile/family/availability",
            (HttpContext context,MobileCalendarService service,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(async()=>
            {
                var session=MobilePrincipal.Read(context.User);
                var command=await ReadAvailabilityAsync(context.Request,token);
                return await service.SubmitDelegatedAvailabilityAsync(session,command,token);
            },context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile family")
            .WithName("SubmitDelegatedFamilyAvailability")
            .WithDescription("Submits an RSVP for the sole active, phone-verified, accepted family delegation. No player_id selector, player-role elevation or staff access. Each mutation revalidates the current guardian, child and revocation inside the final SQL statement.")
            .Accepts<MobileAvailabilityBody>("application/json")
            .Produces<MobileAvailabilityResult>()
            .Produces<MobileAuthError>(StatusCodes.Status413PayloadTooLarge)
            .WithMobileErrors();

        endpoints.MapGet("/api/mobile/my-availability",(HttpContext context,MobileCalendarService service,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>service.GetOwnAvailabilityAsync(MobilePrincipal.Read(context.User),token),context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile calendar").WithName("GetMobileOwnAvailability")
            .WithDescription("Returns RSVP rows only for players currently linked to the authenticated parent/player identity.")
            .Produces<IReadOnlyList<MobileAvailabilityRow>>().WithMobileErrors();

        endpoints.MapGet("/api/mobile/calendar/{event_type}/{event_id}/responses",([FromRoute(Name="event_id")]string eventId,
                [FromRoute(Name="event_type")]string kind,string? offset,HttpContext context,MobileCalendarService service,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>service.GetResponsesAsync(MobilePrincipal.Read(context.User),eventId,
                kind,ParseOffset(SingleQuery(context,"offset",offset)),token),context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile calendar").WithName("GetMobileEventResponses")
            .WithDescription("Returns scoped player RSVPs for a currently authorized coach/management identity. These are availability responses, not physical attendance or read receipts. The required event_type is match, fixture, event or training. offset is an integer from 0 to 100000; each page contains at most 200 players.")
            .Produces<MobileCalendarResponseResult>().WithMobileErrors();
        return endpoints;
    }

    private static string? SingleQuery(HttpContext context,string name,string? value)
    {
        if(context.Request.Query[name].Count>1)throw InvalidInput();
        return value;
    }
    private static int ParseOffset(string? value)
    {
        if(value is null)return 0;
        if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var offset))throw InvalidInput();
        return offset;
    }

    private static async Task<MobileAvailabilityCommand> ReadAvailabilityAsync(HttpRequest request,CancellationToken token)
    {
        if(request.ContentLength>MaximumBodyBytes)throw TooLarge();
        if(!request.HasJsonContentType())throw InvalidInput();
        try
        {
            // The extra byte proves the bound for chunked requests too, without
            // trusting Content-Length or buffering an unbounded request stream.
            var bytes=new byte[MaximumBodyBytes+1];var count=0;
            while(count<bytes.Length)
            {
                var read=await request.Body.ReadAsync(bytes.AsMemory(count),token);
                if(read==0)break;
                count+=read;
            }
            if(count>MaximumBodyBytes)throw TooLarge();
            _=new UTF8Encoding(false,true).GetCharCount(bytes,0,count);
            using var document=JsonDocument.Parse(bytes.AsMemory(0,count),new JsonDocumentOptions{MaxDepth=32});
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)throw InvalidInput();
            DuplicateKeys(root);
            return new(String(root,"event_id",true),String(root,"player_id",allowNull:true),
                String(root,"event_type",allowNull:true),String(root,"status",fallback:"going")!);
        }
        catch(BadHttpRequestException exception)when(exception.StatusCode==StatusCodes.Status413PayloadTooLarge){throw TooLarge();}
        catch(BadHttpRequestException){throw InvalidInput();}
        catch(DecoderFallbackException){throw InvalidInput();}
        catch(JsonException){throw InvalidInput();}
        catch(InvalidOperationException){throw InvalidInput();}
    }
    private static string? String(JsonElement root,string field,bool required=false,bool allowNull=false,string? fallback=null)
    {
        if(!root.TryGetProperty(field,out var value))return required?throw InvalidInput():fallback;
        if(value.ValueKind==JsonValueKind.Null&&allowNull)return null;
        if(value.ValueKind!=JsonValueKind.String)throw InvalidInput();
        return value.GetString();
    }
    private static void DuplicateKeys(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in value.EnumerateObject())
            {
                if(!names.Add(property.Name))throw InvalidInput();
                DuplicateKeys(property.Value);
            }
        }
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())DuplicateKeys(item);
    }
    private static MobileAuthException InvalidInput()=>new(MobileAuthFaultKind.BadRequest,MobileAuthErrorCode.InvalidCalendarInput,"Invalid calendar input.");
    private static MobileAuthException TooLarge()=>new(MobileAuthFaultKind.PayloadTooLarge,MobileAuthErrorCode.InvalidCalendarInput,"Calendar request body is too large.");
}
