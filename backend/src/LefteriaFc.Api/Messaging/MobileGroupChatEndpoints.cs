using System.Globalization;
using LefteriaFc.Api.Identity;
using LefteriaFc.Application.Messaging;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Infrastructure.Messaging;

namespace LefteriaFc.Api.Messaging;

public static class MobileGroupChatEndpoints
{
    public static IEndpointRouteBuilder MapMobileGroupChat(this IEndpointRouteBuilder app)
    {
        var chat=app.MapGroup("/api/mobile/chat")
            .WithTags("Mobile app messaging")
            .RequireAuthorization(IdentityServices.Policy);
        chat.MapGet("/rooms",(HttpContext context,PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.RoomsAsync(MobilePrincipal.Read(context.User),token),context))
            .WithName("GetMobileChatRooms").Produces<MobileChatRoomsDto>().WithMobileErrors();

        chat.MapGet("/rooms/{organization}/{group}/messages",(
                string organization,string group,string? offset,HttpContext context,
                PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.MessagesAsync(MobilePrincipal.Read(context.User),
                    organization,group,ParseOffset(context,offset),token),context))
            .WithName("GetMobileChatMessages").Produces<MobileChatPageDto>().WithMobileErrors();

        chat.MapPost("/rooms/{organization}/{group}/messages",(
                string organization,string group,MobileChatSendBody body,HttpContext context,
                PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.SendAsync(MobilePrincipal.Read(context.User),
                    organization,group,body.Content,token),context))
            .WithName("SendMobileChatMessage").Produces<MobileChatMessageDto>()
            .Produces(StatusCodes.Status413PayloadTooLarge).WithMobileErrors();
        return app;
    }

    private static int ParseOffset(HttpContext context,string? raw)
    {
        if(context.Request.Query["offset"].Count>1 ||
           (raw is not null && (!int.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out var value)
              || value is <0 or >100000)))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid message pagination.");
        return raw is null ? 0 : int.Parse(raw,CultureInfo.InvariantCulture);
    }
}
