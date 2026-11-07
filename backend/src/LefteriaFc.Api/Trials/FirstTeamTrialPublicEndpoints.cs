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
        endpoints.MapGet("/api/trials/settings",
            async (HttpContext context, IFirstTeamTrialDisplayReader reader, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            // Read only the hard-scoped, closed-only DB function. Any missing,
            // uninitialized or rejected data falls back to source defaults;
            // there is no pathway to opening submissions in this endpoint.
            try
            {
                var settings = await reader.ReadAsync(token);
                return Results.Ok(settings is { Open: false }
                    ? settings : FirstTeamTrialsSettingsPolicy.Default);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { return Results.Ok(FirstTeamTrialsSettingsPolicy.Default); }
        })
        .AllowAnonymous()
        .WithTags("First-team trials")
        .WithName("GetFirstTeamTrialSettings")
        .Produces<FirstTeamTrialsSettings>(200);

        return endpoints;
    }
}
