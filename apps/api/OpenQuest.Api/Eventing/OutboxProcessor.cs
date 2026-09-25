using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenQuest.Api.Config;
using OpenQuest.Api.Data;

namespace OpenQuest.Api.Eventing;

/// <summary>
/// Delivers outbox messages to event handlers as soon as they are committed. It does not poll: a database trigger
/// sends <c>NOTIFY outbox</c> on every insert (delivered by PostgreSQL only when the inserting transaction commits),
/// and this service listens for it. Time only enters for retries: after a failed delivery the processor sleeps until
/// the message's <c>available_at</c> (exponential backoff), then tries again. Nothing is lost while the service is down:
/// pending messages are drained at start-up.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopes,
    IEventDispatcher dispatcher,
    IOutboxClock clock,
    IConfiguration configuration,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> log) : BackgroundService
{
    public const string Channel = "outbox";
    private readonly SemaphoreSlim _signal = new(0, 1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RunAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                log.LogError(e, "Outbox processor failed; reconnecting.");
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var listener = new NpgsqlConnection(configuration.GetConnectionString("Default"));
        await listener.OpenAsync(ct);
        listener.Notification += (_, _) => Wake();
        await using (var listen = new NpgsqlCommand($"LISTEN {Channel}", listener)) await listen.ExecuteNonQueryAsync(ct);

        // Reads notifications; on failure wakes the main loop so it can surface the exception.
        using var listenerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var listening = Task.Run(async () =>
        {
            try { while (!listenerCts.IsCancellationRequested) await listener.WaitAsync(listenerCts.Token); }
            finally { Wake(); }
        }, CancellationToken.None);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                while (await ProcessBatchAsync(ct)) { }   // drain everything that is due (also what arrived while down)

                if (listening.IsFaulted) await listening;   // rethrows the listener's failure
                var wait = await TimeUntilNextRetryAsync(ct);
                await _signal.WaitAsync(wait ?? Timeout.InfiniteTimeSpan, ct);
            }
        }
        finally
        {
            listenerCts.Cancel();
            try { await listening; } catch { /* shutting down or already reported */ }
        }
    }

    private void Wake()
    {
        try { _signal.Release(); } catch (SemaphoreFullException) { /* already signalled */ }
    }

    private async Task<TimeSpan?> TimeUntilNextRetryAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var next = await db.OutboxMessages.AsNoTracking().Where(m => m.Status == OutboxStatus.Pending)
            .MinAsync(m => (DateTimeOffset?)m.AvailableAt, ct);
        if (next is null) return null;
        var delay = next.Value - clock.UtcNow;
        return delay > TimeSpan.Zero ? delay + TimeSpan.FromMilliseconds(20) : TimeSpan.Zero;
    }

    /// <summary>Delivers one batch of due messages. Returns false if there was nothing to do.</summary>
    private async Task<bool> ProcessBatchAsync(CancellationToken ct)
    {
        var o = options.Value;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = clock.UtcNow;
        var messages = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_message
                WHERE status = 'pending' AND available_at <= {now}
                ORDER BY occurred_at
                LIMIT {o.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);
        if (messages.Count == 0) return false;

        foreach (var group in messages.GroupBy(m => m.Type))
        {
            try
            {
                using var handlerScope = scopes.CreateScope(); // handlers get their own DbContext/connection
                await dispatcher.DispatchAsync(group.Key, group.Select(m => m.Payload).ToList(), handlerScope.ServiceProvider, ct);
                foreach (var m in group)
                {
                    m.Status = OutboxStatus.Processed;
                    m.ProcessedAt = clock.UtcNow;
                    m.LastError = null;
                }
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(e, "Delivering {Count} '{Type}' event(s) failed.", group.Count(), group.Key);
                foreach (var m in group)
                {
                    m.Attempts++;
                    m.LastError = e.Message.Length > 1000 ? e.Message[..1000] : e.Message;
                    if (m.Attempts >= o.MaxAttempts)
                    {
                        m.Status = OutboxStatus.Dead;
                        log.LogError("Event {Id} ('{Type}') is dead after {Attempts} attempts.", m.Id, m.Type, m.Attempts);
                    }
                    else m.AvailableAt = clock.UtcNow + TimeSpan.FromMilliseconds(o.RetryBaseDelayMs * Math.Pow(2, m.Attempts - 1));
                }
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);
        return true;
    }
}
