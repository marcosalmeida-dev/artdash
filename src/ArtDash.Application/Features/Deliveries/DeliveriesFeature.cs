using ArtDash.Shared.Contracts.Deliveries;

namespace ArtDash.Application.Features.Deliveries;

// ---------------------------------------------------------------- Queries (staff)

public sealed record SearchDeliveriesQuery(DeliveryQuery Filter) : IQuery<PagedResult<DeliveryListItemDto>>;

internal sealed class SearchDeliveriesQueryHandler(IApplicationDbContext db, ICurrentTenant currentTenant)
    : IQueryHandler<SearchDeliveriesQuery, PagedResult<DeliveryListItemDto>>
{
    public async Task<PagedResult<DeliveryListItemDto>> Handle(SearchDeliveriesQuery query, CancellationToken cancellationToken)
    {
        var filter = query.Filter;
        var zone = await TenantTime.GetZoneAsync(db, currentTenant, cancellationToken);
        DateTime? fromUtc = filter.From is { } from ? TenantTime.StartOfDayUtc(from, zone) : null;
        DateTime? toUtc = filter.To is { } to ? TenantTime.EndOfDayUtc(to, zone) : null;

        return await db.Deliveries.AsNoTracking()
            .WhereIf(filter.Status is not null, d => d.Status == filter.Status)
            .WhereIf(filter.DriverId is not null, d => d.DriverId == filter.DriverId)
            .WhereIf(fromUtc is not null, d => d.ScheduledFor >= fromUtc)
            .WhereIf(toUtc is not null, d => d.ScheduledFor < toUtc)
            .OrderByDescending(d => d.ScheduledFor)
            .Select(DeliveryProjections.ToListItem)
            .ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }
}

public sealed record GetDeliveryQuery(Guid Id) : IQuery<DeliveryDto>;

internal sealed class GetDeliveryQueryHandler(IApplicationDbContext db) : IQueryHandler<GetDeliveryQuery, DeliveryDto>
{
    public async Task<DeliveryDto> Handle(GetDeliveryQuery query, CancellationToken cancellationToken) =>
        await DeliveryDetailsLoader.LoadAsync(db, query.Id, driverId: null, cancellationToken)
        ?? throw new NotFoundException("Entrega", query.Id);
}

/// <summary>Last known position of every vehicle on the road (live map).</summary>
public sealed record GetActivePositionsQuery : IQuery<IReadOnlyList<VehiclePositionDto>>;

internal sealed class GetActivePositionsQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetActivePositionsQuery, IReadOnlyList<VehiclePositionDto>>
{
    public async Task<IReadOnlyList<VehiclePositionDto>> Handle(GetActivePositionsQuery query, CancellationToken cancellationToken) =>
        await db.Deliveries.AsNoTracking()
            .Where(d => d.Status == DeliveryStatus.InProgress && d.LastLatitude != null && d.LastLongitude != null)
            .Select(d => new VehiclePositionDto(
                d.Id,
                d.Number,
                d.LastLatitude!.Value,
                d.LastLongitude!.Value,
                d.LastSpeedKmh,
                d.LastHeading,
                d.LastPositionAt ?? d.StartedAt ?? d.ScheduledFor,
                d.Driver.Name,
                d.Vehicle.Plate,
                d.Vehicle.Type))
            .ToListAsync(cancellationToken);
}

/// <summary>The path driven so far, for drawing on the map.</summary>
public sealed record GetDeliveryTrailQuery(Guid Id) : IQuery<IReadOnlyList<TrailPointDto>>;

internal sealed class GetDeliveryTrailQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetDeliveryTrailQuery, IReadOnlyList<TrailPointDto>>
{
    private const int MaxPoints = 2000;

    public async Task<IReadOnlyList<TrailPointDto>> Handle(GetDeliveryTrailQuery query, CancellationToken cancellationToken)
    {
        await DeliveryAccess.EnsureCanSeeAsync(db, currentUser, query.Id, cancellationToken);

        var points = await db.VehiclePositions.AsNoTracking()
            .Where(p => p.DeliveryId == query.Id)
            .OrderByDescending(p => p.RecordedAt)
            .Take(MaxPoints)
            .Select(p => new TrailPointDto(p.Latitude, p.Longitude, p.RecordedAt))
            .ToListAsync(cancellationToken);

        points.Reverse();
        return points;
    }
}

// ---------------------------------------------------------------- Commands (staff)

public sealed record CreateDeliveryCommand(CreateDeliveryRequest Request) : ICommand<Guid>;

