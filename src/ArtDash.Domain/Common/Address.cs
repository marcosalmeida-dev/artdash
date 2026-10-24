namespace ArtDash.Domain.Common;

/// <summary>
/// Brazilian postal address (owned value). Mutable on purpose so EF Core can map it as an owned type
/// without a constructor dance; aggregates always store a <see cref="Clone"/> so a caller cannot change
/// an order's address after the fact through a shared instance.
/// </summary>
public sealed class Address
{
    public string ZipCode { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string? Complement { get; set; }
    public string District { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;

    public Address Clone() => (Address)MemberwiseClone();

    public string ToSingleLine()
    {
        var street = string.Join(", ", new[] { Street, Number }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var parts = new[]
        {
            string.IsNullOrWhiteSpace(Complement) ? street : $"{street} - {Complement}",
            District,
            string.Join("/", new[] { City, State }.Where(s => !string.IsNullOrWhiteSpace(s)))
        };

        return string.Join(" • ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>Keeps only the digits of a CEP ("88010-000" becomes "88010000").</summary>
    public static string NormalizeZipCode(string? zipCode) =>
        new((zipCode ?? string.Empty).Where(char.IsDigit).ToArray());
}
