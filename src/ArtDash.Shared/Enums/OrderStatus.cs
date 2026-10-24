namespace ArtDash.Shared.Enums;

/// <summary>Lifecycle of an order. Transitions are enforced by the <c>Order</c> aggregate.</summary>
public enum OrderStatus
{
    /// <summary>Placed; waiting for payment (prepaid) or for the store to confirm it.</summary>
    Pending = 1,
    Confirmed = 2,
    Preparing = 3,
    ReadyForDelivery = 4,
    OutForDelivery = 5,
    Delivered = 6,
    Cancelled = 7
}
