using ArtDash.Shared.Contracts.Deliveries;

namespace ArtDash.Application.Features.Deliveries;

// The driver's phone (PWA) works through these use cases. Staff may run them as well (e.g. to close a
// stop on behalf of a driver whose phone died); DeliveryAccess decides who may touch which run.

public sealed record GetMyDeliveriesQuery : IQuery<IReadOnlyList<DeliveryListItemDto>>;

internal sealed class GetMyDeliveriesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IQueryHandler<GetMyDeliveriesQuery, IReadOnlyList<DeliveryListItemDto>>
{
    public async Task<IReadOnlyList<DeliveryListItemDto>> Handle(GetMyDeliveriesQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId();
        var driver = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId, cancellationToken)
            ?? throw new ForbiddenAccessException("Seu usuário não tem cadastro de motorista.");

        var since = clock.GetUtcNow().UtcDateTime.AddDays(-2);

        return await db.Deliveries.AsNoTracking()
            .Where(d => d.DriverId == driver.Id &&
                        (d.Status == DeliveryStatus.Planned || d.Status == DeliveryStatus.InProgress ||
                         (d.Status == DeliveryStatus.Completed && d.CompletedAt >= since)))
            .OrderBy(d => d.Status == DeliveryStatus.InProgress ? 0 : d.Status == DeliveryStatus.Planned ? 1 : 2)
            .ThenBy(d => d.ScheduledFor)
            .Select(DeliveryProjections.ToListItem)
            .ToListAsync(cancellationToken);
    }
}

public sealed record GetMyDeliveryQuery(Guid Id) : IQuery<DeliveryDto>;

internal sealed class GetMyDeliveryQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyDeliveryQuery, DeliveryDto>
{
    public async Task<DeliveryDto> Handle(GetMyDeliveryQuery query, CancellationToken cancellationToken)
    {
        var driver = await DeliveryAccess.GetCurrentDriverAsync(db, currentUser, cancellationToken);
        return await DeliveryDetailsLoader.LoadAsync(db, query.Id, driver?.Id, cancellationToken)
               ?? throw new NotFoundException("Entrega", query.Id);
    }
}

public sealed record StartDeliveryCommand(Guid Id) : ICommand;

