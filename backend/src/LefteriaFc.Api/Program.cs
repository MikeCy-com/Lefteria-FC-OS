using LefteriaFc.Infrastructure;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using var response = await healthClient.GetAsync("http://127.0.0.1:8080/api/health/ready");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        return 1;
    }
    catch (TaskCanceledException)
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddLefteriaInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();

app.MapGet("/api/health/live", () => Results.Ok(new { status = "live" }))
    .WithName("GetLiveness")
    .WithTags("Health");

app.MapGet("/api/health/ready", async (
        LefteriaDbContext database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        IResult result;
        try
        {
            result = await database.Database.CanConnectAsync(cancellationToken)
                ? Results.Ok(new { status = "ready" })
                : Results.Json(
                    new { status = "unavailable" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("Readiness").LogWarning(
                "Database readiness check failed ({ExceptionType})",
                exception.GetType().Name);
            result = Results.Json(
                new { status = "unavailable" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return result;
    })
    .WithName("GetReadiness")
    .WithTags("Health");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/api/openapi/{documentName}.json");
}

app.Run();

return 0;
