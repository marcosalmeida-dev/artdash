using ArtDash.Application.Features.Customers;
using ArtDash.Shared.Contracts.Orders;

namespace ArtDash.Application.Features.Orders;

// ---------------------------------------------------------------- Staff

public sealed record SearchOrdersQuery(OrderQuery Filter) : IQuery<PagedResult<OrderListItemDto>>;

internal sealed class SearchOrdersQueryHandler(IApplicationDbContext db, ICurrentTenant currentTenant)
    : IQueryHandler<SearchOrdersQuery, PagedResult<OrderListItemDto>>
{
    public async Task<PagedResult<OrderListItemDto>> Handle(SearchOrdersQuery query, CancellationToken cancellationToken)
    {
        var filter = query.Filter;
        var term = filter.Search.ToSearchTerm();
        var zone = await TenantTime.GetZoneAsync(db, currentTenant, cancellationToken);
        DateTime? fromUtc = filter.From is { } from ? TenantTime.StartOfDayUtc(from, zone) : null;
        DateTime? toUtc = filter.To is { } to ? TenantTime.EndOfDayUtc(to, zone) : null;

        return await db.Orders.AsNoTracking()
            .WhereIf(term is not null, o => o.Number.Contains(term!) || o.CustomerName.ToLower().Contains(term!))
            .WhereIf(filter.Status is not null, o => o.Status == filter.Status)
            .WhereIf(filter.PaymentStatus is not null, o => o.PaymentStatus == filter.PaymentStatus)
            .WhereIf(filter.PaymentTiming is not null, o => o.PaymentTiming == filter.PaymentTiming)
            .WhereIf(fromUtc is not null, o => o.PlacedAt >= fromUtc)
            .WhereIf(toUtc is not null, o => o.PlacedAt < toUtc)
            .OrderByDescending(o => o.PlacedAt)
            .Select(OrderProjections.ToListItem)
            .ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }
}

/// <summary>Everything still in motion, for the kanban board.</summary>
public sealed record GetOrderBoardQuery : IQuery<IReadOnlyList<OrderListItemDto>>;

internal sealed class GetOrderBoardQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetOrderBoardQuery, IReadOnlyList<OrderListItemDto>>
{
    private static readonly OrderStatus[] BoardStatuses =
    [
        OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Preparing,
        OrderStatus.ReadyForDelivery, OrderStatus.OutForDelivery
    ];

    public async Task<IReadOnlyList<OrderListItemDto>> Handle(GetOrderBoardQuery query, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking()
            .Where(o => BoardStatuses.Contains(o.Status))
            .OrderBy(o => o.PlacedAt)
            .Select(OrderProjections.ToListItem)
            .Take(500)
            .ToListAsync(cancellationToken);
}

public sealed record GetOrderQuery(Guid Id) : IQuery<OrderDto>;

internal sealed class GetOrderQueryHandler(IApplicationDbContext db, ICurrentTenant currentTenant, TimeProvider clock)
    : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderQuery query, CancellationToken cancellationToken)
    {
        var zone = await TenantTime.GetZoneAsync(db, currentTenant, cancellationToken);
        var today = TenantTime.Today(clock.GetUtcNow().UtcDateTime, zone);

        return await OrderDetailsLoader.LoadAsync(db, o => o.Id == query.Id, today, cancellationToken)
               ?? throw new NotFoundException("Pedido", query.Id);
    }
}

public sealed record GetReceivablesQuery(ReceivableQuery Filter) : IQuery<ReceivablesDto>;

