using LefteriaFc.Api.Identity;
using System.Text.Json.Serialization;
using LefteriaFc.Application.Family;

namespace LefteriaFc.Api.Family;

public sealed record IssueFamilyInvitationBody(string? Phone);
public sealed record IssueGuardianPermissionBody(
    [property:JsonPropertyName("phone")] string? Phone,
    [property:JsonPropertyName("request_id")] Guid RequestId);

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
        // Distinct from the future *recipient* invitation inbox path.
        // Only the currently verified guardian can see their outgoing pending
        // reservations; recipient phones and inactive history are not exposed.
        // Parent sees only the verified children that currently have a live
        // delegate. Recipient identity, phone and grant identifiers stay hidden.
        endpoints.MapGet("/api/mobile/family/active-children",
            (HttpContext context, FamilyInvitationService service,
                CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.ReadActiveForParentAsync(MobilePrincipal.Read(context.User), token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("GetParentActiveFamilyDelegations")
            .Produces<ParentActiveDelegationsResult>()
            .WithMobileErrors();

        endpoints.MapGet("/api/mobile/family/outgoing-invitations",
            (HttpContext context, FamilyInvitationService service,
                CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.ReadOutgoingAsync(MobilePrincipal.Read(context.User), token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("GetParentOutgoingInvitations")
            .Produces<ParentOutgoingInvitationsResult>()
            .WithMobileErrors();

        // This is the recipient's private phone-verified inbox, intentionally
        // separate from the parent-owned outgoing list. No child PII is returned.
        // Only a currently verified fan learns whether their own phone holds
        // a live grant. No grant ID, child ID, phone or guardian data leaves
        // this endpoint, and it does not provide a player access token.
        endpoints.MapGet("/api/mobile/family/active-delegation",
            (HttpContext context, FamilyInvitationService service,
                CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.GetDelegationAvailabilityAsync(MobilePrincipal.Read(context.User), token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("GetRecipientFamilyDelegationAvailability")
            .Produces<FamilyDelegationAvailabilityDto>()
            .WithMobileErrors();

        endpoints.MapGet("/api/mobile/family/invitations",
            (HttpContext context, FamilyInvitationService service,
                CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.ReadRecipientAsync(MobilePrincipal.Read(context.User), token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("GetRecipientPendingFamilyInvitations")
            .Produces<RecipientPendingInvitationsResult>()
            .WithMobileErrors();

        // An OTP-verified fan may accept only the invitation currently tied
        // to their own phone. The database checks current canonical guardian,
        // active child, collision rules and invitation status atomically.
        // This stores a *revocable* delegation but deliberately never issues
        // a player token, adds a player role or unlocks player-data endpoints.
        endpoints.MapPost("/api/mobile/family/invitations/{invitationId:guid}/accept",
            (Guid invitationId, HttpContext context, FamilyInvitationService service,
                CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.AcceptGrantAsync(MobilePrincipal.Read(context.User), invitationId, token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("AcceptVerifiedRecipientFamilyInvitation")
            .Produces<FamilyOperationResult>()
            .Produces<MobileAuthError>(409).WithMobileErrors();

        endpoints.MapPost("/api/mobile/family/invitations/{invitationId:guid}/decline",
            (Guid invitationId, HttpContext context, FamilyInvitationService service,
                CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.DeclineAsync(MobilePrincipal.Read(context.User), invitationId, token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("DeclineRecipientPendingFamilyInvitation")
            .Produces<FamilyOperationResult>()
            .WithMobileErrors();

        // A currently verified canonical guardian can revoke an existing
        // accepted delegation. The protected SQL operation clears both grant
        // and invitation atomically; it never creates player access.
        endpoints.MapPost("/api/mobile/family/children/{playerId}/revoke",
            (string playerId, HttpContext context, FamilyInvitationService service,
                CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.RevokeGrantAsync(MobilePrincipal.Read(context.User), playerId, token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("RevokeCurrentFamilyPlayerDelegation")
            .Produces<FamilyOperationResult>()
            .Produces<MobileAuthError>(409).WithMobileErrors();

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

        // Separate from the one-child player invitation. The caller must
        // have a current verified parent relationship for each child.
        endpoints.MapGet("/api/mobile/family/guardian-permissions",
            (HttpContext context, GuardianPermissionService service, CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.ListAsync(MobilePrincipal.Read(context.User), token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("GetIssuedGuardianPermissions")
            .Produces<GuardianPermissionsResult>().WithMobileErrors();

        endpoints.MapPost("/api/mobile/family/children/{playerId}/guardian-permissions",
            (string playerId, IssueGuardianPermissionBody body,
                HttpContext context, GuardianPermissionService service, CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.IssueAsync(MobilePrincipal.Read(context.User),
                        playerId, body.Phone, body.RequestId, token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("IssueGuardianPermission")
            .Produces<GuardianPermissionDto>().Produces<MobileAuthError>(409).WithMobileErrors();

        endpoints.MapPost("/api/mobile/family/guardian-permissions/{permissionId:guid}/revoke",
            (Guid permissionId, HttpContext context, GuardianPermissionService service,
                CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => service.RevokeAsync(MobilePrincipal.Read(context.User),
                        permissionId, token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile family")
            .WithName("RevokeIssuedGuardianPermission")
            .Produces<FamilyOperationResult>().WithMobileErrors();

        return endpoints;
    }
}
