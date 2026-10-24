using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArtDash.Application.Common.Events;

/// <summary>Reacts to a domain event after the change that raised it has been committed.</summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}

/// <summary>
/// Called by the persistence layer after <c>SaveChanges</c> succeeds. Handlers run in the same scope
/// (same tenant, same user). A failing handler is logged and does not fail the request: the data is
/// already committed and side effects (notifications) must not turn a success into an error.
/// </summary>
internal sealed class DomainEventDispatcher(IServiceProvider serviceProvider, ILogger<DomainEventDispatcher> logger)
    : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<object, IDomainEvent, CancellationToken, Task>> Invokers = new();

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            var eventType = domainEvent.GetType();
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var invoke = Invokers.GetOrAdd(eventType, BuildInvoker);

            foreach (var handler in serviceProvider.GetServices(handlerType))
            {
                if (handler is null)
                {
                    continue;
                }

                try
                {
                    await invoke(handler, domainEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Domain event handler {Handler} failed for {Event}", handler.GetType().Name, eventType.Name);
                }
            }
        }
    }

    private static Func<object, IDomainEvent, CancellationToken, Task> BuildInvoker(Type eventType)
    {
        var method = typeof(DomainEventDispatcher)
            .GetMethod(nameof(InvokeTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(eventType);

        return method.CreateDelegate<Func<object, IDomainEvent, CancellationToken, Task>>();
    }

    private static Task InvokeTyped<TEvent>(object handler, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent =>
        ((IDomainEventHandler<TEvent>)handler).Handle((TEvent)domainEvent, cancellationToken);
}
