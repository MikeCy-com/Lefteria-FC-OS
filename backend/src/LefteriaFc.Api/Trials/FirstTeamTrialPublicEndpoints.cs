using LefteriaFc.Application.Trials;

namespace LefteriaFc.Api.Trials;

/// <summary>
/// Source-compatible read-only first-team trials defaults. Public collection
/// remains disabled until consent, admin permissions, retention and stored
/// settings are separately verified. This endpoint never opens applications.
/// </summary>
public static class FirstTeamTrialPublicEndpoints
{
    public static IEndpointRouteBuilder MapFirstTeamTrialPublic(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/trials/settings", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            // Explicit source defaults, not an assertion about the original
            // live site and not a substitute for a future approved DB read.
            return Results.Ok(FirstTeamTrialsSettingsPolicy.Default);
        })
        .AllowAnonymous()
        .WithTags("First-team trials")
        .WithName("GetFirstTeamTrialSettings")
        .Produces<FirstTeamTrialsSettings>(200);

        return endpoints;
    }
}
