using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;

namespace OpenQuest.Api.Scheduling;

/// <summary>
/// Starts the weekly quest runs when their time has come. This is the one place that has to look at the clock, because "Sunday 08:00"
/// is a moment, not an event. Ticking often is cheap (the runner only reads the schedules) and safe: a run happens once per period.
/// </summary>
public sealed class QuestScheduleWorker(IServiceScopeFactory scopes, IOptions<GamificationOptions> options, ILogger<QuestScheduleWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.QuestScheduleIntervalSeconds)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var runs = await scope.ServiceProvider.GetRequiredService<IQuestScheduleRunner>().RunDueAsync(ct);
                if (runs > 0) log.LogInformation("Started {Count} scheduled quest run(s).", runs);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { log.LogError(e, "Scheduled quest run failed."); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
