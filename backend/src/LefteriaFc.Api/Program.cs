using LefteriaFc.Infrastructure;
using LefteriaFc.Api.Health;
using LefteriaFc.Api.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Api.Roster;
using LefteriaFc.Api.Family;
using LefteriaFc.Api.ClubCalendar;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Api.PublicContent;
using LefteriaFc.Api.Trials;
using LefteriaFc.Api.Pos;
using LefteriaFc.Application.Pos;
using LefteriaFc.Infrastructure.Pos;
using LefteriaFc.Infrastructure.PublicContent;

if (args.SequenceEqual(["--check-avatar-codec"], StringComparer.Ordinal))
{
    var ready = AvatarCodecProbe.Check();
    Console.WriteLine(ready ? "Avatar codec ready." : "Avatar codec unavailable.");
    return ready ? 0 : 1;
}

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
    options.AddDocumentTransformer<WebAdminSecurityDocumentTransformer>();
    options.AddOperationTransformer<WebAdminSecurityOperationTransformer>();
    options.AddSchemaTransformer<AdminClubSchemaTransformer>();
    options.AddOperationTransformer<AdminClubOperationTransformer>();
    options.AddOperationTransformer<FirstTeamTrialAdminOperationTransformer>();
    options.AddOperationTransformer<AdminNewsOperationTransformer>();
    options.AddOperationTransformer<AdminPosCatalogOperationTransformer>();
});
builder.Services.AddLefteriaInfrastructure();
builder.Services.AddMobileIdentity(builder.Configuration);
builder.Services.AddWebAdmin(builder.Configuration);
builder.Services.AddPublicContent();
builder.Services.AddPublicSections();
builder.Services.AddScoped<IAdminPosCatalogReader, PostgresAdminPosCatalogReader>();
builder.Services.AddScoped<IAdminPosCategoryWriter, PostgresAdminPosCategoryWriter>();
builder.Services.AddScoped<IAdminPosProductWriter, PostgresAdminPosProductWriter>();
builder.Services.AddScoped<LefteriaFc.Application.PublicContent.IAdminClubRepository, PostgresAdminClubRepository>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower);

var app = builder.Build();
app.UseExceptionHandler();
app.UseForwardedHeaders();
// All mobile account, OTP, family, roster, and calendar responses are
// session-specific, including authentication failures and service errors.
// Register OnStarting so endpoint/error handlers cannot accidentally emit
// cacheable private data. Public content and avatars are unaffected.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/mobile", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            return Task.CompletedTask;
        });
    }
    await next(context);
});
app.UseMobileRequestGuards();
app.UseWebAdminGuards();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapMobileIdentity();
app.MapMobileRoster();
app.MapParentFamily();
app.MapMobileCalendar();
app.MapWebAdmin();
app.MapPublicContentEndpoints();
app.MapFirstTeamTrialPublic();
app.MapFirstTeamTrialsAdmin();
app.MapPublicSections();
app.MapAdminClub();
app.MapAdminNews();
app.MapAdminPosCatalog();
app.MapAdminPosCategories();
app.MapAdminPosProducts();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/api/openapi/{documentName}.json");
}

app.Run();

return 0;

public partial class Program { }
