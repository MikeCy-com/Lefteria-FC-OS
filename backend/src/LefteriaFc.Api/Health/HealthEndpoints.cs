using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Api.Health;

public sealed record HealthResponse(string Status);

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/health").WithTags("Health");
        group.MapGet("/live", () => TypedResults.Ok(new HealthResponse("live")))
            .WithName("GetLiveness");
        group.MapGet("/ready", CheckReadiness)
            .WithName("GetReadiness")
            .Produces<HealthResponse>()
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<IResult> CheckReadiness(
        HttpContext context,
        LefteriaDbContext database,
        WebAdminConnections adminConnections,
        IOptions<WebAdminOptions> adminOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // Never let a proxy cache a previously ready container after a schema
        // revocation or during a release/migration transition.
        context.Response.Headers.CacheControl="no-store, max-age=0";
        context.Response.Headers.Pragma="no-cache";
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        var logger = loggerFactory.CreateLogger("Readiness");
        try
        {
            if (await RuntimeReadiness.CheckAsync(
                database,adminConnections,adminOptions,budget.Token))
            {
                return TypedResults.Ok(new HealthResponse("ready"));
            }
            logger.LogWarning("Runtime database schema or least-privilege grants are not ready");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Database readiness check exceeded its time budget");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Database readiness check failed ({ExceptionType})", exception.GetType().Name);
        }

        return TypedResults.Json(
            new HealthResponse("unavailable"),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
