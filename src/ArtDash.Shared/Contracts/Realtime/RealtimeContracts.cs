using ArtDash.Shared.Enums;

namespace ArtDash.Shared.Contracts.Realtime;

/// <summary>Pushed to staff and to whoever tracks the order whenever its status or payment changes.</summary>
public sealed record OrderChangedMessage(
    Guid OrderId,
    string Number,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    string CustomerName,
    decimal Total,
    DateTime UpdatedAt);

/// <summary>Pushed to staff when a delivery run is planned, started, finished or cancelled.</summary>
public sealed record DeliveryChangedMessage(Guid DeliveryId, string Number, DeliveryStatus Status);
