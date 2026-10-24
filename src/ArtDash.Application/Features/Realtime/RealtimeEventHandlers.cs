using ArtDash.Shared.Contracts.Realtime;

namespace ArtDash.Application.Features.Realtime;

/// <summary>Turns order domain events into live updates for the order board and the tracking pages.</summary>
internal sealed class OrderRealtimeEventHandler(IRealtimeNotifier notifier, TimeProvider clock)
    : IDomainEventHandler<OrderPlacedEvent>,
      IDomainEventHandler<OrderStatusChangedEvent>,
      IDomainEventHandler<OrderPaymentStatusChangedEvent>
{
    public Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken) =>
        NotifyAsync(domainEvent.Order, cancellationToken);

    public Task Handle(OrderStatusChangedEvent domainEvent, CancellationToken cancellationToken) =>
        NotifyAsync(domainEvent.Order, cancellationToken);

    public Task Handle(OrderPaymentStatusChangedEvent domainEvent, CancellationToken cancellationToken) =>
        NotifyAsync(domainEvent.Order, cancellationToken);

    private Task NotifyAsync(Order order, CancellationToken cancellationToken) =>
        notifier.OrderChangedAsync(
            order.TenantId,
            new OrderChangedMessage(
                order.Id,
                order.Number,
                order.Status,
                order.PaymentStatus,
                order.CustomerName,
                order.Total,
                clock.GetUtcNow().UtcDateTime),
            cancellationToken);
}

internal sealed class DeliveryRealtimeEventHandler(IRealtimeNotifier notifier) : IDomainEventHandler<DeliveryStatusChangedEvent>
{
    public Task Handle(DeliveryStatusChangedEvent domainEvent, CancellationToken cancellationToken) =>
        notifier.DeliveryChangedAsync(
            domainEvent.Delivery.TenantId,
            new DeliveryChangedMessage(domainEvent.Delivery.Id, domainEvent.Delivery.Number, domainEvent.Delivery.Status),
            cancellationToken);
}
