using Npgsql;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// Opt-in isolated worker for weekly individual lesson inbox notifications.
/// The worker authenticates with its own narrowly granted PostgreSQL role;
/// no AI, mobile runtime or API session can claim the queue.
/// </summary>
public sealed class HubLessonDeliverySettings
{
    public bool Enabled { get; }
    public string WorkerConnection { get; }
    public HubLessonDeliverySettings()
    {
        Enabled=string.Equals(Environment.GetEnvironmentVariable(
            "LEFTERIA_LESSON_DELIVERY_ENABLED"),"true",StringComparison.OrdinalIgnoreCase);
        WorkerConnection=Environment.GetEnvironmentVariable(
            "LEFTERIA_LESSON_WORKER_CONNECTION")??"";
        if(Enabled&&string.IsNullOrWhiteSpace(WorkerConnection))
            throw new InvalidOperationException(
                "An isolated lesson worker database role must be configured.");
    }
}

public sealed class HubLessonDeliveryWorker(
    HubLessonDeliverySettings settings,ILogger<HubLessonDeliveryWorker> logger)
    :BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!settings.Enabled)return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var db=new NpgsqlConnection(settings.WorkerConnection);
                await db.OpenAsync(stoppingToken);
                await using var cmd=new NpgsqlCommand(
                    "SELECT hub.process_due_mobile_lesson_schedules(@limit)",db);
                cmd.Parameters.AddWithValue("limit",20);
                _=await cmd.ExecuteScalarAsync(stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested)
            {break;}
            catch(Exception)
            {
                // No recipient identity, phone, lesson contents, or credentials
                // in server logs, including transient provider/db errors.
                logger.LogWarning("Private scheduled lesson cycle failed closed.");
            }
            try{await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}
