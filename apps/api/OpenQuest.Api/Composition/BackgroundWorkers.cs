using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;
using OpenQuest.Api.Services;
using OpenQuest.Api.Sync;

namespace OpenQuest.Api.Composition;

/// <summary>
/// Pulls the city's data on a schedule. The open data platform sends no change notifications,
/// so scheduled pulling is the only option for this direction (the way back to the city is event-driven).
/// </summary>
public sealed class ScheduledSyncWorker(ISyncTrigger sync, IOptions<AdapterOptions> options, ILogger<ScheduledSyncWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (o.SyncOnStartup) sync.TryStartInBackground();
        if (o.SyncIntervalHours <= 0)
        {
            log.LogInformation("Scheduled asset sync disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(o.SyncIntervalHours));
        while (await timer.WaitForNextTickAsync(ct))
            if (!sync.TryStartInBackground()) log.LogWarning("Skipping scheduled sync: another one is still running.");
    }
}

/// <summary>Claims expire by the clock, not by an event: this sweep only makes the release visible to everyone promptly.</summary>
public sealed class ClaimExpiryWorker(IServiceScopeFactory scopes, ILogger<ClaimExpiryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var n = await scope.ServiceProvider.GetRequiredService<IClaimExpiryService>().ExpireStaleClaimsAsync(ct);
                if (n > 0) log.LogInformation("Expired {Count} claims.", n);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { log.LogError(e, "Claim expiry run failed."); }
        }
    }
}
