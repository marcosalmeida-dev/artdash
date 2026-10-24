namespace ArtDash.Domain.Orders;

/// <summary>A line of an order. Name, SKU and price are copied from the product when ordered.</summary>
public sealed class OrderItem : BaseEntity, ITenantEntity
{
    private OrderItem()
    {
    }

    internal OrderItem(Guid orderId, Guid productId, string productName, string? sku, UnitOfMeasure unit, decimal unitPrice, decimal quantity)
    {
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        Sku = sku;
        Unit = unit;
        UnitPrice = unitPrice;
        Quantity = quantity;
        Recalculate();
    }

    public Guid TenantId { get; set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public string? Sku { get; private set; }
    public UnitOfMeasure Unit { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Total { get; private set; }

    internal void IncreaseQuantity(decimal quantity)
    {
        Quantity += quantity;
        Recalculate();
    }

    private void Recalculate() => Total = decimal.Round(UnitPrice * Quantity, 2);
}

public sealed class OrderStatusHistory : BaseEntity, ITenantEntity
{
    private OrderStatusHistory()
    {
    }

    internal OrderStatusHistory(Guid orderId, OrderStatus? from, OrderStatus to, DateTime changedAt, string? changedBy, string? note)
    {
        OrderId = orderId;
        FromStatus = from;
        ToStatus = to;
        ChangedAt = changedAt;
        ChangedBy = changedBy;
        Note = note;
    }

    public Guid TenantId { get; set; }
    public Guid OrderId { get; private set; }
    public OrderStatus? FromStatus { get; private set; }
    public OrderStatus ToStatus { get; private set; }
    public DateTime ChangedAt { get; private set; }
    public string? ChangedBy { get; private set; }
    public string? Note { get; private set; }
}

/// <summary>Money received for an order: online, at the door, or settling an invoice.</summary>
public sealed class OrderPayment : BaseEntity, ITenantEntity
{
    private OrderPayment()
    {
    }

    internal OrderPayment(Guid orderId, PaymentMethod method, decimal amount, DateTime paidAt, string? reference, string? registeredBy, Guid? paymentTransactionId)
    {
        OrderId = orderId;
        Method = method;
        Amount = amount;
        PaidAt = paidAt;
        Reference = reference;
        RegisteredBy = registeredBy;
        PaymentTransactionId = paymentTransactionId;
    }

    public Guid TenantId { get; set; }
    public Guid OrderId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime PaidAt { get; private set; }
    public string? Reference { get; private set; }
    public string? RegisteredBy { get; private set; }
    public Guid? PaymentTransactionId { get; private set; }
}

public sealed record OrderPlacedEvent(Order Order) : DomainEvent;

public sealed record OrderStatusChangedEvent(Order Order, OrderStatus? From, OrderStatus To) : DomainEvent;

public sealed record OrderPaymentStatusChangedEvent(Order Order, PaymentStatus From, PaymentStatus To) : DomainEvent;

/// <summary>Generates the short codes used in public tracking links.</summary>
public static class TrackingCodeGenerator
{
    // No 0/O, 1/I/L: the code is sometimes read over the phone.
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public static string New(int length = 10) =>
        string.Create(length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
        });
}
