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
using LefteriaFc.Api.Audiences;
using LefteriaFc.Api.Messaging;
using LefteriaFc.Infrastructure.Messaging;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Infrastructure.Audiences;
using LefteriaFc.Application.Pos;
using LefteriaFc.Infrastructure.Pos;
using LefteriaFc.Infrastructure.PublicContent;
using LefteriaFc.Api.Evaluations;
using LefteriaFc.Api.MobileChat;
using LefteriaFc.Api.Hub;
using LefteriaFc.Api.Attendance;
using LefteriaFc.Application.MobileChat;
using LefteriaFc.Api.Crm;
using LefteriaFc.Api.Payabl;
using LefteriaFc.Api.Football;
using LefteriaFc.Api.Finance;
using LefteriaFc.Api.Marketing;
using LefteriaFc.Infrastructure.MobileChat;
using LefteriaFc.Api.Release;

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
// MVP-only must be an exact, deliberate operator setting. Unknown values
// cannot silently turn on unverified payments, POS or fan subscriptions.
var academyMvpMode = builder.Configuration["LEFTERIA_ACADEMY_MVP_ONLY"];
if (!AcademyMvpBoundary.ValidMode(academyMvpMode))
    throw new InvalidOperationException("LEFTERIA_ACADEMY_MVP_ONLY must be exactly true or false.");
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
    options.AddOperationTransformer<AdminNewsDraftOperationTransformer>();
    options.AddOperationTransformer<AdminPosCatalogOperationTransformer>();
    options.AddOperationTransformer<AdminAudienceOperationTransformer>();
});
builder.Services.AddLefteriaInfrastructure();
builder.Services.AddScoped<PostgresMobileGroupChatStore>();
builder.Services.AddScoped<PostgresCoachFeedbackDraftStore>();
builder.Services.AddScoped<MobileHubPublishedStore>();
builder.Services.AddScoped<MobileLessonScheduleStore>();
builder.Services.AddSingleton<HubLessonDeliverySettings>();
builder.Services.AddHostedService<HubLessonDeliveryWorker>();
builder.Services.AddScoped<MobilePlayerWeeklyQuizStore>();
builder.Services.AddScoped<MobilePlayerClassroomStore>();
builder.Services.AddScoped<MobileFanPayablStore>();
builder.Services.AddScoped<MobileFanLearningStore>();
builder.Services.AddScoped<MobileFanLessonScheduleStore>();
builder.Services.AddSingleton<HubFanLessonDeliverySettings>();
// A deferred fan subscription must not keep dispatching old lessons in the
// Academy-only pilot, even if a stale worker opt-in is still present.
if (!AcademyMvpBoundary.Disabled(academyMvpMode))
    builder.Services.AddHostedService<HubFanLessonDeliveryWorker>();
builder.Services.AddScoped<PostgresMobilePushStore>();
builder.Services.AddSingleton(new WebPushDeliverySettings(builder.Configuration));
builder.Services.AddHttpClient("web-push",client=>
    client.Timeout=TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(()=>new SocketsHttpHandler{
        AllowAutoRedirect=false,UseCookies=false,
        AutomaticDecompression=System.Net.DecompressionMethods.None,
        MaxConnectionsPerServer=4,
        PooledConnectionLifetime=TimeSpan.FromMinutes(5)
    });
builder.Services.AddSingleton<WebPushDeliverySender>();
builder.Services.AddHostedService<WebPushDispatchWorker>();
// Explicit test-environment opt-in only. No API model key or worker DB role
// is required unless LEFTERIA_AI_ENABLED=true.
builder.Services.AddSingleton<HubAiEducationSettings>();
builder.Services.AddHttpClient("hub-ai",client=>
    client.Timeout=TimeSpan.FromSeconds(25))
    .ConfigurePrimaryHttpMessageHandler(()=>new SocketsHttpHandler
    {
        AllowAutoRedirect=false,UseCookies=false,
        AutomaticDecompression=System.Net.DecompressionMethods.None,
        MaxConnectionsPerServer=2,
        PooledConnectionLifetime=TimeSpan.FromMinutes(5)
    });
builder.Services.AddHostedService<HubAiEducationWorker>();
builder.Services.AddScoped<PostgresMobilePrivateChatStore>();
builder.Services.AddMobileIdentity(builder.Configuration);
builder.Services.AddWebAdmin(builder.Configuration);
builder.Services.AddPayabl(builder.Configuration);
builder.Services.AddPublicContent();
builder.Services.AddPublicSections();
builder.Services.AddScoped<IAdminPosCatalogReader, PostgresAdminPosCatalogReader>();
builder.Services.AddScoped<IAdminPosCategoryWriter, PostgresAdminPosCategoryWriter>();
builder.Services.AddScoped<IAdminPosProductWriter, PostgresAdminPosProductWriter>();
builder.Services.AddScoped<IAdminAudienceDirectory, PostgresAdminAudienceDirectory>();
builder.Services.AddScoped<IAdminCoachAssignedGroupsReader, PostgresAdminCoachAssignedGroupsReader>();
builder.Services.AddScoped<IAdminRosterGroupWriter, PostgresAdminRosterGroupWriter>();
builder.Services.AddScoped<IAdminRosterPlayerDirectory, PostgresAdminRosterPlayerDirectory>();
builder.Services.AddScoped<IAdminRosterAssignmentWriter, PostgresAdminRosterAssignmentWriter>();
builder.Services.AddScoped<IAdminCoachDirectory, PostgresAdminCoachDirectory>();
builder.Services.AddScoped<IAdminCoachAssignmentWriter, PostgresAdminCoachAssignmentWriter>();
builder.Services.AddScoped<IAdminAnnouncementDraftStore, PostgresAdminAnnouncementDraftStore>();
builder.Services.AddScoped<IMobileChatGroupReader, PostgresMobileChatGroupReader>();
builder.Services.AddScoped<MobileChatDirectoryService>();
builder.Services.AddScoped<LefteriaFc.Application.PublicContent.IAdminClubRepository, PostgresAdminClubRepository>();
builder.Services.AddScoped<LefteriaFc.Application.PublicContent.IAdminNewsRepository, PostgresAdminNewsRepository>();
builder.Services.AddScoped<LefteriaFc.Application.PublicContent.IAdminNewsDraftRepository, PostgresAdminNewsDraftRepository>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower);

