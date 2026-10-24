using ArtDash.Domain.Customers;

namespace ArtDash.Domain.Orders;

/// <summary>
/// The order aggregate. Owns its items, payments and status history and enforces the lifecycle:
/// <code>
/// Pending ──► Confirmed ──► Preparing ──► ReadyForDelivery ──► OutForDelivery ──► Delivered
///    └──────────────┴────────────┴────────────────┴──► Cancelled
/// </code>
/// Prepaid orders can only leave <see cref="OrderStatus.Pending"/> once fully paid. OutForDelivery and
/// Delivered are reached through a delivery run, never by hand.
/// </summary>
public sealed class Order : BaseTenantEntity
{
    private readonly List<OrderItem> _items = [];
    private readonly List<OrderStatusHistory> _history = [];
    private readonly List<OrderPayment> _payments = [];

    private Order()
    {
    }

    public string Number { get; private set; } = string.Empty;

    /// <summary>Unguessable code for the public tracking link.</summary>
    public string TrackingCode { get; private set; } = string.Empty;

    public Guid CustomerId { get; private set; }
    public Customer Customer { get; private set; } = null!;

    // Snapshot of the customer at the time of the order.
    public string CustomerName { get; private set; } = string.Empty;
    public string? CustomerPhone { get; private set; }
    public string? CustomerDocument { get; private set; }

    public OrderChannel Channel { get; private set; }
    public OrderStatus Status { get; private set; }
    public PaymentTiming PaymentTiming { get; private set; }
    public PaymentMethod? PaymentMethod { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal DeliveryFee { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Total { get; private set; }
    public decimal AmountPaid { get; private set; }

    public Address DeliveryAddress { get; private set; } = new();
    public string? Notes { get; private set; }

    public DateTime PlacedAt { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>Due date of invoiced orders (a calendar date in the tenant's time zone).</summary>
    public DateOnly? DueDate { get; private set; }

    public DateTime? PaidAt { get; private set; }

    /// <summary>The delivery run currently carrying (or planned to carry) this order.</summary>
    public Guid? DeliveryId { get; private set; }

    /// <summary>Optimistic concurrency: webhook, polling and staff can touch the same order at once.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<OrderItem> Items => _items;
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _history;
    public IReadOnlyCollection<OrderPayment> Payments => _payments;

    public decimal OutstandingAmount => Math.Max(0m, Total - AmountPaid);

    public bool IsFinal => Status is OrderStatus.Delivered or OrderStatus.Cancelled;

    public static Order Create(
        string number,
        string trackingCode,
        Customer customer,
        OrderChannel channel,
        PaymentTiming paymentTiming,
        PaymentMethod? intendedMethod,
        Address deliveryAddress,
        string? notes,
        DateTime placedAt,
        string? placedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackingCode);
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(deliveryAddress);

        if (!customer.IsActive)
        {
            throw new DomainException("Cliente inativo não pode fazer pedidos.");
        }

        var order = new Order
        {
            Number = number,
            TrackingCode = trackingCode,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CustomerPhone = customer.Phone,
            CustomerDocument = customer.Document,
            Channel = channel,
            PaymentTiming = paymentTiming,
            PaymentMethod = intendedMethod,
            PaymentStatus = PaymentStatus.Pending,
            Status = OrderStatus.Pending,
            DeliveryAddress = deliveryAddress.Clone(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            PlacedAt = placedAt
        };

        order._history.Add(new OrderStatusHistory(order.Id, null, OrderStatus.Pending, placedAt, placedBy, null));
        order.Raise(new OrderPlacedEvent(order));
        return order;
    }

    public void AddItem(Guid productId, string productName, string? sku, UnitOfMeasure unit, decimal unitPrice, decimal quantity)
    {
        EnsureItemsEditable();

        if (quantity <= 0)
        {
            throw new DomainException($"Quantidade inválida para \"{productName}\".");
        }

        if (unitPrice < 0)
        {
            throw new DomainException($"Preço inválido para \"{productName}\".");
        }

        var existing = _items.FirstOrDefault(i => i.ProductId == productId && i.UnitPrice == unitPrice);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity);
        }
        else
        {
            _items.Add(new OrderItem(Id, productId, productName, sku, unit, unitPrice, quantity));
        }

        RecalculateTotals();
    }

    public void SetDeliveryFee(decimal fee)
    {
        EnsureItemsEditable();

        if (fee < 0)
        {
            throw new DomainException("A taxa de entrega não pode ser negativa.");
        }

        DeliveryFee = decimal.Round(fee, 2);
        RecalculateTotals();
    }

    public void ApplyDiscount(decimal discount)
    {
        EnsureItemsEditable();

        if (discount < 0 || discount > Subtotal + DeliveryFee)
        {
            throw new DomainException("Desconto inválido.");
        }

        Discount = decimal.Round(discount, 2);
        RecalculateTotals();
    }

    public void SetDueDate(DateOnly dueDate)
    {
        if (PaymentTiming != PaymentTiming.Invoiced)
        {
            throw new DomainException("Somente pedidos faturados têm vencimento.");
        }

        DueDate = dueDate;
    }

    /// <summary>Validates the order is complete. Called once all items are in.</summary>
    public void EnsureReadyToPlace()
    {
        if (_items.Count == 0)
        {
            throw new DomainException("O pedido precisa ter pelo menos um item.");
        }

        if (PaymentTiming == PaymentTiming.Invoiced && DueDate is null)
        {
            throw new DomainException("Pedido faturado precisa de vencimento.");
        }
    }

    public void Confirm(DateTime now, string? by)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new DomainException($"O pedido {Number} já foi confirmado ou encerrado.");
        }

        if (PaymentTiming == PaymentTiming.Prepaid && PaymentStatus != PaymentStatus.Paid)
        {
            throw new DomainException("Pedido pré-pago só pode ser confirmado depois do pagamento.");
        }

        ConfirmedAt = now;
        ChangeStatus(OrderStatus.Confirmed, now, by, null);
    }

