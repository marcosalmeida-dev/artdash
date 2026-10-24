using ArtDash.Shared.Contracts.Common;
using ArtDash.Shared.Contracts.Payments;
using ArtDash.Shared.Enums;

namespace ArtDash.Shared.Contracts.Orders;

public sealed class OrderItemRequest
{
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; } = 1;
}

/// <summary>
/// Places an order. Customers use it from the storefront (the customer comes from their account);
/// staff use it from the back office and must say which customer, and may override the fee.
/// </summary>
public sealed class PlaceOrderRequest
{
    public Guid? CustomerId { get; set; }
    public List<OrderItemRequest> Items { get; set; } = [];
    public AddressDto DeliveryAddress { get; set; } = new();
    public PaymentTiming PaymentTiming { get; set; } = PaymentTiming.Prepaid;

    /// <summary>The method the customer intends to use (for on-delivery and invoiced orders).</summary>
    public PaymentMethod? PaymentMethod { get; set; }

    public string? Notes { get; set; }

    /// <summary>Back office only: replaces the tenant's delivery fee.</summary>
    public decimal? DeliveryFeeOverride { get; set; }

    /// <summary>Back office only.</summary>
    public decimal? Discount { get; set; }
}

public sealed record PlaceOrderResultDto(Guid OrderId, string Number, string TrackingCode, bool RequiresOnlinePayment);

public sealed record OrderListItemDto
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public OrderChannel Channel { get; init; }
    public OrderStatus Status { get; init; }
    public PaymentTiming PaymentTiming { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public PaymentMethod? PaymentMethod { get; init; }
    public decimal Total { get; init; }
    public decimal AmountPaid { get; init; }
    public int ItemsCount { get; init; }
    public string? District { get; init; }
    public string? City { get; init; }
    public DateTime PlacedAt { get; init; }
    public DateOnly? DueDate { get; init; }
    public Guid? DeliveryId { get; init; }
}

public sealed record OrderDto
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public string TrackingCode { get; init; } = string.Empty;
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? CustomerPhone { get; init; }
    public string? CustomerDocument { get; init; }
    public OrderChannel Channel { get; init; }
    public OrderStatus Status { get; init; }
    public PaymentTiming PaymentTiming { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public PaymentMethod? PaymentMethod { get; init; }
    public decimal Subtotal { get; init; }
    public decimal DeliveryFee { get; init; }
    public decimal Discount { get; init; }
    public decimal Total { get; init; }
    public decimal AmountPaid { get; init; }
    public decimal Outstanding { get; init; }
    public AddressDto DeliveryAddress { get; init; } = new();
    public string? Notes { get; init; }
    public DateTime PlacedAt { get; init; }
    public DateTime? ConfirmedAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public DateTime? CancelledAt { get; init; }
    public string? CancellationReason { get; init; }
    public DateOnly? DueDate { get; init; }
    public DateTime? PaidAt { get; init; }
    public bool IsOverdue { get; init; }
    public Guid? DeliveryId { get; init; }
    public string? DeliveryNumber { get; init; }
    public IReadOnlyList<OrderItemDto> Items { get; init; } = [];
    public IReadOnlyList<OrderStatusHistoryDto> History { get; init; } = [];
    public IReadOnlyList<OrderPaymentDto> Payments { get; init; } = [];
    public IReadOnlyList<PaymentTransactionDto> Transactions { get; init; } = [];
}

public sealed record OrderItemDto(
    Guid ProductId,
    string ProductName,
    string? Sku,
    UnitOfMeasure Unit,
    decimal UnitPrice,
    decimal Quantity,
    decimal Total);

public sealed record OrderStatusHistoryDto(OrderStatus? From, OrderStatus To, DateTime ChangedAt, string? ChangedBy, string? Note);

public sealed record OrderPaymentDto(Guid Id, PaymentMethod Method, decimal Amount, DateTime PaidAt, string? Reference, string? RegisteredBy);

public sealed class OrderQuery
{
    public string? Search { get; set; }
    public OrderStatus? Status { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public PaymentTiming? PaymentTiming { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class ChangeOrderStatusRequest
{
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
}

public sealed class CancelOrderRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class RegisterPaymentRequest
{
    public PaymentMethod Method { get; set; } = PaymentMethod.Pix;
    public decimal Amount { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Public tracking view (no personal data beyond what the link holder already knows).</summary>
public sealed record OrderTrackingDto
{
    public string Number { get; init; } = string.Empty;
    public string StoreName { get; init; } = string.Empty;
    public OrderStatus Status { get; init; }
    public DateTime PlacedAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public string? DeliveryArea { get; init; }
    public IReadOnlyList<OrderStatusHistoryDto> History { get; init; } = [];
    public string? DriverName { get; init; }
    public string? VehiclePlate { get; init; }
    public VehicleType? VehicleType { get; init; }
    public double? VehicleLatitude { get; init; }
    public double? VehicleLongitude { get; init; }
    public DateTime? VehiclePositionAt { get; init; }
    public double? DestinationLatitude { get; init; }
    public double? DestinationLongitude { get; init; }

    /// <summary>How many deliveries the driver still has to make before this one.</summary>
    public int? StopsBefore { get; init; }
}

public sealed class ReceivableQuery
{
    public string? Search { get; set; }
    public ReceivableFilter Filter { get; set; } = ReceivableFilter.Open;
    public DateTime? DueFrom { get; set; }
    public DateTime? DueTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record ReceivableDto
{
    public Guid OrderId { get; init; }
    public string Number { get; init; } = string.Empty;
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public decimal AmountPaid { get; init; }
    public decimal Outstanding { get; init; }
    public DateTime PlacedAt { get; init; }
    public DateOnly? DueDate { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public bool IsOverdue { get; init; }
    public int DaysOverdue { get; init; }
}

public sealed record ReceivablesDto(PagedResult<ReceivableDto> Page, decimal TotalOpen, decimal TotalOverdue, int OverdueCount);

/// <summary>An order that can be put on a delivery run.</summary>
public sealed record DeliverableOrderDto
{
    public Guid OrderId { get; init; }
    public string Number { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public OrderStatus Status { get; init; }
    public PaymentTiming PaymentTiming { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public decimal Total { get; init; }
    public decimal? TotalWeightKg { get; init; }
    public string? District { get; init; }
    public string? City { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public DateTime PlacedAt { get; init; }
}
