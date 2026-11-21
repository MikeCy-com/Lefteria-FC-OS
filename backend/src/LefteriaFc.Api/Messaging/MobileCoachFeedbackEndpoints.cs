using System.Globalization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Api.Identity;
using LefteriaFc.Infrastructure.Messaging;

namespace LefteriaFc.Api.Messaging;

/// <summary>
/// Only an actively assigned coach can prepare a player-specific feedback draft.
/// This endpoint does not send a message or attribute AI output to a coach.
/// </summary>
public static class MobileCoachFeedbackEndpoints
{
    public sealed record CoachFeedbackDraftBody(
        string? OrganizationId, string? PlayerId,
        string? Observation, string? FocusPoint, string? Language);

    public sealed record CoachFeedbackSignoffBody(long Version,string? FinalText);
    public sealed record CoachFeedbackSendBody(long Version);

    public static IEndpointRouteBuilder MapMobileCoachFeedback(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/mobile/coach/players/{playerId}/development-goals",
            (string playerId,string? organization_id,HttpContext context,
             PostgresCoachFeedbackDraftStore store,CancellationToken token) =>
            {
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";
                if(context.Request.Query["organization_id"].Count!=1 ||
                    organization_id is not ("lefteria_fc" or "lefteria_fc_academy"))
                    throw new MobileAuthException(MobileAuthFaultKind.Validation,
                        MobileAuthErrorCode.InvalidInput,"Invalid goal organization.");
                return MobileIdentityEndpoints.Execute(
                    ()=>store.PlayerGoalsAsync(MobilePrincipal.Read(context.User),
                        organization_id,playerId,token),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile coach player development")
            .WithName("GetMobileCoachPlayerDevelopmentGoals")
            .WithDescription("Verified exact-group read-only development goal summaries; no event notes, evaluation details, guardian records or documents.")
            .Produces<CoachPlayerGoalSummaryResult>()
            .WithMobileErrors();

        endpoints.MapGet("/api/mobile/coach/feedback/targets",
            (HttpContext context,PostgresCoachFeedbackDraftStore store,CancellationToken token) =>
            {
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";
                return MobileIdentityEndpoints.Execute(
                    () => store.TargetsAsync(MobilePrincipal.Read(context.User),token),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile coach feedback")
            .WithName("GetMobileCoachFeedbackTargets")
            .WithDescription("Only verified active coach's currently assigned players; no private contacts, guardian records or draft content.")
            .Produces<CoachFeedbackTargetsResult>()
            .WithMobileErrors();

        endpoints.MapPost("/api/mobile/coach/feedback/drafts/{id:guid}/send",
            (Guid id,CoachFeedbackSendBody body,HttpContext context,
             PostgresCoachFeedbackDraftStore store,CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => store.SendAsync(MobilePrincipal.Read(context.User),
                        id,body.Version,token),context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile coach feedback")
            .WithName("SendMobileCoachFeedback")
            .WithDescription("Coach explicitly releases an approved message into the exact player's private inbox; no push or team broadcast.")
            .Produces<CoachFeedbackSendResult>()
            .WithMobileErrors();

        endpoints.MapGet("/api/mobile/player/coach-feedback",
            (string? offset,HttpContext context,
             PostgresCoachFeedbackDraftStore store,CancellationToken token) =>
            {
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";
                return MobileIdentityEndpoints.Execute(
                    () => store.InboxAsync(MobilePrincipal.Read(context.User),
                        ParseFeedbackOffset(context,offset),token),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile player education")
            .WithName("GetMobilePlayerCoachFeedback")
            .WithDescription("Current self-verified player reads only personally addressed, coach-signed and explicitly sent feedback.")
            .Produces<CoachFeedbackInboxResult>()
            .WithMobileErrors();

        // Final message must be explicitly signed by its true coach, but
        // signoff is NOT delivery and creates no outbound notification.
        endpoints.MapPost("/api/mobile/coach/feedback/drafts/{id:guid}/approve",
            (Guid id,CoachFeedbackSignoffBody body,HttpContext context,
             PostgresCoachFeedbackDraftStore store,CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => store.ApproveAsync(MobilePrincipal.Read(context.User),
                        id,body.Version,body.FinalText??"",token),context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile coach feedback")
            .WithName("ApproveMobileCoachFeedbackDraft")
            .WithDescription("Same verified coach signs final words, with current roster assignment. Does not send to player.")
            .Produces<CoachFeedbackSignoffResult>()
            .WithMobileErrors();

        endpoints.MapPost("/api/mobile/coach/feedback/drafts",
            (CoachFeedbackDraftBody body, HttpContext context,
             PostgresCoachFeedbackDraftStore store, CancellationToken token) =>
                MobileIdentityEndpoints.Execute(
                    () => store.CreateAsync(MobilePrincipal.Read(context.User),
                        body.OrganizationId ?? "", body.PlayerId ?? "",
                        body.Observation ?? "", body.FocusPoint ?? "",
                        body.Language ?? "", token), context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile coach feedback")
            .WithName("CreateMobileCoachFeedbackDraft")
            .WithDescription("Coach-authored draft only. Requires a current canonical coach-to-player assignment; never sends, approves, publishes or attributes AI-generated words.")
            .Produces<CoachFeedbackDraftResult>()
            .WithMobileErrors();
        return endpoints;
    }

    private static int ParseFeedbackOffset(HttpContext context,string? raw)
    {
        if(context.Request.Query["offset"].Count>1 ||
            (raw is not null &&
             (!int.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out var offset) ||
              offset is <0 or >10000)))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid feedback offset.");
        return raw is null ? 0 : int.Parse(raw,CultureInfo.InvariantCulture);
    }
}
