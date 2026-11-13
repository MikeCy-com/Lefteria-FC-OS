using Npgsql;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// Staging-only, opt-in in-app weekly lesson dispatch.
/// Uses a separate least-privilege PostgreSQL connection: never the API
/// runtime or database owner credentials. No push, SMS or payment processing.
/// </summary>
public sealed class HubWeeklyLearningDispatchWorker(
    IHostEnvironment environment,IConfiguration configuration,
    ILogger<HubWeeklyLearningDispatchWorker> logger) : BackgroundService
{
    private readonly string? connection=Validate(environment,configuration);

    private static string? Validate(IHostEnvironment environment,IConfiguration config)
    {
        if(config["HubLearningScheduler:Enabled"] is not "true")return null;
        if(!environment.IsStaging() ||
           config["HubLearningScheduler:IsolatedStaging"]!="true")
            throw new InvalidOperationException(
                "Weekly learning scheduler is restricted to isolated staging.");
        var raw=config["HubLearningScheduler:ConnectionString"];
        if(string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "Dedicated weekly scheduler connection is required.");
        var value=new NpgsqlConnectionStringBuilder(raw);
        if(value.Username!="lefteria_hub_scheduler" ||
           !(value.Database.EndsWith("_staging",StringComparison.OrdinalIgnoreCase) ||
             value.Database.EndsWith("_preview",StringComparison.OrdinalIgnoreCase) ||
             value.Database.EndsWith("_test",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Weekly dispatch requires the dedicated scheduler role and isolated database.");
        value.ApplicationName="lefteria-hub-weekly-dispatch";
        value.Pooling=false;
        value.Timeout=5;
        value.CommandTimeout=20;
        return value.ConnectionString;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(connection is null)return; // OFF by default in all deployments.
        logger.LogInformation("Isolated weekly HUB scheduler enabled.");
        await DispatchAsync(stoppingToken);
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(10));
        while(await timer.WaitForNextTickAsync(stoppingToken))
            await DispatchAsync(stoppingToken);
    }

    private async Task DispatchAsync(CancellationToken token)
    {
        try
        {
            await using var db=new NpgsqlConnection(connection);
            await db.OpenAsync(token);
            await using var command=new NpgsqlCommand(
                "SELECT hub.dispatch_due_weekly_learning(@max)",db);
            command.Parameters.AddWithValue("max",25);
            var delivered=await command.ExecuteScalarAsync(token);
            if(delivered is int count && count>0)
                logger.LogInformation("Weekly in-app learning delivered for {Count} enrollments.",count);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested)
        {
            // Normal shutdown; no retry task or orphaned background dispatch.
        }
        catch(Exception ex)
        {
            // No phone numbers, member IDs, lesson bodies or credentials logged.
            logger.LogWarning("Weekly learning dispatcher pass failed ({Kind}).",
                ex.GetType().Name);
        }
    }
}
