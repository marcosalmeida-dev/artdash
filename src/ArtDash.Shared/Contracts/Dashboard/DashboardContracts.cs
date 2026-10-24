using ArtDash.Shared.Contracts.Orders;

namespace ArtDash.Shared.Contracts.Dashboard;

public sealed record DashboardDto
{
    public int OrdersToday { get; init; }
    public decimal RevenueToday { get; init; }
    public int PendingOrders { get; init; }
    public int InPreparation { get; init; }
    public int ReadyForDelivery { get; init; }
    public int OutForDelivery { get; init; }
    public int ActiveDeliveries { get; init; }
    public decimal ReceivablesOpen { get; init; }
    public decimal ReceivablesOverdue { get; init; }
    public int OverdueCount { get; init; }
    public IReadOnlyList<DailySalesDto> Last7Days { get; init; } = [];
    public IReadOnlyList<OrderListItemDto> RecentOrders { get; init; } = [];
}

public sealed record DailySalesDto(DateOnly Date, int Orders, decimal Revenue);
