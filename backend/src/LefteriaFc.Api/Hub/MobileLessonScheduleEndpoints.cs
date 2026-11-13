using LefteriaFc.Api.Identity;
using LefteriaFc.Application.MobileAuth;

namespace LefteriaFc.Api.Hub;

public static class MobileLessonScheduleEndpoints
{
    public static IEndpointRouteBuilder MapMobileLessonSchedules(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/mobile/hub/learning/schedule",
            (string? organization_id,HttpContext context,
             MobileLessonScheduleStore store,CancellationToken ct)=>
            {
                NoStore(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.EnrollAsync(MobilePrincipal.Read(context.User),
                        Owner(organization_id,context),ct),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile education")
            .WithName("EnrollMobilePersonalLessonSchedule")
            .WithDescription("Start or resume a personal weekly in-app delivery schedule. Never resets when a new learner joins.")
            .Produces<MobileLessonSchedule>().WithMobileErrors();

        endpoints.MapGet("/api/mobile/hub/learning/deliveries",
            (string? organization_id,HttpContext context,
             MobileLessonScheduleStore store,CancellationToken ct)=>
            {
                NoStore(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.InboxAsync(MobilePrincipal.Read(context.User),
                        Owner(organization_id,context),ct),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile education")
            .WithName("GetMobilePersonalLessonDeliveries")
            .WithDescription("Only the current authorized learner's scheduled lesson notification, never withdrawn or later lessons.")
            .Produces<MobileLessonDeliveryInbox>().WithMobileErrors();
        return endpoints;
    }
    private static string Owner(string? owner,HttpContext context)
    {
        if(context.Request.Query["organization_id"].Count!=1 ||
           owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid schedule organization.");
        return owner;
    }
    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
    }
}
