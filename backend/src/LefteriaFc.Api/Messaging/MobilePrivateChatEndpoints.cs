using System.Globalization;
using LefteriaFc.Api.Identity;
using LefteriaFc.Application.Messaging;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Infrastructure.Messaging;

namespace LefteriaFc.Api.Messaging;

public static class MobilePrivateChatEndpoints
{
    public static IEndpointRouteBuilder MapMobilePrivateChat(this IEndpointRouteBuilder app)
    {
        var chat=app.MapGroup("/api/mobile/private")
            .WithTags("Mobile app private messaging")
            .RequireAuthorization(IdentityServices.Policy);

        chat.MapGet("/contacts",(string? organization,string? group,
            HttpContext context,PostgresMobilePrivateChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>store.ContactsAsync(
                MobilePrincipal.Read(context.User),organization??"",group??"",token),context))
            .WithName("GetMobilePrivateContacts")
            .Produces<MobilePrivateContactDirectoryDto>().WithMobileErrors();

        chat.MapGet("/conversations",(HttpContext context,
            PostgresMobilePrivateChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>store.InboxAsync(
                MobilePrincipal.Read(context.User),token),context))
            .WithName("GetMobilePrivateConversations")
            .Produces<MobilePrivateInboxDto>().WithMobileErrors();

        chat.MapPost("/invitations",(MobilePrivateInviteBody body,HttpContext context,
            PostgresMobilePrivateChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>store.InviteAsync(
                MobilePrincipal.Read(context.User),body.OrganizationId,body.GroupId,
                body.RecipientId,token),context))
            .WithName("RequestMobilePrivateConversation")
            .Produces<MobilePrivateConversationDto>().WithMobileErrors();

        chat.MapPost("/conversations/{conversationId:guid}/respond",(
            Guid conversationId,MobilePrivateRespondBody body,HttpContext context,
            PostgresMobilePrivateChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>store.RespondAsync(
                MobilePrincipal.Read(context.User),conversationId,body.Accept,token),context))
            .WithName("RespondMobilePrivateConversation")
            .Produces<MobilePrivateConversationDto>().WithMobileErrors();

        chat.MapGet("/conversations/{conversationId:guid}/messages",(
            Guid conversationId,string? offset,HttpContext context,
            PostgresMobilePrivateChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>store.MessagesAsync(
                MobilePrincipal.Read(context.User),conversationId,
                ParseOffset(context,offset),token),context))
            .WithName("GetMobilePrivateMessages")
            .Produces<MobilePrivateMessagePageDto>().WithMobileErrors();

        chat.MapPost("/conversations/{conversationId:guid}/messages",(
            Guid conversationId,MobilePrivateSendBody body,HttpContext context,
            PostgresMobilePrivateChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>store.SendAsync(
                MobilePrincipal.Read(context.User),conversationId,body.Content,token),context))
            .WithName("SendMobilePrivateMessage")
            .Produces<MobilePrivateMessageDto>().WithMobileErrors();

        chat.MapPut("/conversations/{conversationId:guid}/messages/{messageId:guid}",(
            Guid conversationId,Guid messageId,MobilePrivateEditBody body,HttpContext context,
            PostgresMobilePrivateChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>store.EditAsync(
                MobilePrincipal.Read(context.User),conversationId,messageId,body.Version,
                body.Content,token),context))
            .WithName("EditMobilePrivateMessage")
            .Produces<MobilePrivateMessageDto>()
            .Produces<MobileAuthError>(StatusCodes.Status409Conflict).WithMobileErrors();

        chat.MapDelete("/conversations/{conversationId:guid}/messages/{messageId:guid}",(
            Guid conversationId,Guid messageId,string? version,HttpContext context,
            PostgresMobilePrivateChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>store.DeleteAsync(
                MobilePrincipal.Read(context.User),conversationId,messageId,
                ParseVersion(context,version),token),context))
            .WithName("DeleteMobilePrivateMessage")
            .Produces<MobilePrivateMessageDto>()
            .Produces<MobileAuthError>(StatusCodes.Status409Conflict).WithMobileErrors();

        chat.MapDelete("/conversations/{conversationId:guid}/for-me",(
            Guid conversationId,HttpContext context,PostgresMobilePrivateChatStore store,
            CancellationToken token)=>
            MobileIdentityEndpoints.Execute(()=>store.HideAsync(
                MobilePrincipal.Read(context.User),conversationId,token),context))
            .WithName("HideMobilePrivateConversationForMe")
            .Produces<MobilePrivateHiddenDto>().WithMobileErrors();
        return app;
    }
    private static long ParseVersion(HttpContext context,string? raw)
    {
        if(context.Request.Query["version"].Count!=1||
          !long.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out var value)||value<1)
          throw new MobileAuthException(MobileAuthFaultKind.Validation,
            MobileAuthErrorCode.InvalidInput,"Invalid private message version.");
        return value;
    }
    private static int ParseOffset(HttpContext context,string? raw)
    {
        if(context.Request.Query["offset"].Count>1||
          (raw is not null&&(!int.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out var number)
            ||number is <0 or >100000)))
          throw new MobileAuthException(MobileAuthFaultKind.Validation,
            MobileAuthErrorCode.InvalidInput,"Invalid private message pagination.");
        return raw is null?0:int.Parse(raw,CultureInfo.InvariantCulture);
    }
}
