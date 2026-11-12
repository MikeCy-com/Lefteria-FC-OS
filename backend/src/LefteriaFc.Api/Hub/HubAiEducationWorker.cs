namespace LefteriaFc.Api.Hub;

/// <summary>
/// Disabled by default. Claims no more than one education brief per cycle,
/// uses a separate least-privileged database identity, and sends no personal
/// player data. Creates only a draft requiring independent human review.
/// </summary>
public sealed class HubAiEducationWorker(
    HubAiEducationSettings settings,IHttpClientFactory clients,
    ILogger<HubAiEducationWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!settings.Enabled)return;
        var store=new HubAiEducationStore(settings.WorkerConnection);
        var provider=new HubEducationProvider(clients.CreateClient("hub-ai"),settings);
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job=await store.ClaimAsync(stoppingToken);
                if(job is not null)
                {
                    var result=await provider.GenerateAsync(job,stoppingToken);
                    if(result.Article is { } article && result.ErrorCode is null)
                        await store.CompleteAsync(job,article,settings.Model,stoppingToken);
                    else
                        await store.FailAsync(job,result.ErrorCode??"invalid_output",stoppingToken);
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {break;}
            catch(Exception)
            {
                // Deliberately omit provider bodies, generated text, credentials,
                // connection strings, names or account IDs from logs.
                logger.LogWarning("Education AI cycle failed closed; next lease will be rechecked.");
            }
            try {await Task.Delay(TimeSpan.FromSeconds(30),stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {break;}
        }
    }
}
