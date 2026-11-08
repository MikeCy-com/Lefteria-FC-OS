using System.Globalization;
using LefteriaFc.Api.Identity;
using LefteriaFc.Application.Messaging;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Infrastructure.Messaging;

namespace LefteriaFc.Api.Messaging;

public static class MobileAppAnnouncementEndpoints
{
    public static IEndpointRouteBuilder MapMobileAppAnnouncements(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/mobile/app/announcements/{organization}/{group}",(
                string organization,string group,string? offset,HttpContext context,
                PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.AppAnnouncementsAsync(MobilePrincipal.Read(context.User),
                    organization,group,ParseOffset(context,offset),token),context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile app announcements")
            .WithName("GetMobileAppAnnouncements")
            .Produces<MobileAppAnnouncementPageDto>().WithMobileErrors();
        app.MapPost("/api/mobile/app/announcements/{organization}/{group}/{announcementId}/reactions",(
                string organization,string group,string announcementId,
                MobileChatReactionBody body,HttpContext context,
                PostgresMobileGroupChatStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(
                ()=>store.SetAppAnnouncementReactionAsync(
                    MobilePrincipal.Read(context.User),organization,group,
                    announcementId,body.Emoji,body.Active,token),context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile app announcements")
            .WithName("SetMobileAppAnnouncementReaction")
            .Produces<MobileAppAnnouncementReactionSnapshotDto>().WithMobileErrors();
        return app;
    }
    private static int ParseOffset(HttpContext context,string? raw)
    {
        if(context.Request.Query["offset"].Count>1||
            (raw is not null && (!int.TryParse(raw,NumberStyles.None,
                CultureInfo.InvariantCulture,out var value)||value is <0 or >100000)))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid announcement pagination.");
        return raw is null?0:int.Parse(raw,CultureInfo.InvariantCulture);
    }
}
