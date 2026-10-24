namespace ArtDash.Domain.Deliveries;

/// <summary>
/// A delivery run: one driver, one vehicle and an ordered list of stops (orders).
/// <code>Planned ──► InProgress ──► Completed      Planned ──► Cancelled</code>
/// The run keeps the last known position of the vehicle for the live map; the full trail is stored
/// separately in <see cref="VehiclePosition"/>. Order status changes are orchestrated by the
/// application layer — this aggregate never reaches into another aggregate.
/// </summary>
public sealed class Delivery : BaseTenantEntity
{
    private readonly List<DeliveryStop> _stops = [];

    private Delivery()
    {
    }

    public string Number { get; private set; } = string.Empty;
    public Guid DriverId { get; private set; }
    public Driver Driver { get; private set; } = null!;
    public Guid VehicleId { get; private set; }
    public Vehicle Vehicle { get; private set; } = null!;
    public DeliveryStatus Status { get; private set; }
    public DateTime ScheduledFor { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? Notes { get; private set; }
    public string? CancellationReason { get; private set; }

    public double? LastLatitude { get; private set; }
    public double? LastLongitude { get; private set; }
    public double? LastSpeedKmh { get; private set; }
    public double? LastHeading { get; private set; }
    public DateTime? LastPositionAt { get; private set; }

    public IReadOnlyCollection<DeliveryStop> Stops => _stops;

    public bool HasPendingStops => _stops.Any(s => s.Status == DeliveryStopStatus.Pending);

    public static Delivery Plan(
        string number,
        Driver driver,
        Vehicle vehicle,
        DateTime scheduledFor,
        IReadOnlyList<Guid> orderIds,
        string? notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(vehicle);

        if (!driver.IsActive)
        {
            throw new DomainException($"O motorista {driver.Name} está inativo.");
        }

        if (!vehicle.IsActive)
        {
            throw new DomainException($"O veículo {vehicle.Plate} está inativo.");
        }

        if (orderIds.Count == 0)
        {
            throw new DomainException("Selecione pelo menos um pedido para a entrega.");
        }

        if (orderIds.Distinct().Count() != orderIds.Count)
        {
            throw new DomainException("Há pedidos repetidos na entrega.");
        }

        var delivery = new Delivery
        {
            Number = number,
            DriverId = driver.Id,
            VehicleId = vehicle.Id,
            Status = DeliveryStatus.Planned,
            ScheduledFor = scheduledFor,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };

        var sequence = 1;
        foreach (var orderId in orderIds)
        {
            delivery._stops.Add(new DeliveryStop(delivery.Id, orderId, sequence++));
        }

        delivery.Raise(new DeliveryStatusChangedEvent(delivery));
        return delivery;
    }

    public void Start(DateTime now)
    {
        if (Status != DeliveryStatus.Planned)
        {
            throw new DomainException($"A entrega {Number} não está planejada.");
        }

        Status = DeliveryStatus.InProgress;
        StartedAt = now;
        Raise(new DeliveryStatusChangedEvent(this));
    }

    public void RecordPosition(double latitude, double longitude, double? speedKmh, double? heading, DateTime recordedAt)
    {
        if (Status != DeliveryStatus.InProgress)
        {
            throw new DomainException($"A entrega {Number} não está em andamento.");
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new DomainException("Coordenadas inválidas.");
        }

        // Positions can arrive out of order on a flaky connection; keep the newest as "last".
        if (LastPositionAt is not null && recordedAt < LastPositionAt)
        {
            return;
        }

        LastLatitude = latitude;
        LastLongitude = longitude;
        LastSpeedKmh = speedKmh is >= 0 ? speedKmh : null;
        LastHeading = heading is >= 0 and <= 360 ? heading : null;
        LastPositionAt = recordedAt;
    }

    public DeliveryStop CompleteStop(Guid stopId, DateTime now, string? recipientName, string? notes)
    {
        EnsureInProgress();
        var stop = FindStop(stopId);
        stop.Complete(now, recipientName, notes);
        return stop;
    }

    public DeliveryStop FailStop(Guid stopId, DateTime now, string reason)
    {
        EnsureInProgress();
        var stop = FindStop(stopId);
        stop.Fail(now, reason);
        return stop;
    }

    public void Finish(DateTime now)
    {
        EnsureInProgress();

        if (HasPendingStops)
        {
            throw new DomainException("Ainda há paradas pendentes nesta entrega.");
        }

        Status = DeliveryStatus.Completed;
        CompletedAt = now;
        Raise(new DeliveryStatusChangedEvent(this));
    }

    public void Cancel(string reason, DateTime now)
    {
        if (Status != DeliveryStatus.Planned)
        {
            throw new DomainException("Somente entregas planejadas (não iniciadas) podem ser canceladas.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Informe o motivo do cancelamento.");
        }

        Status = DeliveryStatus.Cancelled;
        CancellationReason = reason.Trim();
        CompletedAt = now;
        Raise(new DeliveryStatusChangedEvent(this));
    }

    private void EnsureInProgress()
    {
        if (Status != DeliveryStatus.InProgress)
        {
            throw new DomainException($"A entrega {Number} não está em andamento.");
        }
    }

    private DeliveryStop FindStop(Guid stopId) =>
        _stops.FirstOrDefault(s => s.Id == stopId)
        ?? throw new DomainException("Parada não encontrada nesta entrega.");
}

public sealed class DeliveryStop : BaseEntity, ITenantEntity
{
    private DeliveryStop()
    {
    }

