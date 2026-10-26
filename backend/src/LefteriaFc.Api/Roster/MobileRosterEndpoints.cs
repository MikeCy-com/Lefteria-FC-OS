using LefteriaFc.Api.Identity;
using LefteriaFc.Application.Roster;

namespace LefteriaFc.Api.Roster;

public static class MobileRosterEndpoints
{
    public static IEndpointRouteBuilder MapMobileRoster(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/mobile/players-list", (HttpContext context, MobileRosterService service, CancellationToken token) =>
            MobileIdentityEndpoints.Execute(() => service.GetPlayersAsync(MobilePrincipal.Read(context.User), token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile roster")
            .WithName("GetMobilePlayersList").Produces<MobilePlayersListResult>().WithMobileErrors();
        return endpoints;
    }
}
