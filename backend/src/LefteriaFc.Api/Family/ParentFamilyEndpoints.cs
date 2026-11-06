using LefteriaFc.Api.Identity;
using LefteriaFc.Application.Family;

namespace LefteriaFc.Api.Family;

public sealed record IssueFamilyInvitationBody(string? Phone);

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
        endpoints.MapPost("/api/mobile/family/children/{playerId}/invitations",
            (string playerId, IssueFamilyInvitationBody body, HttpContext context,
                FamilyInvitationService service, CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.IssueAsync(MobilePrincipal.Read(context.User), playerId, body.Phone, token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("IssuePendingFamilyInvitation")
            .Produces<PendingFamilyInvitationDto>()
            .Produces<MobileAuthError>(409).WithMobileErrors();

        endpoints.MapPost("/api/mobile/family/invitations/{invitationId:guid}/cancel",
            (Guid invitationId, HttpContext context, FamilyInvitationService service,
                CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.CancelAsync(MobilePrincipal.Read(context.User), invitationId, token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("CancelPendingFamilyInvitation")
            .Produces<FamilyOperationResult>()
            .Produces<MobileAuthError>(409).WithMobileErrors();

        return endpoints;
    }
}
