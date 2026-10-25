using LefteriaFc.Infrastructure.Persistence;

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
        LefteriaDbContext database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        var logger = loggerFactory.CreateLogger("Readiness");
        try
        {
            if (await database.Database.CanConnectAsync(budget.Token))
            {
                return TypedResults.Ok(new HealthResponse("ready"));
            }
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
