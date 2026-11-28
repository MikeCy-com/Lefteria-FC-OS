using LefteriaFc.Api.Identity;
using LefteriaFc.Application.ClubCalendar;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.ClubCalendar;
using Microsoft.AspNetCore.Mvc;

namespace LefteriaFc.Api.ClubCalendar;

public static class MobileMatchWorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapMobileMatchWorkspace(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/mobile/calendar/match/{event_id}/workspace")
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile match workspace");
        group.MapGet("",([FromRoute(Name="event_id")]string match,HttpContext context,
            PostgresMobileMatchWorkspaceStore store,MobileAuthService identity,CancellationToken token)=>
            {
                context.Response.Headers.CacheControl="private, no-store";
                return MobileIdentityEndpoints.Execute(async()=>
                {
                    var actor=MobilePrincipal.Read(context.User);
                    await PreflightAsync(actor,identity,token);
                    return await store.ReadAsync(actor,match,token);
                },context);
            })
            .WithName("GetMobileMatchWorkspace").Produces<MobileMatchWorkspaceDto>().WithMobileErrors();
        group.MapPost("",([FromRoute(Name="event_id")]string match,MobileMatchWorkspaceBody body,HttpContext context,
            PostgresMobileMatchWorkspaceStore store,MobileAuthService identity,CancellationToken token)=>
            {
                context.Response.Headers.CacheControl="private, no-store";
                return MobileIdentityEndpoints.Execute(async()=>
                {
                    var actor=MobilePrincipal.Read(context.User);
                    await PreflightAsync(actor,identity,token);
                    return await store.WriteAsync(actor,match,body,token);
                },context);
            })
            .WithName("WriteMobileMatchWorkspace").Produces<MobileMatchWorkspaceDto>()
            .Produces<MobileAuthError>(StatusCodes.Status409Conflict).WithMobileErrors();
        return app;
    }

    private static async Task PreflightAsync(MobileSession actor,MobileAuthService identity,CancellationToken token)
    {
        if(actor.Role is not (MobileRole.Coach or MobileRole.Management))
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.RoleUnavailable,"Match workspace requires a staff role.");
        var profile=await identity.GetMeAsync(actor,token);
        if(!profile.CurrentRoleAvailable)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.AccountUnavailable,"The current account is unavailable.");
        if(actor.Role==MobileRole.Management)
        {
            var owner=await identity.GetOrganizationAsync(actor,token);
            if(owner.RequiresOrganizationSelection||owner.OrganizationId is null)
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.OrganizationSelectionRequired,"Select an organization.");
        }
    }
}