internal sealed class StartDeliveryCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<StartDeliveryCommand>
{
    public async Task<Unit> Handle(StartDeliveryCommand command, CancellationToken cancellationToken)
    {
        var delivery = await DeliveryAccess.LoadForActionAsync(db, currentUser, command.Id, cancellationToken);

        var driverBusy = await db.Deliveries.AnyAsync(
            d => d.DriverId == delivery.DriverId && d.Status == DeliveryStatus.InProgress && d.Id != delivery.Id,
            cancellationToken);

        if (driverBusy)
        {
            throw new DomainException("O motorista já tem outra entrega em andamento.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var by = currentUser.AuditName();
        delivery.Start(now);

        var orderIds = delivery.Stops.Select(s => s.OrderId).ToList();
        var orders = await db.Orders.Where(o => orderIds.Contains(o.Id)).ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            order.MarkOutForDelivery(delivery.Id, now, by);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record RecordPositionCommand(Guid DeliveryId, RecordPositionRequest Request) : ICommand;

public sealed class RecordPositionCommandValidator : AbstractValidator<RecordPositionCommand>
{
    public RecordPositionCommandValidator()
    {
        RuleFor(x => x.Request.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Request.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Request.AccuracyMeters).GreaterThanOrEqualTo(0).When(x => x.Request.AccuracyMeters.HasValue);
    }
}

internal sealed class RecordPositionCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IRealtimeNotifier notifier,
    TimeProvider clock)
    : ICommandHandler<RecordPositionCommand>
{
    public async Task<Unit> Handle(RecordPositionCommand command, CancellationToken cancellationToken)
    {
        var delivery = await DeliveryAccess.LoadForActionAsync(db, currentUser, command.DeliveryId, cancellationToken);
        var request = command.Request;
        var now = clock.GetUtcNow().UtcDateTime;

        // Trust the phone's clock only within reason.
        var recordedAt = request.RecordedAt is { } at && at <= now.AddMinutes(1) && at >= now.AddHours(-6)
            ? DateTime.SpecifyKind(at, DateTimeKind.Utc)
            : now;

        delivery.RecordPosition(request.Latitude, request.Longitude, request.SpeedKmh, request.Heading, recordedAt);
        db.VehiclePositions.Add(new VehiclePosition(
            delivery.Id, delivery.DriverId, delivery.VehicleId,
            request.Latitude, request.Longitude, request.AccuracyMeters, request.SpeedKmh, request.Heading, recordedAt));

        await db.SaveChangesAsync(cancellationToken);

        var position = new VehiclePositionDto(
            delivery.Id, delivery.Number, request.Latitude, request.Longitude, request.SpeedKmh, request.Heading,
            recordedAt, delivery.Driver.Name, delivery.Vehicle.Plate, delivery.Vehicle.Type);

        var trackedOrders = delivery.Stops
            .Where(s => s.Status == DeliveryStopStatus.Pending)
            .Select(s => s.OrderId)
            .ToList();

        await notifier.PositionUpdatedAsync(currentTenant.RequiredId(), position, trackedOrders, cancellationToken);
        return Unit.Value;
    }
}

public sealed record CompleteStopCommand(Guid DeliveryId, Guid StopId, CompleteStopRequest Request) : ICommand;

public sealed class CompleteStopCommandValidator : AbstractValidator<CompleteStopCommand>
{
    public CompleteStopCommandValidator()
    {
        RuleFor(x => x.Request.RecipientName).MaximumLength(150);
        RuleFor(x => x.Request.Notes).MaximumLength(500);
        RuleFor(x => x.Request.CollectedAmount).GreaterThan(0).When(x => x.Request.CollectedAmount.HasValue);
        RuleFor(x => x.Request.CollectedMethod).NotNull()
            .When(x => x.Request.CollectedAmount is > 0)
            .WithMessage("Informe a forma de pagamento recebida.");
    }
}

internal sealed class CompleteStopCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<CompleteStopCommand>
{
    public async Task<Unit> Handle(CompleteStopCommand command, CancellationToken cancellationToken)
    {
        var delivery = await DeliveryAccess.LoadForActionAsync(db, currentUser, command.DeliveryId, cancellationToken);
        var request = command.Request;
        var now = clock.GetUtcNow().UtcDateTime;
        var by = currentUser.AuditName();

        var stop = delivery.CompleteStop(command.StopId, now, request.RecipientName, request.Notes);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == stop.OrderId, cancellationToken)
            ?? throw new NotFoundException("Pedido", stop.OrderId);

        if (request.CollectedAmount is > 0 && request.CollectedMethod is { } method && order.OutstandingAmount > 0)
        {
            var amount = Math.Min(request.CollectedAmount.Value, order.OutstandingAmount);
            order.RegisterPayment(method, amount, now, $"Recebido na entrega {delivery.Number}", by);
        }

        order.MarkDelivered(now, by, request.RecipientName is null ? null : $"Recebido por {request.RecipientName.Trim()}");

        if (!delivery.HasPendingStops)
        {
            delivery.Finish(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record FailStopCommand(Guid DeliveryId, Guid StopId, string Reason) : ICommand;

public sealed class FailStopCommandValidator : AbstractValidator<FailStopCommand>
{
    public FailStopCommandValidator() => RuleFor(x => x.Reason).NotEmpty().WithName("Motivo").MaximumLength(500);
}

internal sealed class FailStopCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<FailStopCommand>
{
    public async Task<Unit> Handle(FailStopCommand command, CancellationToken cancellationToken)
    {
        var delivery = await DeliveryAccess.LoadForActionAsync(db, currentUser, command.DeliveryId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        var stop = delivery.FailStop(command.StopId, now, command.Reason);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == stop.OrderId, cancellationToken)
            ?? throw new NotFoundException("Pedido", stop.OrderId);

        order.ReturnFromFailedDelivery(now, currentUser.AuditName(), command.Reason.Trim());

        if (!delivery.HasPendingStops)
        {
            delivery.Finish(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record FinishDeliveryCommand(Guid Id) : ICommand;

internal sealed class FinishDeliveryCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<FinishDeliveryCommand>
{
    public async Task<Unit> Handle(FinishDeliveryCommand command, CancellationToken cancellationToken)
    {
        var delivery = await DeliveryAccess.LoadForActionAsync(db, currentUser, command.Id, cancellationToken);
        delivery.Finish(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
