using LefteriaFc.Application.Messaging;
using LefteriaFc.Infrastructure.Messaging;

namespace LefteriaFc.Api.Messaging;

/// <summary>
/// Opt-in bounded hosted worker. Each attempt is atomically leased in
/// PostgreSQL, authorization is rechecked immediately before delivery,
/// and only a generic payload-free wake-up reaches a trusted push provider.
/// </summary>
public sealed class WebPushDispatchWorker(
    IServiceScopeFactory scopes,WebPushDeliverySettings settings,
    WebPushDeliverySender sender,ILogger<WebPushDispatchWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!settings.Enabled)return;
        // Do not start a push fan-out on every app restart in a tight loop.
        while(!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchAsync(stoppingToken); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested)
            {break;}
            catch(Exception)
            {
                // Never log secret URLs, bearer tokens or recipient identities.
                logger.LogWarning("Web Push delivery cycle failed closed; will retry later.");
            }
            try{await Task.Delay(TimeSpan.FromSeconds(20),stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
        }
    }

    private async Task DispatchAsync(CancellationToken token)
    {
        using var scope=scopes.CreateScope();
        var store=scope.ServiceProvider.GetRequiredService<PostgresMobilePushStore>();
        var batch=await store.ClaimAsync(10,token);
        if(batch.Deliveries is null||batch.Deliveries.Count>10)
            throw new InvalidOperationException("Invalid bounded push batch.");
        foreach(var lease in batch.Deliveries)
        {
            token.ThrowIfCancellationRequested();
            if(lease.Id==Guid.Empty||lease.LeaseToken==Guid.Empty||
               lease.Attempts is <1 or >5 || lease.Endpoint.Length>2048)
                continue; // Bad rows expire without a provider request.
            // This second check closes the window between selection and
            // outbound IO for read, withdrawal, opt-out and roster revocation.
            if(!await store.RecheckAsync(lease.Id,lease.LeaseToken,token))
            {
                await store.FinishAsync(lease.Id,lease.LeaseToken,
                    "cancelled",null,token);
                continue;
            }
            var (outcome,status)=await sender.SendAsync(lease.Endpoint,token);
            await store.FinishAsync(lease.Id,lease.LeaseToken,outcome,status,token);
            // 'accepted' means push gateway accepted the request; it does NOT
            // mean a human saw it. Real read receipts stay in the app DB.
        }
    }
}
