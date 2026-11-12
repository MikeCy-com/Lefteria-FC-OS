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

    public static IEndpointRouteBuilder MapMobileCoachFeedback(this IEndpointRouteBuilder endpoints)
    {
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
}
