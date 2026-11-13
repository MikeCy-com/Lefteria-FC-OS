using Npgsql;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// A separate opt-in staging worker for Payabl-verified fan lessons.
/// Does not send personalized text to external notification providers.
/// </summary>
public sealed class HubFanLessonDeliverySettings
{
    public bool Enabled { get; }
    public string WorkerConnection { get; }
    public HubFanLessonDeliverySettings()
    {
        Enabled=string.Equals(Environment.GetEnvironmentVariable(
          "LEFTERIA_FAN_LESSON_DELIVERY_ENABLED"),"true",
          StringComparison.OrdinalIgnoreCase);
        WorkerConnection=Environment.GetEnvironmentVariable(
          "LEFTERIA_FAN_LESSON_WORKER_CONNECTION")??"";
        if(Enabled&&string.IsNullOrWhiteSpace(WorkerConnection))
            throw new InvalidOperationException(
                "Isolated fan delivery worker credentials are required.");
    }
}
public sealed class HubFanLessonDeliveryWorker(
    HubFanLessonDeliverySettings settings,
    ILogger<HubFanLessonDeliveryWorker> logger):BackgroundService
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
                await using var command=new NpgsqlCommand(
                    "SELECT hub.process_due_fan_lesson_schedules(@limit)",db);
                command.Parameters.AddWithValue("limit",20);
                _=await command.ExecuteScalarAsync(stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested)
            {break;}
            catch(Exception)
            {
                logger.LogWarning("Scheduled fan education delivery failed closed.");
            }
            try{await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}
