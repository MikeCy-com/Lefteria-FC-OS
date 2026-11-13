using System.Globalization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Api.Identity;
namespace LefteriaFc.Api.Hub;

/// <summary>Approved, published education only; never drafts or fan subscriptions.</summary>
public static class MobileHubPublishedEndpoints
{
    public sealed record HubLessonCompletionBody(Guid ContentId,Guid RevisionId);

    public static IEndpointRouteBuilder MapMobileHubPublished(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/mobile/hub/education",
            (string? organization_id,string? offset,HttpContext context,
             MobileHubPublishedStore store,CancellationToken token)=>
                MobileIdentityEndpoints.Execute(
                    ()=>store.ReadAsync(MobilePrincipal.Read(context.User),
                        ParseOrganization(organization_id,context),ParseOffset(offset,context),token),context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile education")
            .WithName("GetMobileHubPublishedEducation")
            .WithDescription("Currently authenticated parent/player audience only; returns reviewed and published articles. Age-restricted player entries stay unavailable until authoritative age integration.")
            .Produces<MobileHubPublishedResult>()
            .WithMobileErrors();
        // A new member starts at the earliest eligible approved lesson,
        // independent of publication age or other people's completion state.
        endpoints.MapGet("/api/mobile/hub/learning/next",
            (string? organization_id,HttpContext context,
             MobileHubPublishedStore store,CancellationToken token)=>
            {
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";
                return MobileIdentityEndpoints.Execute(
                    ()=>store.NextAsync(MobilePrincipal.Read(context.User),
                        ParseOrganization(organization_id,context),token),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile education")
            .WithName("GetMobileHubNextLesson")
            .WithDescription("First unfinished approved lesson for this authenticated parent/player, never the global newest lesson.")
            .Produces<MobileHubNextPage>()
            .WithMobileErrors();

        endpoints.MapPost("/api/mobile/hub/learning/complete",
            (string? organization_id,HubLessonCompletionBody body,HttpContext context,
             MobileHubPublishedStore store,CancellationToken token)=>
            {
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";
                return MobileIdentityEndpoints.Execute(
                    ()=>store.CompleteNextAsync(MobilePrincipal.Read(context.User),
                        ParseOrganization(organization_id,context),
                        body.ContentId,body.RevisionId,token),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile education")
            .WithName("CompleteMobileHubLesson")
            .WithDescription("Explicitly complete only this member's currently next approved lesson; rejects stale, guessed and cross-role submissions.")
            .Produces<MobileHubCompletionResult>()
            .WithMobileErrors();

        return endpoints;
    }

    private static string ParseOrganization(string? raw,HttpContext context)
    {
        // Never let framework query binding choose one of several conflicting
        // organization scopes. Authorization must use one unambiguous owner.
        if(context.Request.Query["organization_id"].Count!=1 ||
           raw is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid education organization.");
        return raw;
    }

    private static int ParseOffset(string? raw,HttpContext context)
    {
        if(context.Request.Query["offset"].Count>1 ||
           (raw is not null &&
             (!int.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out var value) ||
               value is <0 or >1000)))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid article offset.");
        return raw is null?0:int.Parse(raw,CultureInfo.InvariantCulture);
    }
}
