using ArtDash.Shared.Contracts.Reports;

namespace ArtDash.Application.Features.Reports;

public sealed record GetReportsQuery(ReportQuery Filter) : IQuery<ReportDto>;

internal sealed class GetReportsQueryValidator : AbstractValidator<GetReportsQuery>
{
    public GetReportsQueryValidator()
    {
        RuleFor(x => x.Filter).NotNull();
        When(x => x.Filter is not null, () =>
        {
            RuleFor(x => x.Filter.Channel).IsInEnum().When(x => x.Filter.Channel is not null);
            RuleFor(x => x.Filter.To)
                .Must(to => to is null || to < DateOnly.MaxValue)
                .WithMessage("A data final deve ser anterior a 31/12/9999.");
            RuleFor(x => x.Filter.From)
                .Must((query, from) => from is null || query.Filter.To is null || from <= query.Filter.To)
                .WithMessage("A data inicial deve ser anterior ou igual à data final.");
            RuleFor(x => x.Filter.To)
                .Must((query, to) => query.Filter.From is not { } from || to is null || to.Value.DayNumber - from.DayNumber < 366)
                .WithMessage("Selecione um período de no máximo 366 dias.");
        });
    }
}

internal sealed class GetReportsQueryHandler(IApplicationDbContext db, ICurrentTenant currentTenant, TimeProvider clock)
    : IQueryHandler<GetReportsQuery, ReportDto>
{
    public async Task<ReportDto> Handle(GetReportsQuery query, CancellationToken cancellationToken)
    {
        var zone = await TenantTime.GetZoneAsync(db, currentTenant, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var today = TenantTime.Today(now, zone);
        var to = query.Filter.To ?? today;
        var from = query.Filter.From ?? DateOnly.FromDayNumber(Math.Max(0, to.DayNumber - 29));

        // Defaults depend on the tenant's local date, so validate the resolved range here as well.
        if (from > to)
        {
            throw new RequestValidationException(nameof(ReportQuery.From), "A data inicial deve ser anterior ou igual à data final.");
        }

        if (to.DayNumber - from.DayNumber >= 366)
        {
            throw new RequestValidationException(nameof(ReportQuery.To), "Selecione um período de no máximo 366 dias.");
        }

        if (to == DateOnly.MaxValue)
        {
            throw new RequestValidationException(nameof(ReportQuery.To), "A data final deve ser anterior a 31/12/9999.");
        }

        var fromUtc = TenantTime.StartOfDayUtc(from.ToDateTime(TimeOnly.MinValue), zone);
        var toUtc = TenantTime.EndOfDayUtc(to.ToDateTime(TimeOnly.MinValue), zone);
        var orders = db.Orders.AsNoTracking()
            .Where(o => o.PlacedAt >= fromUtc && o.PlacedAt < toUtc)
            .WhereIf(query.Filter.Channel is not null, o => o.Channel == query.Filter.Channel);
        var validOrders = orders.Where(o => o.Status != OrderStatus.Cancelled);

        // A small grouped result supports every financial breakdown without loading order aggregates.
        var groups = await orders
            .GroupBy(o => new { o.Status, o.Channel, o.PaymentTiming, o.PaymentStatus })
            .Select(g => new
            {
                g.Key.Status,
                g.Key.Channel,
                g.Key.PaymentTiming,
                g.Key.PaymentStatus,
                Orders = g.Count(),
                Revenue = g.Sum(o => o.Total),
                AmountPaid = g.Sum(o => o.AmountPaid),
                OutstandingAmount = g.Sum(o => o.Total > o.AmountPaid ? o.Total - o.AmountPaid : 0m),
                OverdueAmount = g.Sum(o => o.PaymentTiming == PaymentTiming.Invoiced && o.DueDate < today && o.Total > o.AmountPaid
                    ? o.Total - o.AmountPaid : 0m),
                OverdueCount = g.Count(o => o.PaymentTiming == PaymentTiming.Invoiced && o.DueDate < today && o.Total > o.AmountPaid)
            })
            .ToListAsync(cancellationToken);
        var validGroups = groups.Where(g => g.Status != OrderStatus.Cancelled).ToList();
        var validCount = validGroups.Sum(g => g.Orders);
        var revenue = validGroups.Sum(g => g.Revenue);

        var activeCustomerIds = validOrders.Select(o => o.CustomerId).Distinct();
        var previousCustomerIds = db.Orders.AsNoTracking()
            .Where(o => o.Status != OrderStatus.Cancelled && o.PlacedAt < fromUtc)
            .Select(o => o.CustomerId);
        var activeCustomers = await activeCustomerIds.CountAsync(cancellationToken);
        var returningCustomers = await activeCustomerIds.CountAsync(id => previousCustomerIds.Contains(id), cancellationToken);

        // Time-zone conversion is done after the SQL projection so local days also handle DST.
        var datedOrders = await validOrders
            .Select(o => new { o.PlacedAt, o.Total })
            .ToListAsync(cancellationToken);
        var salesByDay = datedOrders
            .GroupBy(o => DateOnly.FromDateTime(TenantClock.ToLocal(o.PlacedAt, zone)))
            .ToDictionary(g => g.Key, g => new ReportDailySalesDto(g.Key, g.Count(), g.Sum(o => o.Total)));
        var dailySales = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(offset => from.AddDays(offset))
            .Select(day => salesByDay.GetValueOrDefault(day) ?? new ReportDailySalesDto(day, 0, 0m))
            .ToList();

        var topCustomers = await GetTopCustomersAsync(validOrders, cancellationToken);
        var topProducts = await GetTopProductsAsync(validOrders, cancellationToken);
        var drivers = await GetDriversAsync(fromUtc, toUtc, cancellationToken);

        return new ReportDto
        {
            From = from,
            To = to,
            GeneratedAt = now,
            Summary = new ReportSummaryDto
            {
                Orders = groups.Sum(g => g.Orders),
                ValidOrders = validCount,
                CancelledOrders = groups.Where(g => g.Status == OrderStatus.Cancelled).Sum(g => g.Orders),
                Revenue = revenue,
                AverageTicket = validCount == 0 ? 0m : decimal.Round(revenue / validCount, 2),
                AmountPaid = validGroups.Sum(g => g.AmountPaid),
                OutstandingAmount = validGroups.Sum(g => g.OutstandingAmount),
                OverdueAmount = validGroups.Sum(g => g.OverdueAmount),
                OverdueCount = validGroups.Sum(g => g.OverdueCount),
                ActiveCustomers = activeCustomers,
                NewCustomers = activeCustomers - returningCustomers,
                ReturningCustomers = returningCustomers
            },
            DailySales = dailySales,
            OrdersByStatus = groups.GroupBy(g => g.Status)
                .OrderBy(g => g.Key)
                .Select(g => new ReportOrderStatusDto(g.Key, g.Sum(x => x.Orders),
                    g.Key == OrderStatus.Cancelled ? 0m : g.Sum(x => x.Revenue)))
                .ToList(),
            OrdersByChannel = validGroups.GroupBy(g => g.Channel)
                .OrderBy(g => g.Key)
                .Select(g => new ReportChannelDto(g.Key, g.Sum(x => x.Orders), g.Sum(x => x.Revenue)))
                .ToList(),
            PaymentsByTiming = validGroups.GroupBy(g => g.PaymentTiming)
                .OrderBy(g => g.Key)
                .Select(g => new ReportPaymentTimingDto(g.Key, g.Sum(x => x.Orders), g.Sum(x => x.Revenue),
                    g.Sum(x => x.AmountPaid), g.Sum(x => x.OutstandingAmount)))
                .ToList(),
            PaymentsByStatus = validGroups.GroupBy(g => g.PaymentStatus)
                .OrderBy(g => g.Key)
                .Select(g => new ReportPaymentStatusDto(g.Key, g.Sum(x => x.Orders), g.Sum(x => x.Revenue),
                    g.Sum(x => x.AmountPaid), g.Sum(x => x.OutstandingAmount)))
                .ToList(),
            TopCustomers = topCustomers,
            TopProducts = topProducts,
            Drivers = drivers,
            Deliveries = new ReportDeliverySummaryDto
            {
                Runs = drivers.Sum(d => d.Runs),
                Planned = drivers.Sum(d => d.Planned),
                InProgress = drivers.Sum(d => d.InProgress),
                Completed = drivers.Sum(d => d.Completed),
                Cancelled = drivers.Sum(d => d.Cancelled),
                Stops = drivers.Sum(d => d.Stops),
                DeliveredStops = drivers.Sum(d => d.DeliveredStops),
                FailedStops = drivers.Sum(d => d.FailedStops),
                PendingStops = drivers.Sum(d => d.PendingStops)
            }
        };
    }

    private async Task<IReadOnlyList<ReportTopCustomerDto>> GetTopCustomersAsync(IQueryable<Order> orders, CancellationToken cancellationToken)
    {
        var ranking = await orders
            .GroupBy(o => o.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Name = g.OrderByDescending(o => o.PlacedAt).ThenByDescending(o => o.Id).Select(o => o.CustomerName).First(),
                Orders = g.Count(),
                Revenue = g.Sum(o => o.Total),
                LastOrderAt = g.Max(o => o.PlacedAt)
            })
            .OrderByDescending(g => g.Revenue).ThenByDescending(g => g.Orders).ThenBy(g => g.CustomerId)
            .Take(10)
            .ToListAsync(cancellationToken);
        var ids = ranking.Select(c => c.CustomerId).ToList();
        var types = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Type }).ToDictionaryAsync(c => c.Id, c => c.Type, cancellationToken);

        return ranking.Select(c => new ReportTopCustomerDto(c.CustomerId, c.Name,
                types.TryGetValue(c.CustomerId, out var type) ? type : null,
                c.Orders, c.Revenue, decimal.Round(c.Revenue / c.Orders, 2), c.LastOrderAt))
            .ToList();
    }

    private async Task<IReadOnlyList<ReportTopProductDto>> GetTopProductsAsync(IQueryable<Order> orders, CancellationToken cancellationToken)
    {
        // Join by IDs instead of navigating the catalog: deleted products remain in sales history.
        var items = from item in db.OrderItems.AsNoTracking()
                    join order in orders on item.OrderId equals order.Id
                    select new { item.ProductId, item.ProductName, item.Unit, item.Quantity, item.Total, item.OrderId, order.PlacedAt };

        return await items
            .GroupBy(i => new { i.ProductId, i.Unit })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.Unit,
                Name = g.OrderByDescending(i => i.PlacedAt).ThenByDescending(i => i.OrderId).Select(i => i.ProductName).First(),
                Quantity = g.Sum(i => i.Quantity),
                Orders = g.Select(i => i.OrderId).Distinct().Count(),
                Revenue = g.Sum(i => i.Total)
            })
            .OrderByDescending(p => p.Revenue).ThenBy(p => p.ProductId).ThenBy(p => p.Unit)
            .Take(10)
            .Select(p => new ReportTopProductDto(p.ProductId, p.Name, p.Unit, p.Quantity, p.Orders, p.Revenue))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ReportDriverDto>> GetDriversAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        // A route can mix order channels. Delivery reports deliberately cover the full operation.
        var deliveries = db.Deliveries.AsNoTracking()
            .Where(d => d.ScheduledFor >= fromUtc && d.ScheduledFor < toUtc);
        var runs = await deliveries
            .GroupBy(d => d.DriverId)
            .Select(g => new
            {
                DriverId = g.Key,
                Runs = g.Count(),
                Planned = g.Count(d => d.Status == DeliveryStatus.Planned),
                InProgress = g.Count(d => d.Status == DeliveryStatus.InProgress),
                Completed = g.Count(d => d.Status == DeliveryStatus.Completed),
                Cancelled = g.Count(d => d.Status == DeliveryStatus.Cancelled)
            })
            .ToListAsync(cancellationToken);
        var stops = await (from stop in db.DeliveryStops.AsNoTracking()
                           join delivery in deliveries on stop.DeliveryId equals delivery.Id
                           select new { delivery.DriverId, delivery.Status, StopStatus = stop.Status })
            // Cancelled runs retain their planned stops; those were not delivery attempts.
            .Where(s => s.Status != DeliveryStatus.Cancelled)
            .GroupBy(s => s.DriverId)
            .Select(g => new
            {
                DriverId = g.Key,
                Stops = g.Count(),
                Delivered = g.Count(s => s.StopStatus == DeliveryStopStatus.Delivered),
                Failed = g.Count(s => s.StopStatus == DeliveryStopStatus.Failed),
                Pending = g.Count(s => s.StopStatus == DeliveryStopStatus.Pending)
            })
            .ToDictionaryAsync(s => s.DriverId, cancellationToken);
        var ids = runs.Select(d => d.DriverId).ToList();
        var names = await db.Drivers.AsNoTracking().Where(d => ids.Contains(d.Id))
            .Select(d => new { d.Id, d.Name }).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        return runs.Select(d => new ReportDriverDto
            {
                DriverId = d.DriverId,
                DriverName = names.GetValueOrDefault(d.DriverId) ?? string.Empty,
                Runs = d.Runs,
                Planned = d.Planned,
                InProgress = d.InProgress,
                Completed = d.Completed,
                Cancelled = d.Cancelled,
                Stops = stops.TryGetValue(d.DriverId, out var counts) ? counts.Stops : 0,
                DeliveredStops = stops.TryGetValue(d.DriverId, out var delivered) ? delivered.Delivered : 0,
                FailedStops = stops.TryGetValue(d.DriverId, out var failed) ? failed.Failed : 0,
                PendingStops = stops.TryGetValue(d.DriverId, out var pending) ? pending.Pending : 0
            })
            .OrderByDescending(d => d.DeliveredStops).ThenByDescending(d => d.Runs).ThenBy(d => d.DriverName).ThenBy(d => d.DriverId)
            .ToList();
    }
}