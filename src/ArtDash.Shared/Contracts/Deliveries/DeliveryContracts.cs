using ArtDash.Shared.Contracts.Common;
using ArtDash.Shared.Enums;

namespace ArtDash.Shared.Contracts.Deliveries;

public sealed record VehicleDto(
    Guid Id,
    string Plate,
    VehicleType Type,
    string? Model,
    decimal? CapacityKg,
    bool IsActive,
    string? Notes);

public sealed class SaveVehicleRequest
{
    public string Plate { get; set; } = string.Empty;
    public VehicleType Type { get; set; } = VehicleType.Car;
    public string? Model { get; set; }
    public decimal? CapacityKg { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public static SaveVehicleRequest From(VehicleDto dto) => new()
    {
        Plate = dto.Plate,
        Type = dto.Type,
        Model = dto.Model,
        CapacityKg = dto.CapacityKg,
        IsActive = dto.IsActive,
        Notes = dto.Notes
    };
}

public sealed record DriverDto(
    Guid Id,
    Guid UserId,
    string Name,
    string? Phone,
    string? LicenseNumber,
    string? LicenseCategory,
    DateTime? LicenseExpiresOn,
    bool IsActive,
    bool IsOnDelivery);

public sealed class CreateDeliveryRequest
{
    public Guid? DriverId { get; set; }
    public Guid? VehicleId { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public List<Guid> OrderIds { get; set; } = [];
    public string? Notes { get; set; }
}

public sealed class CancelDeliveryRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class DeliveryQuery
{
    public DeliveryStatus? Status { get; set; }
    public Guid? DriverId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record DeliveryListItemDto
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public DeliveryStatus Status { get; init; }
    public string DriverName { get; init; } = string.Empty;
    public string VehiclePlate { get; init; } = string.Empty;
    public VehicleType VehicleType { get; init; }
    public DateTime ScheduledFor { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int StopsTotal { get; init; }
    public int StopsDelivered { get; init; }
    public int StopsFailed { get; init; }
    public DateTime? LastPositionAt { get; init; }
}

public sealed record DeliveryDto
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public DeliveryStatus Status { get; init; }
    public Guid DriverId { get; init; }
    public string DriverName { get; init; } = string.Empty;
    public string? DriverPhone { get; init; }
    public Guid VehicleId { get; init; }
    public string VehiclePlate { get; init; } = string.Empty;
    public VehicleType VehicleType { get; init; }
    public string? VehicleModel { get; init; }
    public DateTime ScheduledFor { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Notes { get; init; }
    public string? CancellationReason { get; init; }
    public VehiclePositionDto? LastPosition { get; init; }
    public IReadOnlyList<DeliveryStopDto> Stops { get; init; } = [];
}

public sealed record DeliveryStopDto
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public int Sequence { get; init; }
    public DeliveryStopStatus Status { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? CustomerPhone { get; init; }
    public AddressDto Address { get; init; } = new();
    public decimal Total { get; init; }

    /// <summary>Amount the driver must collect (on-delivery orders not yet paid).</summary>
    public decimal AmountToCollect { get; init; }

    public PaymentTiming PaymentTiming { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public PaymentMethod? PaymentMethod { get; init; }
    public string? OrderNotes { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? RecipientName { get; init; }
    public string? Notes { get; init; }
    public string? FailureReason { get; init; }
}

public sealed record VehiclePositionDto(
    Guid DeliveryId,
    string DeliveryNumber,
    double Latitude,
    double Longitude,
    double? SpeedKmh,
    double? Heading,
    DateTime RecordedAt,
    string? DriverName,
    string? VehiclePlate,
    VehicleType VehicleType);

public sealed record TrailPointDto(double Latitude, double Longitude, DateTime RecordedAt);

public sealed class RecordPositionRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public double? SpeedKmh { get; set; }
    public double? Heading { get; set; }
    public DateTime? RecordedAt { get; set; }
}

public sealed class CompleteStopRequest
{
    public string? RecipientName { get; set; }
    public string? Notes { get; set; }

    /// <summary>Filled when the driver collected the payment at the door.</summary>
    public PaymentMethod? CollectedMethod { get; set; }

    public decimal? CollectedAmount { get; set; }
}

public sealed class FailStopRequest
{
    public string Reason { get; set; } = string.Empty;
}
