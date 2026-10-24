namespace ArtDash.Application.Common.Extensions;

public static class AddressMappings
{
    public static AddressDto ToDto(this Address address) => new()
    {
        ZipCode = address.ZipCode,
        Street = address.Street,
        Number = address.Number,
        Complement = address.Complement,
        District = address.District,
        City = address.City,
        State = address.State,
        Latitude = address.Latitude,
        Longitude = address.Longitude
    };

    public static Address ToDomain(this AddressDto dto) => new()
    {
        ZipCode = Address.NormalizeZipCode(dto.ZipCode),
        Street = dto.Street.Trim(),
        Number = dto.Number.Trim(),
        Complement = dto.Complement.NullIfWhiteSpace(),
        District = dto.District.Trim(),
        City = dto.City.Trim(),
        State = dto.State.Trim().ToUpperInvariant(),
        Latitude = dto.Latitude,
        Longitude = dto.Longitude
    };
}
