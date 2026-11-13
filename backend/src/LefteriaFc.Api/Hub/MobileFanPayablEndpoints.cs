using LefteriaFc.Api.Identity;
using LefteriaFc.Application.MobileAuth;

namespace LefteriaFc.Api.Hub;

/// <summary>Sandbox Payabl checkout for fans; no browser-settled entitlement.</summary>
public static class MobileFanPayablEndpoints
{
    public static IEndpointRouteBuilder MapMobileFanPayabl(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/mobile/hub/fan-subscription",
            (string? organization_id,HttpContext context,
             MobileFanPayablStore store,CancellationToken ct)=>
            {
                Private(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.StatusAsync(MobilePrincipal.Read(context.User),
                        Owner(organization_id,context),ct),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile HUB fan subscriptions (Payabl sandbox)")
            .WithName("GetMobileFanHubSubscription")
            .Produces<MobileFanHubStatus>()
            .WithMobileErrors();

        endpoints.MapPost("/api/mobile/hub/fan-subscription/checkout",
            (MobileFanHubStartBody body,HttpContext context,
             MobileFanPayablStore store,CancellationToken ct)=>
            {
                Private(context);
                var ipv4=context.Connection.RemoteIpAddress?.MapToIPv4().ToString()
                    ?? "127.0.0.1";
                return MobileIdentityEndpoints.Execute(
                    ()=>store.StartAsync(MobilePrincipal.Read(context.User),
                        body,ipv4,ct),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile HUB fan subscriptions (Payabl sandbox)")
            .WithName("StartMobileFanPayablCheckout")
            .Produces<MobileFanHubCheckout>()
            .WithMobileErrors();
        return endpoints;
    }
    private static string Owner(string? owner,HttpContext context)
    {
        if(context.Request.Query["organization_id"].Count!=1 ||
           owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid HUB organization.");
        return owner;
    }
    private static void Private(HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
    }
}