    internal DeliveryStop(Guid deliveryId, Guid orderId, int sequence)
    {
        DeliveryId = deliveryId;
        OrderId = orderId;
        Sequence = sequence;
        Status = DeliveryStopStatus.Pending;
    }

    public Guid TenantId { get; set; }
    public Guid DeliveryId { get; private set; }
    public Guid OrderId { get; private set; }
    public int Sequence { get; private set; }
    public DeliveryStopStatus Status { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? RecipientName { get; private set; }
    public string? Notes { get; private set; }
    public string? FailureReason { get; private set; }

    internal void Complete(DateTime now, string? recipientName, string? notes)
    {
        EnsurePending();
        Status = DeliveryStopStatus.Delivered;
        CompletedAt = now;
        RecipientName = string.IsNullOrWhiteSpace(recipientName) ? null : recipientName.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    internal void Fail(DateTime now, string reason)
    {
        EnsurePending();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Informe o motivo da entrega não realizada.");
        }

        Status = DeliveryStopStatus.Failed;
        CompletedAt = now;
        FailureReason = reason.Trim();
    }

    private void EnsurePending()
    {
        if (Status != DeliveryStopStatus.Pending)
        {
            throw new DomainException("Esta parada já foi finalizada.");
        }
    }
}

/// <summary>One GPS fix sent by the driver's phone. Append-only.</summary>
public sealed class VehiclePosition : BaseEntity, ITenantEntity
{
    private VehiclePosition()
    {
    }

    public VehiclePosition(Guid deliveryId, Guid driverId, Guid vehicleId, double latitude, double longitude,
        double? accuracyMeters, double? speedKmh, double? heading, DateTime recordedAt)
    {
        DeliveryId = deliveryId;
        DriverId = driverId;
        VehicleId = vehicleId;
        Latitude = latitude;
        Longitude = longitude;
        AccuracyMeters = accuracyMeters;
        SpeedKmh = speedKmh;
        Heading = heading;
        RecordedAt = recordedAt;
    }

    public Guid TenantId { get; set; }
    public Guid DeliveryId { get; private set; }
    public Guid DriverId { get; private set; }
    public Guid VehicleId { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public double? AccuracyMeters { get; private set; }
    public double? SpeedKmh { get; private set; }
    public double? Heading { get; private set; }
    public DateTime RecordedAt { get; private set; }
}

public sealed record DeliveryStatusChangedEvent(Delivery Delivery) : DomainEvent;
