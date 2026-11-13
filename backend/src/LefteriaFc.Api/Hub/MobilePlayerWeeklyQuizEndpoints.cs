using LefteriaFc.Api.Identity;
using LefteriaFc.Application.MobileAuth;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// Player-only weekly football quizzes. No draft publication or answer keys.
/// The first explicit track start begins Week 1 regardless of other learners.
/// </summary>
public static class MobilePlayerWeeklyQuizEndpoints
{
    public static IEndpointRouteBuilder MapMobilePlayerWeeklyQuiz(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/mobile/hub/player-quizzes/start",
            (string? organization_id,HttpContext context,
             MobilePlayerWeeklyQuizStore store,CancellationToken token)=>
            {
                NoStore(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.StartAsync(MobilePrincipal.Read(context.User),
                      Organization(organization_id,context),token),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile education")
            .WithName("StartMobilePlayerQuizTrack")
            .WithDescription("Start or resume the verified player's individual football quiz journey at Week 1.")
            .Produces<PlayerWeeklyQuizStart>()
            .WithMobileErrors();

        endpoints.MapGet("/api/mobile/hub/player-quizzes/next",
            (string? organization_id,HttpContext context,
             MobilePlayerWeeklyQuizStore store,CancellationToken token)=>
            {
                NoStore(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.ReadAsync(MobilePrincipal.Read(context.User),
                      Organization(organization_id,context),token),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile education")
            .WithName("GetMobilePlayerWeeklyQuiz")
            .WithDescription("Read only the next human-approved, published player quiz eligible in this learner's own week; never expose answer keys.")
            .Produces<PlayerWeeklyQuizPage>()
            .WithMobileErrors();

        endpoints.MapPost("/api/mobile/hub/player-quizzes/submit",
            (string? organization_id,PlayerWeeklyQuizSubmission submission,
             HttpContext context,MobilePlayerWeeklyQuizStore store,
             CancellationToken token)=>
            {
                NoStore(context);
                return MobileIdentityEndpoints.Execute(
                    ()=>store.SubmitAsync(MobilePrincipal.Read(context.User),
                      Organization(organization_id,context),submission,token),context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile education")
            .WithName("SubmitMobilePlayerWeeklyQuiz")
            .WithDescription("Save one private server-graded attempt for the current player quiz; no answer key or public ranking.")
            .Produces<PlayerWeeklyQuizScore>()
            .WithMobileErrors();
        return endpoints;
    }

    private static string Organization(string? raw,HttpContext context)
    {
        if(context.Request.Query["organization_id"].Count!=1 ||
            raw is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid quiz organization.");
        return raw;
    }

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
    }
}