    /// <summary>Manual status changes available to staff on the order board.</summary>
    public void MoveTo(OrderStatus target, DateTime now, string? by, string? note = null)
    {
        switch (target)
        {
            case OrderStatus.Confirmed:
                Confirm(now, by);
                break;

            case OrderStatus.Preparing when Status == OrderStatus.Confirmed:
            case OrderStatus.ReadyForDelivery when Status is OrderStatus.Confirmed or OrderStatus.Preparing:
                ChangeStatus(target, now, by, note);
                break;

            case OrderStatus.OutForDelivery:
            case OrderStatus.Delivered:
                throw new DomainException("Saída para entrega e entrega são registradas pela rota do motorista.");

            case OrderStatus.Cancelled:
                Cancel(note ?? "Cancelado pela loja", now, by);
                break;

            default:
                throw new DomainException($"O pedido {Number} não pode ir para esta etapa a partir da etapa atual.");
        }
    }

    public void AssignToDelivery(Guid deliveryId)
    {
        if (Status is not (OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.ReadyForDelivery))
        {
            throw new DomainException($"O pedido {Number} não está liberado para entrega.");
        }

        if (DeliveryId is not null && DeliveryId != deliveryId)
        {
            throw new DomainException($"O pedido {Number} já está em outra entrega.");
        }

        DeliveryId = deliveryId;
    }

    public void UnassignFromDelivery()
    {
        if (Status == OrderStatus.OutForDelivery)
        {
            throw new DomainException($"O pedido {Number} já saiu para entrega.");
        }

        DeliveryId = null;
    }

    public void MarkOutForDelivery(Guid deliveryId, DateTime now, string? by)
    {
        if (Status is not (OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.ReadyForDelivery))
        {
            throw new DomainException($"O pedido {Number} não pode sair para entrega no status atual.");
        }

        DeliveryId = deliveryId;
        ChangeStatus(OrderStatus.OutForDelivery, now, by, null);
    }

