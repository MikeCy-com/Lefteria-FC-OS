using LefteriaFc.Application.Trials;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Api.WebAdmin;

namespace LefteriaFc.Api.Trials;

/// <summary>
/// READ-ONLY private admin preview of verified first-team registration settings.
/// Never uses source mobile auth, anonymous query data or legacy admin JWTs.
/// Changes still require a separate, audited consent/organization write contract.
/// </summary>
public static class FirstTeamTrialsAdminEndpoints
{
    public static IEndpointRouteBuilder MapFirstTeamTrialsAdmin(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/trials/settings",
            async (HttpContext context,string? organization_id,
                   WebAdminService admin,IFirstTeamTrialDisplayReader reader,CancellationToken token) =>
            {
                context.Response.Headers.CacheControl="private, no-store";
                context.Response.Headers.Pragma="no-cache";

                if (organization_id is null || organization_id!="lefteria_fc")
                    return Results.Json(new FirstTeamTrialAdminError("invalid_organization"),
                        statusCode:400);

                WebAdminSessionDto? session;
                try
                {
                    session=await admin.CurrentAsync(
                        context.Request.Cookies[WebAdminServices.CookieName],token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { return Unavailable(); }

                if(session is null)
                    return Results.Json(new FirstTeamTrialAdminError("not_authenticated"),
                        statusCode:401);

                if (!FirstTeamTrialsAdminAccess.CanRead(session.User,organization_id))
                    return Results.Json(new FirstTeamTrialAdminError("not_authorized"),
                        statusCode:403);

                try
                {
                    var result=await reader.ReadAsync(token);
                    // An actual verified DB row is mandatory for the
                    // management preview; source fallbacks are for public
                    // content only. Never expose unapproved 'open' states.
                    if(result is not { Open:false })
                        return Unavailable();
                    return Results.Ok(result);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { return Unavailable(); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("First-team trials administration")
            .WithName("GetAdminFirstTeamTrialSettings")
            .Produces<FirstTeamTrialsSettings>(200)
            .Produces<FirstTeamTrialAdminError>(400)
            .Produces<FirstTeamTrialAdminError>(401)
            .Produces<FirstTeamTrialAdminError>(403)
            .Produces<FirstTeamTrialAdminError>(503);
        return endpoints;
    }

    private static IResult Unavailable() =>
        Results.Json(new FirstTeamTrialAdminError("trials_unavailable"),
            statusCode:503);
}

public sealed record FirstTeamTrialAdminError(string Code);
