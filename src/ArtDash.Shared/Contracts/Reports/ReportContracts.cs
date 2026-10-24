using ArtDash.Shared.Enums;

namespace ArtDash.Shared.Contracts.Reports;

public sealed record ReportQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public OrderChannel? Channel { get; init; }
}

/// <summary>
/// Orders are grouped by their placement date in the tenant's time zone. Financial balances are
/// the current state of those orders, not cash received during the period. Delivery runs use their
/// scheduled date and include all channels. Rankings are limited to the first ten results.
/// </summary>
public sealed record ReportDto
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public DateTime GeneratedAt { get; init; }
    public ReportSummaryDto Summary { get; init; } = new();
    public IReadOnlyList<ReportDailySalesDto> DailySales { get; init; } = [];
    public IReadOnlyList<ReportOrderStatusDto> OrdersByStatus { get; init; } = [];
    public IReadOnlyList<ReportChannelDto> OrdersByChannel { get; init; } = [];
    public IReadOnlyList<ReportPaymentTimingDto> PaymentsByTiming { get; init; } = [];
    public IReadOnlyList<ReportPaymentStatusDto> PaymentsByStatus { get; init; } = [];
    public IReadOnlyList<ReportTopCustomerDto> TopCustomers { get; init; } = [];
    public IReadOnlyList<ReportTopProductDto> TopProducts { get; init; } = [];
    public ReportDeliverySummaryDto Deliveries { get; init; } = new();
    public IReadOnlyList<ReportDriverDto> Drivers { get; init; } = [];
}

public sealed record ReportSummaryDto
{
    public int Orders { get; init; }
    public int ValidOrders { get; init; }
    public int CancelledOrders { get; init; }
    public decimal Revenue { get; init; }
    public decimal AverageTicket { get; init; }
    public decimal AmountPaid { get; init; }
    public decimal OutstandingAmount { get; init; }
    public decimal OverdueAmount { get; init; }
    public int OverdueCount { get; init; }
    public int ActiveCustomers { get; init; }
    /// <summary>Customers whose first non-cancelled order, across all channels, is in the period.</summary>
    public int NewCustomers { get; init; }
    public int ReturningCustomers { get; init; }
}

public sealed record ReportDailySalesDto(DateOnly Date, int Orders, decimal Revenue);
public sealed record ReportOrderStatusDto(OrderStatus Status, int Orders, decimal Revenue);
public sealed record ReportChannelDto(OrderChannel Channel, int Orders, decimal Revenue);
public sealed record ReportPaymentTimingDto(PaymentTiming Timing, int Orders, decimal Revenue, decimal AmountPaid, decimal OutstandingAmount);
public sealed record ReportPaymentStatusDto(PaymentStatus Status, int Orders, decimal Revenue, decimal AmountPaid, decimal OutstandingAmount);

public sealed record ReportTopCustomerDto(
    Guid CustomerId,
    string Name,
    CustomerType? Type,
    int Orders,
    decimal Revenue,
    decimal AverageTicket,
    DateTime LastOrderAt);

/// <summary>Item revenue before order-level discounts and delivery fees; historical item snapshots are preserved.</summary>
public sealed record ReportTopProductDto(Guid ProductId, string Name, UnitOfMeasure Unit, decimal Quantity, int Orders, decimal Revenue);

public sealed record ReportDeliverySummaryDto
{
    public int Runs { get; init; }
    public int Planned { get; init; }
    public int InProgress { get; init; }
    public int Completed { get; init; }
    public int Cancelled { get; init; }
    public int Stops { get; init; }
    public int DeliveredStops { get; init; }
    public int FailedStops { get; init; }
    public int PendingStops { get; init; }
}

public sealed record ReportDriverDto
{
    public Guid DriverId { get; init; }
    public string DriverName { get; init; } = string.Empty;
    public int Runs { get; init; }
    public int Planned { get; init; }
    public int InProgress { get; init; }
    public int Completed { get; init; }
    public int Cancelled { get; init; }
    public int Stops { get; init; }
    public int DeliveredStops { get; init; }
    public int FailedStops { get; init; }
    public int PendingStops { get; init; }
}