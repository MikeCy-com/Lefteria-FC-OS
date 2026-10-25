using LefteriaFc.Infrastructure;
using LefteriaFc.Api.Health;

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    return await HealthProbe.CheckAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddLefteriaInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();

app.MapHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/api/openapi/{documentName}.json");
}

app.Run();

return 0;
