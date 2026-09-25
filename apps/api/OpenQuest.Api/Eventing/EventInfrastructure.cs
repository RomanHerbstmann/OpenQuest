using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Eventing;

/// <summary>Time source of the outbox. Separate from the game clock so scheduling keeps working when that one is faked.</summary>
public interface IOutboxClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemOutboxClock : IOutboxClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Maps event type names (stored in the outbox) to CLR types. All <see cref="IDomainEvent"/>s of Core are known.</summary>
public sealed class EventTypeRegistry
{
    private readonly Dictionary<string, Type> _types = typeof(IDomainEvent).Assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IDomainEvent).IsAssignableFrom(t))
        .ToDictionary(t => t.Name);

    public static string NameOf(Type type) => type.Name;
    public Type? Resolve(string name) => _types.GetValueOrDefault(name);
}

/// <summary>Stages events in the outbox table as part of the caller's transaction (see <see cref="OutboxMessage"/>).</summary>
public sealed class OutboxEventPublisher(AppDbContext db, IOutboxClock clock) : IEventPublisher
{
    public void Publish(IDomainEvent domainEvent)
    {
        var now = clock.UtcNow;
        db.OutboxMessages.Add(new OutboxMessage
        {
            Type = EventTypeRegistry.NameOf(domainEvent.GetType()),
            Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), EventJson.Options),
            OccurredAt = now,
            AvailableAt = now,
        });
    }
}

/// <summary>Delivers a batch of serialized events of one type to all registered handlers.</summary>
public interface IEventDispatcher
{
    Task DispatchAsync(string eventType, IReadOnlyList<string> payloads, IServiceProvider services, CancellationToken ct);
}

public sealed class EventDispatcher(EventTypeRegistry registry) : IEventDispatcher
{
    private static readonly MethodInfo DispatchTypedMethod =
        typeof(EventDispatcher).GetMethod(nameof(DispatchTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    public Task DispatchAsync(string eventType, IReadOnlyList<string> payloads, IServiceProvider services, CancellationToken ct)
    {
        var type = registry.Resolve(eventType) ?? throw new InvalidOperationException($"Unknown event type '{eventType}'.");
        return (Task)DispatchTypedMethod.MakeGenericMethod(type).Invoke(null, [payloads, services, ct])!;
    }

    private static async Task DispatchTyped<T>(IReadOnlyList<string> payloads, IServiceProvider services, CancellationToken ct)
        where T : IDomainEvent
    {
        var events = payloads.Select(p => JsonSerializer.Deserialize<T>(p, EventJson.Options)
                                          ?? throw new InvalidOperationException("Empty event payload.")).ToList();
        foreach (var handler in services.GetServices<IEventHandler<T>>())
            await handler.HandleAsync(events, ct);
    }
}
