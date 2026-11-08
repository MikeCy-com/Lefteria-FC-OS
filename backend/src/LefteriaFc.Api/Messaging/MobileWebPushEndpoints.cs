using LefteriaFc.Api.Identity;
using LefteriaFc.Application.Messaging;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Infrastructure.Messaging;

namespace LefteriaFc.Api.Messaging;

/// <summary>Only authenticated members can opt a browser/device into the
/// club's generic push notifications. Unsupported or unconfigured deployments
/// cannot acquire a subscription. No endpoint/key material is returned.</summary>
public static class MobileWebPushEndpoints
{
    public static IEndpointRouteBuilder MapMobileWebPush(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/mobile/app/push",(
                HttpContext context,PostgresMobilePushStore store,
                WebPushDeliverySettings settings,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(async ()=>{
                var actor=Member(context);
                var status=await store.StatusAsync(actor,token);
                return new MobilePushConfigDto(settings.Enabled,settings.PublicKey,
                    status.ActiveDevices);
            },context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithName("GetMobileWebPushConfig")
            .WithTags("Mobile app push")
            .Produces<MobilePushConfigDto>().WithMobileErrors();

        app.MapPost("/api/mobile/app/push/subscriptions",(
                MobilePushSubscriptionBody body,HttpContext context,
                PostgresMobilePushStore store,WebPushDeliverySettings settings,
                CancellationToken token)=>
            MobileIdentityEndpoints.Execute(async ()=>{
                if(!settings.Enabled)throw Disabled();
                var actor=Member(context);
                if(!WebPushEndpointPolicy.TryEndpoint(body.Endpoint,out _)||
                   !WebPushEndpointPolicy.DecodeKey(body.Keys?.P256dh,out var p256dh)||
                   p256dh.Length!=65||p256dh[0]!=4||
                   !WebPushEndpointPolicy.DecodeKey(body.Keys?.Auth,out var auth)||
                   auth.Length!=16)
                    throw Invalid();
                var result=await store.SubscribeAsync(actor,body.Endpoint!,
                    WebPushEndpointPolicy.Base64Url(p256dh),
                    WebPushEndpointPolicy.Base64Url(auth),token);
                if(!result.Subscribed)throw Disabled();
                return result;
            },context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithName("SubscribeMobileWebPushDevice")
            .WithTags("Mobile app push")
            .Produces<MobilePushSubscriptionStateDto>().WithMobileErrors();

        app.MapDelete("/api/mobile/app/push/subscriptions",(
                MobilePushUnsubscribeBody body,HttpContext context,
                PostgresMobilePushStore store,CancellationToken token)=>
            MobileIdentityEndpoints.Execute(async ()=>{
                var actor=Member(context);
                if(!WebPushEndpointPolicy.TryEndpoint(body.Endpoint,out _))
                    throw Invalid();
                return await store.UnsubscribeAsync(actor,body.Endpoint!,token);
            },context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithName("UnsubscribeMobileWebPushDevice")
            .WithTags("Mobile app push")
            .Produces<MobilePushSubscriptionStateDto>().WithMobileErrors();
        return app;
    }

    private static MobileSession Member(HttpContext context)
    {
        var actor=MobilePrincipal.Read(context.User);
        if(actor.Role.ToString().Equals("fan",StringComparison.OrdinalIgnoreCase))
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Only current team members may subscribe.");
        return actor;
    }
    private static MobileAuthException Disabled()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Device push is not configured for this deployment.");
    private static MobileAuthException Invalid()=>new(
        MobileAuthFaultKind.Validation,MobileAuthErrorCode.InvalidInput,
        "Provide a current HTTPS Web Push subscription from a supported browser.");
}