var app = builder.Build();
app.UseExceptionHandler();
app.UseForwardedHeaders();
if (AcademyMvpBoundary.Disabled(academyMvpMode))
{
    // Preserve routes and API contracts for a later, independently approved
    // feature rollout, but fail closed before any optional handler executes.
    app.Use(async (context, next) =>
    {
        if (!AcademyMvpBoundary.IsOptionalEndpoint(context.Request.Path))
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.Pragma = "no-cache";
        await Results.Json(new
        {
            code = "mvp_feature_disabled",
            detail = "This optional feature is not enabled in the Academy MVP."
        }, statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
    });
}
// Authenticated mobile and web-admin API responses may contain account,
// roster, child, financial or session data. Never let shared intermediaries or
// the browser store them, including auth failures and error responses.
// Public content routes remain cacheable under their own explicit policies.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/mobile", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers["Expires"] = "0";
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
app.MapMobileGroupChat();
app.MapMobileCoachFeedback();
app.MapMobilePrivateChat();
app.MapMobileAppAnnouncements();
app.MapMobileWebPush();
app.MapWebAdmin();
app.MapAdminUserDirectory();
app.MapAdminProviderSettings();
app.MapAdminNativeCoach();
app.MapPayabl();
app.MapAcademyFees();
app.MapAcademyInvoices();
app.MapPublicContentEndpoints();
app.MapFirstTeamTrialPublic();
app.MapFirstTeamTrialsAdmin();
app.MapPublicSections();
app.MapPublicMedia();
app.MapAcademyRegistration();
app.MapAcademyIntakeAdmin();
app.MapAdminClub();
app.MapAdminNews();
app.MapAdminNewsDrafts();
app.MapWebsitePages();
app.MapAdminSponsorships();
app.MapAdminPosCatalog();
app.MapAdminPosCategories();
app.MapAdminPosProducts();
app.MapAdminPosVatSettings();
app.MapAdminPosLocations();
app.MapAdminPosCashier();
app.MapAdminPosSites();
app.MapAdminAudiences();
app.MapAdminGroupCreates();
app.MapAdminProfiledGroupCreates();
app.MapAdminAcademyGroupProfiles();
app.MapAdminFirstTeamProfiles();
app.MapAdminRosterPlayers();
app.MapAdminPlayerBirthdayPreferences();
app.MapAdminRosterAssignments();
app.MapAdminCoaches();
app.MapAdminCoachAssignments();
app.MapAdminAnnouncementDrafts();
app.MapAdminHub();
app.MapAdminPlayerWeeklyQuiz();
app.MapAdminPlayerLearningPositions();
app.MapAdminClassroomCurriculum();
app.MapMobileHubPublished();
app.MapMobileLessonSchedules();
app.MapMobilePlayerWeeklyQuiz();
app.MapMobileFanPayabl();
app.MapMobileChatGroupDirectory();
app.MapAdminAcademyEvaluations();
app.MapAdminPlayerDevelopmentGoals();
app.MapAdminAcademyDocuments();
app.MapAdminPrivateDocumentUploads();
app.MapAdminAcademyAttendance();
app.MapAdminAcademyTrainingPlans();
app.MapAcademyLeadAdmin();
app.MapAcademyOffice();
app.MapAcademyGuardianProof();
app.MapAdminFootballTerritories();
app.MapAdminFootballTerritoryAccess();
app.MapAdminAcademyLocationFinance();
app.MapAdminFinanceOverview();
app.MapAdminSupplierBills();
app.MapAdminSupplierPayments();
app.MapAdminSupplierCredits();
app.MapAdminSupplierReversals();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/api/openapi/{documentName}.json");
}

app.Run();

return 0;

public partial class Program { }
