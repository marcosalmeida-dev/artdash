using System.Text.Json.Serialization;

namespace ArtDash.Shared.Contracts.Common;

/// <summary>A page of results plus the total, for server-side paging.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Empty(int page = 1, int pageSize = 20) => new([], 0, page, pageSize);
}

/// <summary>Id + label pair for selects and autocompletes.</summary>
public sealed record LookupDto(Guid Id, string Name, string? Description = null);

/// <summary>Result of a file upload.</summary>
public sealed record UploadResultDto(string Url);

/// <summary>
/// Brazilian postal address. A class (not a record) because Blazor forms bind to it two-way.
/// Coordinates are optional; when present the address shows up on the delivery maps.
/// </summary>
public sealed class AddressDto
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

    [JsonIgnore]
    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Street) && string.IsNullOrWhiteSpace(City);

    public string ToSingleLine()
    {
        var parts = new List<string>();

        var streetLine = string.Join(", ", new[] { Street, Number }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(streetLine))
        {
            parts.Add(string.IsNullOrWhiteSpace(Complement) ? streetLine : $"{streetLine} - {Complement}");
        }

        if (!string.IsNullOrWhiteSpace(District))
        {
            parts.Add(District);
        }

        var cityLine = string.Join("/", new[] { City, State }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(cityLine))
        {
            parts.Add(cityLine);
        }

        if (!string.IsNullOrWhiteSpace(ZipCode))
        {
            parts.Add($"CEP {ZipCode}");
        }

        return string.Join(" • ", parts);
    }

    public AddressDto Clone() => (AddressDto)MemberwiseClone();
}
