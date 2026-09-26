using OpenQuest.Api.Services;

namespace OpenQuest.Api.Composition;

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