public sealed class CreateDeliveryCommandValidator : AbstractValidator<CreateDeliveryCommand>
{
    public CreateDeliveryCommandValidator()
    {
        RuleFor(x => x.Request.DriverId).NotEmpty().WithMessage("Selecione o motorista.");
        RuleFor(x => x.Request.VehicleId).NotEmpty().WithMessage("Selecione o veículo.");
        RuleFor(x => x.Request.OrderIds).NotEmpty().WithMessage("Selecione pelo menos um pedido.");
        RuleFor(x => x.Request.Notes).MaximumLength(500);
    }
}

internal sealed class CreateDeliveryCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant currentTenant,
    ISequenceGenerator sequences,
    TimeProvider clock)
    : ICommandHandler<CreateDeliveryCommand, Guid>
{
    public async Task<Guid> Handle(CreateDeliveryCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var driver = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DriverId, cancellationToken)
            ?? throw new NotFoundException("Motorista", request.DriverId ?? Guid.Empty);
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken)
            ?? throw new NotFoundException("Veículo", request.VehicleId ?? Guid.Empty);

        var orderIds = request.OrderIds.Distinct().ToList();
        var orderCount = await db.Orders.CountAsync(o => orderIds.Contains(o.Id), cancellationToken);
        if (orderCount != orderIds.Count)
        {
            throw new DomainException("Um ou mais pedidos não foram encontrados.");
        }

        await EnsureCapacityAsync(vehicle, orderIds, cancellationToken);

        var zone = await TenantTime.GetZoneAsync(db, currentTenant, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var scheduledFor = request.ScheduledFor is { } local
            ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone)
            : now;

        // The sequence saves immediately, so it runs before any entity is tracked as changed.
        var number = await sequences.NextAsync(SequenceNames.Delivery, cancellationToken);

        var orders = await db.Orders.Where(o => orderIds.Contains(o.Id)).ToListAsync(cancellationToken);
        var delivery = Delivery.Plan($"E{number:D5}", driver, vehicle, scheduledFor, orderIds, request.Notes);

        foreach (var order in orders)
        {
            order.AssignToDelivery(delivery.Id);
        }

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(cancellationToken);
        return delivery.Id;
    }

    private async Task EnsureCapacityAsync(Vehicle vehicle, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        if (vehicle.CapacityKg is not > 0)
        {
            return;
        }

        var lines = await db.OrderItems.AsNoTracking()
            .Where(i => orderIds.Contains(i.OrderId))
            .Select(i => new { i.ProductId, i.Quantity })
            .ToListAsync(cancellationToken);

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var weights = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.WeightKg != null)
            .ToDictionaryAsync(p => p.Id, p => p.WeightKg!.Value, cancellationToken);

        var total = lines.Sum(l => weights.TryGetValue(l.ProductId, out var w) ? w * l.Quantity : 0m);
        if (total > vehicle.CapacityKg.Value)
        {
            throw new DomainException(
                $"A carga ({total:N1} kg) excede a capacidade do veículo {vehicle.Plate} ({vehicle.CapacityKg.Value:N1} kg).");
        }
    }
}

public sealed record CancelDeliveryCommand(Guid Id, string Reason) : ICommand;

public sealed class CancelDeliveryCommandValidator : AbstractValidator<CancelDeliveryCommand>
{
    public CancelDeliveryCommandValidator() => RuleFor(x => x.Reason).NotEmpty().WithName("Motivo").MaximumLength(500);
}