internal sealed class GetReceivablesQueryHandler(IApplicationDbContext db, ICurrentTenant currentTenant, TimeProvider clock)
    : IQueryHandler<GetReceivablesQuery, ReceivablesDto>
{
    public async Task<ReceivablesDto> Handle(GetReceivablesQuery query, CancellationToken cancellationToken)
    {
        var filter = query.Filter;
        var zone = await TenantTime.GetZoneAsync(db, currentTenant, cancellationToken);
        var today = TenantTime.Today(clock.GetUtcNow().UtcDateTime, zone);
        var term = filter.Search.ToSearchTerm();
        DateOnly? dueFrom = filter.DueFrom is { } from ? DateOnly.FromDateTime(from) : null;
        DateOnly? dueTo = filter.DueTo is { } to ? DateOnly.FromDateTime(to) : null;

        var invoiced = db.Orders.AsNoTracking()
            .Where(o => o.PaymentTiming == PaymentTiming.Invoiced && o.Status != OrderStatus.Cancelled);

        var open = invoiced.Where(o => o.PaymentStatus != PaymentStatus.Paid);
        var totalOpen = await open.SumAsync(o => (decimal?)(o.Total - o.AmountPaid), cancellationToken) ?? 0m;
        var overdue = open.Where(o => o.DueDate < today);
        var totalOverdue = await overdue.SumAsync(o => (decimal?)(o.Total - o.AmountPaid), cancellationToken) ?? 0m;
        var overdueCount = await overdue.CountAsync(cancellationToken);

        var filtered = filter.Filter switch
        {
            ReceivableFilter.Open => open,
            ReceivableFilter.Overdue => overdue,
            ReceivableFilter.Paid => invoiced.Where(o => o.PaymentStatus == PaymentStatus.Paid),
            _ => invoiced
        };

        var page = await filtered
            .WhereIf(term is not null, o => o.Number.Contains(term!) || o.CustomerName.ToLower().Contains(term!))
            .WhereIf(dueFrom is not null, o => o.DueDate >= dueFrom)
            .WhereIf(dueTo is not null, o => o.DueDate <= dueTo)
            .OrderBy(o => o.DueDate).ThenBy(o => o.Number)
            .Select(o => new ReceivableDto
            {
                OrderId = o.Id,
                Number = o.Number,
                CustomerId = o.CustomerId,
                CustomerName = o.CustomerName,
                Total = o.Total,
                AmountPaid = o.AmountPaid,
                Outstanding = o.Total - o.AmountPaid,
                PlacedAt = o.PlacedAt,
                DueDate = o.DueDate,
                PaymentStatus = o.PaymentStatus,
                IsOverdue = o.PaymentStatus != PaymentStatus.Paid && o.DueDate < today
            })
            .ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);

        var items = page.Items
            .Select(r => r with
            {
                Outstanding = Math.Max(0m, r.Outstanding),
                DaysOverdue = r.IsOverdue && r.DueDate is { } due ? today.DayNumber - due.DayNumber : 0
            })
            .ToList();

        return new ReceivablesDto(page with { Items = items }, totalOpen, totalOverdue, overdueCount);
    }
}

/// <summary>Orders that can go on a delivery run, with their total weight.</summary>
public sealed record GetDeliverableOrdersQuery : IQuery<IReadOnlyList<DeliverableOrderDto>>;

internal sealed class GetDeliverableOrdersQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetDeliverableOrdersQuery, IReadOnlyList<DeliverableOrderDto>>
{
    public async Task<IReadOnlyList<DeliverableOrderDto>> Handle(GetDeliverableOrdersQuery query, CancellationToken cancellationToken)
    {
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.DeliveryId == null &&
                        (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.ReadyForDelivery))
            .OrderBy(o => o.PlacedAt)
            .Select(o => new
            {
                o.Id, o.Number, o.CustomerName, o.Status, o.PaymentTiming, o.PaymentStatus, o.Total,
                o.DeliveryAddress.District, o.DeliveryAddress.City, o.DeliveryAddress.Latitude, o.DeliveryAddress.Longitude, o.PlacedAt
            })
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var lines = await db.OrderItems.AsNoTracking()
            .Where(i => orderIds.Contains(i.OrderId))
            .Select(i => new { i.OrderId, i.ProductId, i.Quantity })
            .ToListAsync(cancellationToken);

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var weights = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.WeightKg != null)
            .ToDictionaryAsync(p => p.Id, p => p.WeightKg!.Value, cancellationToken);

        return orders.Select(o =>
        {
            var orderLines = lines.Where(l => l.OrderId == o.Id).ToList();
            decimal? weight = orderLines.Any(l => weights.ContainsKey(l.ProductId))
                ? orderLines.Sum(l => weights.TryGetValue(l.ProductId, out var w) ? w * l.Quantity : 0m)
                : null;

            return new DeliverableOrderDto
            {
                OrderId = o.Id,
                Number = o.Number,
                CustomerName = o.CustomerName,
                Status = o.Status,
                PaymentTiming = o.PaymentTiming,
                PaymentStatus = o.PaymentStatus,
                Total = o.Total,
                TotalWeightKg = weight,
                District = o.District,
                City = o.City,
                Latitude = o.Latitude,
                Longitude = o.Longitude,
                PlacedAt = o.PlacedAt
            };
        }).ToList();
    }
}

// ---------------------------------------------------------------- Customer (own orders)

public sealed record GetMyOrdersQuery(int Page = 1, int PageSize = 20) : IQuery<PagedResult<OrderListItemDto>>;

internal sealed class GetMyOrdersQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyOrdersQuery, PagedResult<OrderListItemDto>>
{
    public async Task<PagedResult<OrderListItemDto>> Handle(GetMyOrdersQuery query, CancellationToken cancellationToken)
    {
        var customer = await CustomerRules.GetForUserAsync(db, currentUser.RequiredUserId(), tracking: false, cancellationToken);

        return await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customer.Id)
            .OrderByDescending(o => o.PlacedAt)
            .Select(OrderProjections.ToListItem)
            .ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);
    }
}

