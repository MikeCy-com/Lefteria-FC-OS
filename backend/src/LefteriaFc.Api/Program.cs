using LefteriaFc.Infrastructure;
using LefteriaFc.Api.Health;
using LefteriaFc.Api.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

if (args.SequenceEqual(["--migrate"], StringComparer.Ordinal))
{
    // Explicit operator command only; ordinary API startup never changes schema.
    var connection = Environment.GetEnvironmentVariable("LEFTERIA_MIGRATION_CONNECTION");
    if (string.IsNullOrWhiteSpace(connection))
    {
        connection = new NpgsqlConnectionStringBuilder
        {
            Host = RequiredMigrationVariable("HOST"), Database = RequiredMigrationVariable("DATABASE"),
            Username = RequiredMigrationVariable("USERNAME"), Password = RequiredMigrationVariable("PASSWORD")
        }.ConnectionString;
    }
    await using var database = new LefteriaDbContext(new DbContextOptionsBuilder<LefteriaDbContext>().UseNpgsql(connection).Options);
    await database.Database.MigrateAsync();
    return 0;

    static string RequiredMigrationVariable(string suffix) =>
        Environment.GetEnvironmentVariable("LEFTERIA_MIGRATION_" + suffix) is { Length: > 0 } value ? value :
            throw new InvalidOperationException("Explicit migration credentials are required for --migrate.");
}

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    return await HealthProbe.CheckAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<MobileSecurityDocumentTransformer>();
    options.AddOperationTransformer<MobileSecurityOperationTransformer>();
    options.AddSchemaTransformer<MobileRequestSchemaTransformer>();
});
builder.Services.AddLefteriaInfrastructure();
builder.Services.AddMobileIdentity(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower);

var app = builder.Build();
app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseMobileRequestGuards();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapMobileIdentity();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/api/openapi/{documentName}.json");
}

app.Run();

return 0;

public partial class Program { }