internal sealed class CancelDeliveryCommandHandler(IApplicationDbContext db, TimeProvider clock) : ICommandHandler<CancelDeliveryCommand>
{
    public async Task<Unit> Handle(CancelDeliveryCommand command, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.Include(d => d.Stops)
            .FirstOrDefaultAsync(d => d.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Entrega", command.Id);

        delivery.Cancel(command.Reason, clock.GetUtcNow().UtcDateTime);

        var orderIds = delivery.Stops.Select(s => s.OrderId).ToList();
        var orders = await db.Orders.Where(o => orderIds.Contains(o.Id) && o.DeliveryId == delivery.Id).ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            order.UnassignFromDelivery();
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------- Shared helpers

internal static class DeliveryProjections
{
    public static readonly System.Linq.Expressions.Expression<Func<Delivery, DeliveryListItemDto>> ToListItem = d => new DeliveryListItemDto
    {
        Id = d.Id,
        Number = d.Number,
        Status = d.Status,
        DriverName = d.Driver.Name,
        VehiclePlate = d.Vehicle.Plate,
        VehicleType = d.Vehicle.Type,
        ScheduledFor = d.ScheduledFor,
        StartedAt = d.StartedAt,
        CompletedAt = d.CompletedAt,
        StopsTotal = d.Stops.Count(),
        StopsDelivered = d.Stops.Count(s => s.Status == DeliveryStopStatus.Delivered),
        StopsFailed = d.Stops.Count(s => s.Status == DeliveryStopStatus.Failed),
        LastPositionAt = d.LastPositionAt
    };
}

internal static class DeliveryDetailsLoader
{
    public static async Task<DeliveryDto?> LoadAsync(IApplicationDbContext db, Guid id, Guid? driverId, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.AsNoTracking()
            .Include(d => d.Driver)
            .Include(d => d.Vehicle)
            .Include(d => d.Stops)
            .FirstOrDefaultAsync(d => d.Id == id && (driverId == null || d.DriverId == driverId), cancellationToken);

        if (delivery is null)
        {
            return null;
        }

        var orderIds = delivery.Stops.Select(s => s.OrderId).ToList();
        var orders = await db.Orders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var stops = delivery.Stops
            .OrderBy(s => s.Sequence)
            .Select(s =>
            {
                orders.TryGetValue(s.OrderId, out var order);
                var toCollect = order is { PaymentTiming: PaymentTiming.OnDelivery } && s.Status == DeliveryStopStatus.Pending
                    ? order.OutstandingAmount
                    : 0m;

                return new DeliveryStopDto
                {
                    Id = s.Id,
                    OrderId = s.OrderId,
                    OrderNumber = order?.Number ?? "?",
                    Sequence = s.Sequence,
                    Status = s.Status,
                    CustomerName = order?.CustomerName ?? string.Empty,
                    CustomerPhone = order?.CustomerPhone,
                    Address = order?.DeliveryAddress.ToDto() ?? new AddressDto(),
                    Total = order?.Total ?? 0m,
                    AmountToCollect = toCollect,
                    PaymentTiming = order?.PaymentTiming ?? PaymentTiming.Prepaid,
                    PaymentStatus = order?.PaymentStatus ?? PaymentStatus.Pending,
                    PaymentMethod = order?.PaymentMethod,
                    OrderNotes = order?.Notes,
                    CompletedAt = s.CompletedAt,
                    RecipientName = s.RecipientName,
                    Notes = s.Notes,
                    FailureReason = s.FailureReason
                };
            })
            .ToList();

        return new DeliveryDto
        {
            Id = delivery.Id,
            Number = delivery.Number,
            Status = delivery.Status,
            DriverId = delivery.DriverId,
            DriverName = delivery.Driver.Name,
            DriverPhone = delivery.Driver.Phone,
            VehicleId = delivery.VehicleId,
            VehiclePlate = delivery.Vehicle.Plate,
            VehicleType = delivery.Vehicle.Type,
            VehicleModel = delivery.Vehicle.Model,
            ScheduledFor = delivery.ScheduledFor,
            StartedAt = delivery.StartedAt,
            CompletedAt = delivery.CompletedAt,
            Notes = delivery.Notes,
            CancellationReason = delivery.CancellationReason,
            LastPosition = delivery.LastLatitude is { } lat && delivery.LastLongitude is { } lng
                ? new VehiclePositionDto(delivery.Id, delivery.Number, lat, lng, delivery.LastSpeedKmh, delivery.LastHeading,
                    delivery.LastPositionAt ?? delivery.ScheduledFor, delivery.Driver.Name, delivery.Vehicle.Plate, delivery.Vehicle.Type)
                : null,
            Stops = stops
        };
    }
}

internal static class DeliveryAccess
{
    /// <summary>The driver profile of the signed-in user, or null for staff.</summary>
    public static async Task<Driver?> GetCurrentDriverAsync(IApplicationDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (currentUser.IsStaff())
        {
            return null;
        }

        var userId = currentUser.RequiredUserId();
        return await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId, cancellationToken)
            ?? throw new ForbiddenAccessException("Seu usuário não tem cadastro de motorista.");
    }

    public static async Task EnsureCanSeeAsync(IApplicationDbContext db, ICurrentUser currentUser, Guid deliveryId, CancellationToken cancellationToken)
    {
        var driver = await GetCurrentDriverAsync(db, currentUser, cancellationToken);
        if (driver is null)
        {
            return;
        }

        if (!await db.Deliveries.AnyAsync(d => d.Id == deliveryId && d.DriverId == driver.Id, cancellationToken))
        {
            throw new NotFoundException("Entrega", deliveryId);
        }
    }

    /// <summary>Loads a delivery (tracked, with stops, driver and vehicle) the current user may act on.</summary>
    public static async Task<Delivery> LoadForActionAsync(IApplicationDbContext db, ICurrentUser currentUser, Guid deliveryId, CancellationToken cancellationToken)
    {
        var driver = await GetCurrentDriverAsync(db, currentUser, cancellationToken);
        var driverId = driver?.Id;

        return await db.Deliveries
                   .Include(d => d.Stops)
                   .Include(d => d.Driver)
                   .Include(d => d.Vehicle)
                   .FirstOrDefaultAsync(d => d.Id == deliveryId && (driverId == null || d.DriverId == driverId), cancellationToken)
               ?? throw new NotFoundException("Entrega", deliveryId);
    }
}
