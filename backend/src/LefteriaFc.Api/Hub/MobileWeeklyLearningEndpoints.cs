using LefteriaFc.Api.Identity;
using LefteriaFc.Application.MobileAuth;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// Private in-app weekly lesson delivery. Subscription selection never
/// produces a fan role or bypasses Payabl verification in PostgreSQL.
/// </summary>
public static class MobileWeeklyLearningEndpoints
{
    public static IEndpointRouteBuilder MapMobileWeeklyLearning(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/mobile/hub/weekly/enroll",
            (string? organization_id,MobileWeeklyLearningJoin body,
             HttpContext context,MobileWeeklyLearningStore store,
             CancellationToken token)=>
            {
                NoStore(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.EnrollAsync(MobilePrincipal.Read(context.User),
                        Organization(organization_id,context),
                        body.Audience,token),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile education")
            .WithName("EnrollMobileWeeklyEducation")
            .WithDescription("Start a persistent personal Week 1 delivery schedule; fans require independently verified active Payabl subscription.")
            .Produces<MobileWeeklyLearningEnrollment>()
            .WithMobileErrors();

        endpoints.MapGet("/api/mobile/hub/weekly/inbox",
            (string? organization_id,string? audience,
             HttpContext context,MobileWeeklyLearningStore store,
             CancellationToken token)=>
            {
                NoStore(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.InboxAsync(MobilePrincipal.Read(context.User),
                        Organization(organization_id,context),
                        Audience(audience,context),token),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile education")
            .WithName("GetMobileWeeklyEducationInbox")
            .WithDescription("Read only this member's scheduled reviewed lessons; live Payabl fan entitlement is rechecked on each read.")
            .Produces<MobileWeeklyLearningInbox>()
            .WithMobileErrors();
        return endpoints;
    }

    private static string Organization(string? raw,HttpContext context)
    {
        if(context.Request.Query["organization_id"].Count!=1 ||
           raw is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid learning organization.");
        return raw;
    }

    private static string Audience(string? raw,HttpContext context)
    {
        if(context.Request.Query["audience"].Count!=1 ||
           raw is not ("parent" or "player" or "fan_parent" or "fan_player"))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid learning audience.");
        return raw;
    }

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
    }
}
