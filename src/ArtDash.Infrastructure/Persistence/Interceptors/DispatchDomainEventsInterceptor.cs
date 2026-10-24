using ArtDash.Application.Common.Events;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArtDash.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Collects domain events while saving and dispatches them only after the save succeeded.
/// (artstore-fs dispatched inside SavingChanges — before the INSERT, with ids not yet generated —
/// within a transaction that did not even wrap the save.)
/// </summary>
public sealed class DispatchDomainEventsInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    private readonly List<IDomainEvent> _pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        DispatchPendingAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await DispatchPendingAsync(cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _pending.Clear();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Clear();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entity in context.ChangeTracker.Entries<BaseEntity>().Select(e => e.Entity))
        {
            var events = entity.GetDomainEvents();
            if (events.Count == 0)
            {
                continue;
            }

            _pending.AddRange(events);
            entity.ClearDomainEvents();
        }
    }

    private async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var events = _pending.ToArray();
        _pending.Clear();
        await dispatcher.DispatchAsync(events, cancellationToken);
    }
}
