using System.Linq.Expressions;
using ArtDash.Shared.Contracts.Orders;
using ArtDash.Shared.Contracts.Payments;

namespace ArtDash.Application.Features.Orders;

internal static class OrderProjections
{
    public static readonly Expression<Func<Order, OrderListItemDto>> ToListItem = o => new OrderListItemDto
    {
        Id = o.Id,
        Number = o.Number,
        CustomerName = o.CustomerName,
        Channel = o.Channel,
        Status = o.Status,
        PaymentTiming = o.PaymentTiming,
        PaymentStatus = o.PaymentStatus,
        PaymentMethod = o.PaymentMethod,
        Total = o.Total,
        AmountPaid = o.AmountPaid,
        ItemsCount = o.Items.Count(),
        District = o.DeliveryAddress.District,
        City = o.DeliveryAddress.City,
        PlacedAt = o.PlacedAt,
        DueDate = o.DueDate,
        DeliveryId = o.DeliveryId
    };
}

internal static class OrderMapper
{
    public static OrderDto ToDto(
        Order order,
        IReadOnlyList<OrderStatusHistory> history,
        IReadOnlyList<OrderPayment> payments,
        IReadOnlyList<PaymentTransaction> transactions,
        string? deliveryNumber,
        DateOnly today) => new()
    {
        Id = order.Id,
        Number = order.Number,
        TrackingCode = order.TrackingCode,
        CustomerId = order.CustomerId,
        CustomerName = order.CustomerName,
        CustomerPhone = order.CustomerPhone,
        CustomerDocument = order.CustomerDocument,
        Channel = order.Channel,
        Status = order.Status,
        PaymentTiming = order.PaymentTiming,
        PaymentStatus = order.PaymentStatus,
        PaymentMethod = order.PaymentMethod,
        Subtotal = order.Subtotal,
        DeliveryFee = order.DeliveryFee,
        Discount = order.Discount,
        Total = order.Total,
        AmountPaid = order.AmountPaid,
        Outstanding = order.OutstandingAmount,
        DeliveryAddress = order.DeliveryAddress.ToDto(),
        Notes = order.Notes,
        PlacedAt = order.PlacedAt,
        ConfirmedAt = order.ConfirmedAt,
        DeliveredAt = order.DeliveredAt,
        CancelledAt = order.CancelledAt,
        CancellationReason = order.CancellationReason,
        DueDate = order.DueDate,
        PaidAt = order.PaidAt,
        IsOverdue = order.IsOverdue(today),
        DeliveryId = order.DeliveryId,
        DeliveryNumber = deliveryNumber,
        Items = order.Items
            .Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.Sku, i.Unit, i.UnitPrice, i.Quantity, i.Total))
            .ToList(),
        History = history
            .OrderBy(h => h.ChangedAt)
            .Select(h => new OrderStatusHistoryDto(h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy, h.Note))
            .ToList(),
        Payments = payments
            .OrderBy(p => p.PaidAt)
            .Select(p => new OrderPaymentDto(p.Id, p.Method, p.Amount, p.PaidAt, p.Reference, p.RegisteredBy))
            .ToList(),
        Transactions = transactions
            .OrderByDescending(t => t.CreatedAt)
            .Select(PaymentTransactionMapper.ToDto)
            .ToList()
    };
}

internal static class PaymentTransactionMapper
{
    public static PaymentTransactionDto ToDto(PaymentTransaction t) =>
        new(t.Id, t.Provider, t.ProviderPaymentId, t.Method, t.Status, t.StatusDetail, t.Amount, t.Installments, t.CreatedAt, t.ApprovedAt);
}

/// <summary>Loads everything an <see cref="OrderDto"/> needs, in a few small queries.</summary>
internal static class OrderDetailsLoader
{
    public static async Task<OrderDto?> LoadAsync(
        IApplicationDbContext db,
        Expression<Func<Order, bool>> predicate,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(predicate, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var history = await db.OrderStatusHistory.AsNoTracking()
            .Where(h => h.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        var payments = await db.OrderPayments.AsNoTracking()
            .Where(p => p.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        var transactions = await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        var deliveryNumber = order.DeliveryId is { } deliveryId
            ? await db.Deliveries.AsNoTracking().Where(d => d.Id == deliveryId).Select(d => d.Number).FirstOrDefaultAsync(cancellationToken)
            : null;

        return OrderMapper.ToDto(order, history, payments, transactions, deliveryNumber, today);
    }
}
