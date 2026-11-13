using LefteriaFc.Api.Identity;
using LefteriaFc.Application.MobileAuth;

namespace LefteriaFc.Api.Hub;

/// <summary>Sandbox Payabl checkout for fans; no browser-settled entitlement.</summary>
public sealed record MobileFanLessonScheduleRequest(string OrganizationId,string Audience);

public static class MobileFanPayablEndpoints
{
    public static IEndpointRouteBuilder MapMobileFanPayabl(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/mobile/hub/fan-subscription/plans",
            (string? organization_id,HttpContext context,
             MobileFanPayablStore store,CancellationToken ct)=>
            {
                Private(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.PlansAsync(MobilePrincipal.Read(context.User),
                        Owner(organization_id,context),ct),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile HUB fan subscriptions (Payabl sandbox)")
            .WithName("GetMobileFanHubPlans")
            .Produces<MobileFanHubPlans>()
            .WithMobileErrors();

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
        endpoints.MapGet("/api/mobile/hub/fan-learning/next",
            (string? organization_id,string? audience,HttpContext context,
             MobileFanLearningStore store,CancellationToken ct)=>
            {
                Private(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.NextAsync(MobilePrincipal.Read(context.User),
                        Owner(organization_id,context),Audience(audience,context),ct),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile HUB fan education")
            .WithName("GetMobileFanNextLesson")
            .Produces<MobileFanNextPage>()
            .WithMobileErrors();
        endpoints.MapPost("/api/mobile/hub/fan-learning/complete",
            (MobileFanLessonCompleteBody body,HttpContext context,
             MobileFanLearningStore store,CancellationToken ct)=>
            {
                Private(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.CompleteAsync(MobilePrincipal.Read(context.User),
                        body.OrganizationId,body.Audience,
                        body.ContentId,body.RevisionId,ct),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile HUB fan education")
            .WithName("CompleteMobileFanNextLesson")
            .Produces<MobileFanLessonCompletion>()
            .WithMobileErrors();
        endpoints.MapPost("/api/mobile/hub/fan-learning/schedule",
            (MobileFanLessonScheduleRequest body,HttpContext context,
             MobileFanLessonScheduleStore store,CancellationToken ct)=>
            {
                Private(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.EnrollAsync(MobilePrincipal.Read(context.User),
                      body.OrganizationId,body.Audience,ct),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile HUB fan education")
            .WithName("EnrollMobileFanPersonalLessonSchedule")
            .Produces<MobileFanLessonSchedule>().WithMobileErrors();

        endpoints.MapGet("/api/mobile/hub/fan-learning/deliveries",
            (string? organization_id,string? audience,HttpContext context,
             MobileFanLessonScheduleStore store,CancellationToken ct)=>
            {
                Private(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.InboxAsync(MobilePrincipal.Read(context.User),
                      Owner(organization_id,context),Audience(audience,context),ct),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile HUB fan education")
            .WithName("GetMobileFanPersonalLessonDeliveries")
            .Produces<MobileFanLessonDeliveryPage>().WithMobileErrors();
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
    private static string Audience(string? audience,HttpContext context)
    {
        if(context.Request.Query["audience"].Count!=1 ||
           audience is not ("parent" or "player"))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid fan learning interest.");
        return audience;
    }
    private static void Private(HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
    }
}
