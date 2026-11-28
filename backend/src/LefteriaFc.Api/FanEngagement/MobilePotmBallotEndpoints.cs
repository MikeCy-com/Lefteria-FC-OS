using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.Identity;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Api.FanEngagement;

public sealed record MobilePotmBallotCommand(string MonthKey,int ExpectedRevision,string? PlayerId)
{
    public override string ToString()=>"Protected POTM ballot request";
}

public sealed record MobilePotmBallot(string MonthKey,bool VotingOpen,string? MyVotePlayerId,int Revision);

public static class MobilePotmBallotEndpoints
{
    private const int MaximumBodyBytes=4096;

    public static IEndpointRouteBuilder MapMobilePotmBallot(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/mobile/potm/ballot",
            (HttpContext context,MobilePotmBallotStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>
            {
                Private(context);
                return store.ReadAsync(Fan(context),token);
            },context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile fan engagement").WithName("GetMobilePotmBallot")
            .WithDescription("Reads only the current verified Fan account's ballot and the owner-reviewed Cyprus month opening gate.")
            .Produces<MobilePotmBallot>().WithMobileErrors();

        endpoints.MapPut("/api/mobile/potm/ballot",
            (HttpContext context,MobilePotmBallotStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(async()=>
            {
                Private(context);
                var actor=Fan(context);
                var command=await ReadCommandAsync(context.Request,token);
                return await store.WriteAsync(actor,command,token);
            },context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile fan engagement").WithName("UpdateMobilePotmBallot")
            .WithDescription("Sets or changes the current Fan ballot for the supplied current Cyprus month and expected revision. Explicit null player_id withdraws. Omitted, duplicate or unknown fields are refused. Closed or conflicting ballots return 409.")
            .Accepts<MobilePotmBallotCommand>("application/json")
            .Produces<MobilePotmBallot>()
            .Produces<MobileAuthError>(409).Produces<MobileAuthError>(413)
            .WithMobileErrors();
        return endpoints;
    }

    private static void Private(HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(context.Request.Query.Count!=0)throw InvalidInput();
    }

    private static MobileSession Fan(HttpContext context)
    {
        var actor=MobilePrincipal.Read(context.User);
        if(actor.Role!=MobileRole.Fan || actor.IdentityId==Guid.Empty || actor.DeviceSessionId==Guid.Empty)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.RoleUnavailable,
                "Απαιτείται επαληθευμένος λογαριασμός φιλάθλου.");
        return actor;
    }

    internal static async Task<MobilePotmBallotCommand> ReadCommandAsync(HttpRequest request,CancellationToken token)
    {
        if(request.ContentLength>MaximumBodyBytes)throw TooLarge();
        if(!request.HasJsonContentType())throw InvalidInput();
        try
        {
            var bytes=new byte[MaximumBodyBytes+1];var count=0;
            while(count<bytes.Length)
            {
                var read=await request.Body.ReadAsync(bytes.AsMemory(count),token);
                if(read==0)break;
                count+=read;
            }
            if(count>MaximumBodyBytes)throw TooLarge();
            _=new UTF8Encoding(false,true).GetCharCount(bytes,0,count);
            using var document=JsonDocument.Parse(bytes.AsMemory(0,count),new JsonDocumentOptions{MaxDepth=8});
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)throw InvalidInput();
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var field in root.EnumerateObject())
                if(field.Name is not ("month_key" or "expected_revision" or "player_id") || !names.Add(field.Name))
                    throw InvalidInput();
            if(names.Count!=3 || !root.TryGetProperty("month_key",out var month) || month.ValueKind!=JsonValueKind.String ||
                !root.TryGetProperty("expected_revision",out var revision) || revision.ValueKind!=JsonValueKind.Number ||
                !revision.TryGetInt32(out var expected) || expected<0 ||
                !root.TryGetProperty("player_id",out var player) ||
                player.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw InvalidInput();
            var monthKey=month.GetString()!;
            var playerId=player.ValueKind==JsonValueKind.Null?null:player.GetString();
            if(!MobilePotmBallotStore.ValidMonth(monthKey) || (playerId is not null && !MobilePotmBallotStore.ValidPlayer(playerId)))
                throw InvalidInput();
            return new(monthKey,expected,playerId);
        }
        catch(BadHttpRequestException error) when(error.StatusCode==StatusCodes.Status413PayloadTooLarge){throw TooLarge();}
        catch(BadHttpRequestException){throw InvalidInput();}
        catch(DecoderFallbackException){throw InvalidInput();}
        catch(JsonException){throw InvalidInput();}
    }

    private static MobileAuthException InvalidInput()=>new(MobileAuthFaultKind.BadRequest,
        MobileAuthErrorCode.InvalidPotmInput,"Μη έγκυρα στοιχεία ψηφοφορίας.");
    private static MobileAuthException TooLarge()=>new(MobileAuthFaultKind.PayloadTooLarge,
        MobileAuthErrorCode.InvalidPotmInput,"Το αίτημα ψηφοφορίας είναι πολύ μεγάλο.");
}