    public void MarkDelivered(DateTime now, string? by, string? note)
    {
        if (Status != OrderStatus.OutForDelivery)
        {
            throw new DomainException($"O pedido {Number} não está em rota de entrega.");
        }

        DeliveredAt = now;
        ChangeStatus(OrderStatus.Delivered, now, by, note);
    }

    /// <summary>The driver could not deliver: back to the queue so it can go on another run.</summary>
    public void ReturnFromFailedDelivery(DateTime now, string? by, string reason)
    {
        if (Status != OrderStatus.OutForDelivery)
        {
            throw new DomainException($"O pedido {Number} não está em rota de entrega.");
        }

        DeliveryId = null;
        ChangeStatus(OrderStatus.ReadyForDelivery, now, by, $"Entrega não realizada: {reason}");
    }

    public void Cancel(string reason, DateTime now, string? by)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Informe o motivo do cancelamento.");
        }

        if (Status is OrderStatus.OutForDelivery or OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            throw new DomainException($"O pedido {Number} não pode mais ser cancelado.");
        }

        if (DeliveryId is not null)
        {
            throw new DomainException($"Retire o pedido {Number} da entrega planejada antes de cancelar.");
        }

        CancelledAt = now;
        CancellationReason = reason.Trim();
        ChangeStatus(OrderStatus.Cancelled, now, by, CancellationReason);
    }

    /// <summary>
    /// Records money received (online, at the door or on an invoice). A prepaid order is confirmed
    /// automatically the moment it becomes fully paid.
    /// </summary>
    public OrderPayment RegisterPayment(
        PaymentMethod method,
        decimal amount,
        DateTime paidAt,
        string? reference,
        string? registeredBy,
        Guid? transactionId = null)
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new DomainException($"O pedido {Number} está cancelado.");
        }

        if (amount <= 0)
        {
            throw new DomainException("O valor do pagamento deve ser maior que zero.");
        }

        if (PaymentStatus == PaymentStatus.Paid)
        {
            throw new DomainException($"O pedido {Number} já está pago.");
        }

        if (transactionId is not null && _payments.Any(p => p.PaymentTransactionId == transactionId))
        {
            throw new DomainException("Este pagamento já foi registrado.");
        }

        var payment = new OrderPayment(Id, method, decimal.Round(amount, 2), paidAt, reference, registeredBy, transactionId);
        _payments.Add(payment);

        var previous = PaymentStatus;
        AmountPaid += payment.Amount;
        PaymentMethod = method;

        if (AmountPaid >= Total)
        {
            PaymentStatus = PaymentStatus.Paid;
            PaidAt = paidAt;
        }
        else
        {
            PaymentStatus = PaymentStatus.PartiallyPaid;
        }

        if (previous != PaymentStatus)
        {
            Raise(new OrderPaymentStatusChangedEvent(this, previous, PaymentStatus));
        }

        if (PaymentTiming == PaymentTiming.Prepaid && Status == OrderStatus.Pending && PaymentStatus == PaymentStatus.Paid)
        {
            Confirm(paidAt, registeredBy);
        }

        return payment;
    }

    public bool IsOverdue(DateOnly today) =>
        PaymentTiming == PaymentTiming.Invoiced &&
        PaymentStatus != PaymentStatus.Paid &&
        Status != OrderStatus.Cancelled &&
        DueDate is not null &&
        DueDate.Value < today;

    private void EnsureItemsEditable()
    {
        if (Status != OrderStatus.Pending || AmountPaid > 0)
        {
            throw new DomainException($"O pedido {Number} não pode mais ser alterado.");
        }
    }

    private void RecalculateTotals()
    {
        Subtotal = _items.Sum(i => i.Total);
        Total = Math.Max(0m, Subtotal + DeliveryFee - Discount);
    }

    private void ChangeStatus(OrderStatus to, DateTime now, string? by, string? note)
    {
        var from = Status;
        if (from == to)
        {
            return;
        }

        Status = to;
        _history.Add(new OrderStatusHistory(Id, from, to, now, by, note));
        Raise(new OrderStatusChangedEvent(this, from, to));
    }
}
