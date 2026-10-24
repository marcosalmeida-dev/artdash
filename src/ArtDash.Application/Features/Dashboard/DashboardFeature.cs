using ArtDash.Application.Features.Orders;
using ArtDash.Shared.Contracts.Dashboard;

namespace ArtDash.Application.Features.Dashboard;

public sealed record GetDashboardQuery : IQuery<DashboardDto>;

internal sealed class GetDashboardQueryHandler(IApplicationDbContext db, ICurrentTenant currentTenant, TimeProvider clock)
    : IQueryHandler<GetDashboardQuery, DashboardDto>
{
    public async Task<DashboardDto> Handle(GetDashboardQuery query, CancellationToken cancellationToken)
    {
        var zone = await TenantTime.GetZoneAsync(db, currentTenant, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var (todayStart, todayEnd) = TenantClock.DayBoundsUtc(now, zone);
        var (weekStart, _) = TenantClock.DayBoundsUtc(now, zone, -6);
        var today = TenantTime.Today(now, zone);

        var orders = db.Orders.AsNoTracking();
        var placedToday = orders.Where(o => o.PlacedAt >= todayStart && o.PlacedAt < todayEnd && o.Status != OrderStatus.Cancelled);

        var ordersToday = await placedToday.CountAsync(cancellationToken);
        var revenueToday = await placedToday.SumAsync(o => (decimal?)o.Total, cancellationToken) ?? 0m;

        var statusCounts = await orders
            .Where(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(params OrderStatus[] statuses) => statusCounts.Where(s => statuses.Contains(s.Status)).Sum(s => s.Count);

        var activeDeliveries = await db.Deliveries.CountAsync(d => d.Status == DeliveryStatus.InProgress, cancellationToken);

        var openInvoiced = orders.Where(o => o.PaymentTiming == PaymentTiming.Invoiced
                                             && o.Status != OrderStatus.Cancelled
                                             && o.PaymentStatus != PaymentStatus.Paid);
        var receivablesOpen = await openInvoiced.SumAsync(o => (decimal?)(o.Total - o.AmountPaid), cancellationToken) ?? 0m;
        var overdue = openInvoiced.Where(o => o.DueDate < today);
        var receivablesOverdue = await overdue.SumAsync(o => (decimal?)(o.Total - o.AmountPaid), cancellationToken) ?? 0m;
        var overdueCount = await overdue.CountAsync(cancellationToken);

        var weekOrders = await orders
            .Where(o => o.PlacedAt >= weekStart && o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.PlacedAt, o.Total })
            .ToListAsync(cancellationToken);

        var last7Days = Enumerable.Range(-6, 7)
            .Select(offset => DateOnly.FromDateTime(TenantClock.LocalToday(now, zone).AddDays(offset)))
            .Select(day =>
            {
                var dayOrders = weekOrders.Where(o => DateOnly.FromDateTime(TenantClock.ToLocal(o.PlacedAt, zone)) == day).ToList();
                return new DailySalesDto(day, dayOrders.Count, dayOrders.Sum(o => o.Total));
            })
            .ToList();

        var recent = await orders
            .OrderByDescending(o => o.PlacedAt)
            .Take(8)
            .Select(OrderProjections.ToListItem)
            .ToListAsync(cancellationToken);

        return new DashboardDto
        {
            OrdersToday = ordersToday,
            RevenueToday = revenueToday,
            PendingOrders = CountOf(OrderStatus.Pending),
            InPreparation = CountOf(OrderStatus.Confirmed, OrderStatus.Preparing),
            ReadyForDelivery = CountOf(OrderStatus.ReadyForDelivery),
            OutForDelivery = CountOf(OrderStatus.OutForDelivery),
            ActiveDeliveries = activeDeliveries,
            ReceivablesOpen = receivablesOpen,
            ReceivablesOverdue = receivablesOverdue,
            OverdueCount = overdueCount,
            Last7Days = last7Days,
            RecentOrders = recent
        };
    }
}
