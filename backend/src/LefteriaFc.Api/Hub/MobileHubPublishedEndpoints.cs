using System.Globalization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Api.Identity;
namespace LefteriaFc.Api.Hub;

/// <summary>Approved, published education only; never drafts or fan subscriptions.</summary>
public static class MobileHubPublishedEndpoints
{
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
