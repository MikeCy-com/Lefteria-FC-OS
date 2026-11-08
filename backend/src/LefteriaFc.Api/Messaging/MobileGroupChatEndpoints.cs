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
        chat.MapGet("/groups/{organization}/{group}/messages",(
                string organization,string group,string? offset,HttpContext context,
                PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.MessagesAsync(MobilePrincipal.Read(context.User),
                    organization,group,ParseOffset(context,offset),token),context))
            .WithName("GetMobileChatMessages").Produces<MobileChatPageDto>().WithMobileErrors();

        chat.MapPost("/groups/{organization}/{group}/messages",(
                string organization,string group,MobileChatSendBody body,HttpContext context,
                PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.SendAsync(MobilePrincipal.Read(context.User),
                    organization,group,body.Content,token),context))
            .WithName("SendMobileChatMessage").Produces<MobileChatMessageDto>()
            .Produces(StatusCodes.Status413PayloadTooLarge).WithMobileErrors();
        chat.MapPut("/groups/{organization}/{group}/messages/{messageId:guid}",(
                string organization,string group,Guid messageId,MobileChatEditBody body,
                HttpContext context,PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.EditAsync(MobilePrincipal.Read(context.User),
                    organization,group,messageId,body.Version,body.Content,token),context))
            .WithName("EditMobileChatMessage").Produces<MobileChatMessageDto>()
            .Produces<MobileAuthError>(StatusCodes.Status409Conflict).WithMobileErrors();

        chat.MapDelete("/groups/{organization}/{group}/messages/{messageId:guid}",(
                string organization,string group,Guid messageId,string? version,HttpContext context,
                PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.DeleteAsync(MobilePrincipal.Read(context.User),
                    organization,group,messageId,ParseVersion(context,version),token),context))
            .WithName("DeleteMobileChatMessage").Produces<MobileChatMessageDto>()
            .Produces<MobileAuthError>(StatusCodes.Status409Conflict).WithMobileErrors();

        chat.MapPost("/groups/{organization}/{group}/messages/{messageId:guid}/reactions",(
                string organization,string group,Guid messageId,
                MobileChatReactionBody body,HttpContext context,
                PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.SetReactionAsync(MobilePrincipal.Read(context.User),
                    organization,group,messageId,body.Emoji,body.Active,token),context))
            .WithName("SetMobileChatReaction")
            .Produces<MobileChatReactionSnapshotDto>().WithMobileErrors();

        chat.MapPost("/groups/{organization}/{group}/messages/{messageId:guid}/seen",(
                string organization,string group,Guid messageId,HttpContext context,
                PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.AcknowledgeReadAsync(MobilePrincipal.Read(context.User),
                    organization,group,messageId,token),context))
            .WithName("AcknowledgeMobileChatRead")
            .Produces<MobileChatSeenAckDto>().WithMobileErrors();

        chat.MapGet("/groups/{organization}/{group}/messages/{messageId:guid}/readers",(
                string organization,string group,Guid messageId,HttpContext context,
                PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.ReadersAsync(MobilePrincipal.Read(context.User),
                    organization,group,messageId,token),context))
            .WithName("GetMobileChatReaders")
            .Produces<MobileChatReadersDto>().WithMobileErrors();

        return app;
    }

    private static long ParseVersion(HttpContext context,string? raw)
    {
        if(context.Request.Query["version"].Count!=1 ||
           !long.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out var number) ||
           number<1)
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid message version.");
        return number;
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
