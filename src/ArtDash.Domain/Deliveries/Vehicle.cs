namespace ArtDash.Domain.Deliveries;

/// <summary>A car, motorcycle, van or truck used for deliveries.</summary>
public sealed class Vehicle : BaseSoftDeleteTenantEntity
{
    public string Plate { get; set; } = string.Empty;
    public VehicleType Type { get; set; } = VehicleType.Car;
    public string? Model { get; set; }
    public decimal? CapacityKg { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    /// <summary>"abc-1d23" becomes "ABC1D23" (old and Mercosul formats).</summary>
    public static string NormalizePlate(string plate) =>
        new((plate ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

/// <summary>
/// The driver profile of a user with the Driver role: license data used by the back office.
/// </summary>
public sealed class Driver : BaseSoftDeleteTenantEntity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }

    /// <summary>CNH number.</summary>
    public string? LicenseNumber { get; set; }

    /// <summary>CNH category (A, B, C, D, E, AB...).</summary>
    public string? LicenseCategory { get; set; }

    public DateOnly? LicenseExpiresOn { get; set; }
    public bool IsActive { get; set; } = true;
}
