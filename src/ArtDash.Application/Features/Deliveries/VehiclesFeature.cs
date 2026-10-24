using ArtDash.Shared.Contracts.Deliveries;

namespace ArtDash.Application.Features.Deliveries;

public sealed record GetVehiclesQuery(bool OnlyActive = false) : IQuery<IReadOnlyList<VehicleDto>>;

internal sealed class GetVehiclesQueryHandler(IApplicationDbContext db) : IQueryHandler<GetVehiclesQuery, IReadOnlyList<VehicleDto>>
{
    public async Task<IReadOnlyList<VehicleDto>> Handle(GetVehiclesQuery query, CancellationToken cancellationToken) =>
        await db.Vehicles.AsNoTracking()
            .WhereIf(query.OnlyActive, v => v.IsActive)
            .OrderBy(v => v.Plate)
            .Select(v => new VehicleDto(v.Id, v.Plate, v.Type, v.Model, v.CapacityKg, v.IsActive, v.Notes))
            .ToListAsync(cancellationToken);
}

public sealed class SaveVehicleRequestValidator : AbstractValidator<SaveVehicleRequest>
{
    public SaveVehicleRequestValidator()
    {
        RuleFor(x => x.Plate)
            .NotEmpty().WithName("Placa")
            .Must(p => Vehicle.NormalizePlate(p).Length == 7).WithMessage("Placa inválida (ex.: ABC1D23).");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Model).MaximumLength(100);
        RuleFor(x => x.CapacityKg).GreaterThan(0).When(x => x.CapacityKg.HasValue);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed record CreateVehicleCommand(SaveVehicleRequest Request) : ICommand<Guid>;

public sealed class CreateVehicleCommandValidator : AbstractValidator<CreateVehicleCommand>
{
    public CreateVehicleCommandValidator() => RuleFor(x => x.Request).SetValidator(new SaveVehicleRequestValidator());
}

internal sealed class CreateVehicleCommandHandler(IApplicationDbContext db) : ICommandHandler<CreateVehicleCommand, Guid>
{
    public async Task<Guid> Handle(CreateVehicleCommand command, CancellationToken cancellationToken)
    {
        await VehicleRules.EnsureUniquePlateAsync(db, command.Request.Plate, null, cancellationToken);
        var vehicle = new Vehicle();
        VehicleRules.Apply(vehicle, command.Request);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(cancellationToken);
        return vehicle.Id;
    }
}

public sealed record UpdateVehicleCommand(Guid Id, SaveVehicleRequest Request) : ICommand;

public sealed class UpdateVehicleCommandValidator : AbstractValidator<UpdateVehicleCommand>
{
    public UpdateVehicleCommandValidator() => RuleFor(x => x.Request).SetValidator(new SaveVehicleRequestValidator());
}

internal sealed class UpdateVehicleCommandHandler(IApplicationDbContext db) : ICommandHandler<UpdateVehicleCommand>
{
    public async Task<Unit> Handle(UpdateVehicleCommand command, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Veículo", command.Id);

        await VehicleRules.EnsureUniquePlateAsync(db, command.Request.Plate, command.Id, cancellationToken);
        VehicleRules.Apply(vehicle, command.Request);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record DeleteVehicleCommand(Guid Id) : ICommand;

internal sealed class DeleteVehicleCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<DeleteVehicleCommand>
{
    public async Task<Unit> Handle(DeleteVehicleCommand command, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Veículo", command.Id);

        var inUse = await db.Deliveries.AnyAsync(
            d => d.VehicleId == command.Id && (d.Status == DeliveryStatus.Planned || d.Status == DeliveryStatus.InProgress),
            cancellationToken);

        if (inUse)
        {
            throw new DomainException("O veículo está em uma entrega planejada ou em andamento.");
        }

        vehicle.IsActive = false;
        vehicle.MarkDeleted(clock.GetUtcNow().UtcDateTime, currentUser.AuditName());
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record GetDriversQuery(bool OnlyActive = false) : IQuery<IReadOnlyList<DriverDto>>;

internal sealed class GetDriversQueryHandler(IApplicationDbContext db) : IQueryHandler<GetDriversQuery, IReadOnlyList<DriverDto>>
{
    public async Task<IReadOnlyList<DriverDto>> Handle(GetDriversQuery query, CancellationToken cancellationToken)
    {
        var drivers = await db.Drivers.AsNoTracking()
            .WhereIf(query.OnlyActive, d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new
            {
                Driver = d,
                IsOnDelivery = db.Deliveries.Any(x => x.DriverId == d.Id && x.Status == DeliveryStatus.InProgress)
            })
            .ToListAsync(cancellationToken);

        return drivers
            .Select(x => new DriverDto(
                x.Driver.Id,
                x.Driver.UserId,
                x.Driver.Name,
                x.Driver.Phone,
                x.Driver.LicenseNumber,
                x.Driver.LicenseCategory,
                x.Driver.LicenseExpiresOn?.ToDateTime(TimeOnly.MinValue),
                x.Driver.IsActive,
                x.IsOnDelivery))
            .ToList();
    }
}

internal static class VehicleRules
{
    public static void Apply(Vehicle vehicle, SaveVehicleRequest request)
    {
        vehicle.Plate = Vehicle.NormalizePlate(request.Plate);
        vehicle.Type = request.Type;
        vehicle.Model = request.Model.NullIfWhiteSpace();
        vehicle.CapacityKg = request.CapacityKg;
        vehicle.IsActive = request.IsActive;
        vehicle.Notes = request.Notes.NullIfWhiteSpace();
    }

    public static async Task EnsureUniquePlateAsync(IApplicationDbContext db, string plate, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = Vehicle.NormalizePlate(plate);
        if (await db.Vehicles.AnyAsync(v => v.Plate == normalized && (exceptId == null || v.Id != exceptId), cancellationToken))
        {
            throw new RequestValidationException(nameof(SaveVehicleRequest.Plate), "Já existe um veículo com esta placa.");
        }
    }
}
