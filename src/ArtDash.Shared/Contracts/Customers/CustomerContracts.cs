using ArtDash.Shared.Contracts.Common;
using ArtDash.Shared.Enums;

namespace ArtDash.Shared.Contracts.Customers;

public sealed record CustomerListItemDto(
    Guid Id,
    string Name,
    CustomerType Type,
    string? Document,
    string? Email,
    string? Phone,
    string City,
    bool AllowInvoicing,
    bool IsActive,
    bool HasLogin);

public sealed record CustomerDto
{
    public Guid Id { get; init; }
    public CustomerType Type { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? TradeName { get; init; }
    public string? Document { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public AddressDto Address { get; init; } = new();
    public bool AllowInvoicing { get; init; }
    public int PaymentTermDays { get; init; }
    public decimal? CreditLimit { get; init; }
    public string? Notes { get; init; }
    public bool IsActive { get; init; }
    public Guid? UserId { get; init; }

    /// <summary>What the customer still owes on invoiced orders.</summary>
    public decimal OpenBalance { get; init; }
}

public sealed class SaveCustomerRequest
{
    public CustomerType Type { get; set; } = CustomerType.Individual;
    public string Name { get; set; } = string.Empty;
    public string? TradeName { get; set; }
    public string? Document { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public AddressDto Address { get; set; } = new();
    public bool AllowInvoicing { get; set; }
    public int PaymentTermDays { get; set; }
    public decimal? CreditLimit { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public static SaveCustomerRequest From(CustomerDto dto) => new()
    {
        Type = dto.Type,
        Name = dto.Name,
        TradeName = dto.TradeName,
        Document = dto.Document,
        Email = dto.Email,
        Phone = dto.Phone,
        Address = dto.Address.Clone(),
        AllowInvoicing = dto.AllowInvoicing,
        PaymentTermDays = dto.PaymentTermDays,
        CreditLimit = dto.CreditLimit,
        Notes = dto.Notes,
        IsActive = dto.IsActive
    };
}

public sealed class CustomerQuery
{
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>The signed-in customer's own profile (storefront).</summary>
public sealed record MyProfileDto
{
    public Guid CustomerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Document { get; init; }
    public AddressDto Address { get; init; } = new();
    public bool AllowInvoicing { get; init; }
    public int PaymentTermDays { get; init; }
}

public sealed class UpdateMyProfileRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Document { get; set; }
    public AddressDto Address { get; set; } = new();
}
