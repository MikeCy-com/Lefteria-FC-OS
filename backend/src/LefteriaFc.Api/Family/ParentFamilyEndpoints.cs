using LefteriaFc.Api.Identity;
using LefteriaFc.Application.Family;

namespace LefteriaFc.Api.Family;

public static class ParentFamilyEndpoints
{
    public static IEndpointRouteBuilder MapParentFamily(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/mobile/family", (HttpContext context, ParentFamilyService service,
            CancellationToken cancellationToken) =>
            MobileIdentityEndpoints.Execute(
                () => service.GetAsync(MobilePrincipal.Read(context.User), cancellationToken), context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile family")
            .WithName("GetParentFamily")
            .Produces<ParentFamilyResult>()
            .WithMobileErrors();
        return endpoints;
    }
}