public sealed record GetMyOrderQuery(Guid Id) : IQuery<OrderDto>;

internal sealed class GetMyOrderQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, ICurrentTenant currentTenant, TimeProvider clock)
    : IQueryHandler<GetMyOrderQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetMyOrderQuery query, CancellationToken cancellationToken)
    {
        var customer = await CustomerRules.GetForUserAsync(db, currentUser.RequiredUserId(), tracking: false, cancellationToken);
        var zone = await TenantTime.GetZoneAsync(db, currentTenant, cancellationToken);
        var today = TenantTime.Today(clock.GetUtcNow().UtcDateTime, zone);

        return await OrderDetailsLoader.LoadAsync(db, o => o.Id == query.Id && o.CustomerId == customer.Id, today, cancellationToken)
               ?? throw new NotFoundException("Pedido", query.Id);
    }
}

// ---------------------------------------------------------------- Public tracking

public sealed record GetOrderTrackingQuery(string TrackingCode) : IQuery<OrderTrackingDto>;

internal sealed class GetOrderTrackingQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetOrderTrackingQuery, OrderTrackingDto>
{
    public async Task<OrderTrackingDto> Handle(GetOrderTrackingQuery query, CancellationToken cancellationToken)
    {
        var code = (query.TrackingCode ?? string.Empty).Trim().ToUpperInvariant();

        // Tracking codes are unique across tenants and unguessable, so the link works on its own.
        var order = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.TrackingCode == code)
            .Select(o => new
            {
                o.Id,
                o.TenantId,
                o.Number,
                o.Status,
                o.PlacedAt,
                o.DeliveredAt,
                o.DeliveryId,
                o.DeliveryAddress.District,
                o.DeliveryAddress.City,
                o.DeliveryAddress.Latitude,
                o.DeliveryAddress.Longitude
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Pedido", code);

        var history = await db.OrderStatusHistory.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.OrderId == order.Id)
            .OrderBy(h => h.ChangedAt)
            .Select(h => new OrderStatusHistoryDto(h.FromStatus, h.ToStatus, h.ChangedAt, null, null))
            .ToListAsync(cancellationToken);

        var storeName = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == order.TenantId)
            .Select(t => t.Branding.DisplayName ?? t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var dto = new OrderTrackingDto
        {
            Number = order.Number,
            StoreName = storeName,
            Status = order.Status,
            PlacedAt = order.PlacedAt,
            DeliveredAt = order.DeliveredAt,
            DeliveryArea = string.Join(" - ", new[] { order.District, order.City }.Where(s => !string.IsNullOrWhiteSpace(s))),
            History = history,
            DestinationLatitude = order.Latitude,
            DestinationLongitude = order.Longitude
        };

        if (order.Status != OrderStatus.OutForDelivery || order.DeliveryId is null)
        {
            return dto;
        }

        var delivery = await db.Deliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.Id == order.DeliveryId)
            .Select(d => new
            {
                DriverName = d.Driver.Name,
                d.Vehicle.Plate,
                d.Vehicle.Type,
                d.LastLatitude,
                d.LastLongitude,
                d.LastPositionAt,
                Stops = d.Stops.Select(s => new { s.OrderId, s.Sequence, s.Status }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (delivery is null)
        {
            return dto;
        }

        var myStop = delivery.Stops.FirstOrDefault(s => s.OrderId == order.Id);
        var stopsBefore = myStop is null
            ? (int?)null
            : delivery.Stops.Count(s => s.Status == DeliveryStopStatus.Pending && s.Sequence < myStop.Sequence);

        return dto with
        {
            DriverName = delivery.DriverName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            VehiclePlate = delivery.Plate,
            VehicleType = delivery.Type,
            VehicleLatitude = delivery.LastLatitude,
            VehicleLongitude = delivery.LastLongitude,
            VehiclePositionAt = delivery.LastPositionAt,
            StopsBefore = stopsBefore
        };
    }
}

/// <summary>Used by the realtime hub to subscribe a tracking page to an order's updates.</summary>
public sealed record ResolveTrackingCodeQuery(string TrackingCode) : IQuery<Guid?>;

internal sealed class ResolveTrackingCodeQueryHandler(IApplicationDbContext db) : IQueryHandler<ResolveTrackingCodeQuery, Guid?>
{
    public async Task<Guid?> Handle(ResolveTrackingCodeQuery query, CancellationToken cancellationToken)
    {
        var code = (query.TrackingCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            return null;
        }

        return await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.TrackingCode == code)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
