using System.Text.Json.Serialization;
using ArtDash.Shared.Enums;

namespace ArtDash.Shared.Contracts.Catalog;

public sealed record CategoryDto(Guid Id, string Name, string? Description, int SortOrder, bool IsActive, int ProductCount);

public sealed class SaveCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record ProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Sku { get; init; }
    public decimal Price { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public UnitOfMeasure Unit { get; init; }
    public decimal? WeightKg { get; init; }
    public Guid CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public bool IsActive { get; init; }
    public bool IsFeatured { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class SaveProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Sku { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Unit;
    public decimal? WeightKg { get; set; }
    public Guid? CategoryId { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }

    public static SaveProductRequest From(ProductDto dto) => new()
    {
        Name = dto.Name,
        Description = dto.Description,
        Sku = dto.Sku,
        Price = dto.Price,
        CompareAtPrice = dto.CompareAtPrice,
        Unit = dto.Unit,
        WeightKg = dto.WeightKg,
        CategoryId = dto.CategoryId,
        ImageUrl = dto.ImageUrl,
        IsActive = dto.IsActive,
        IsFeatured = dto.IsFeatured,
        SortOrder = dto.SortOrder
    };
}

public sealed class ProductQuery
{
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record StorefrontCatalogDto(IReadOnlyList<StorefrontCategoryDto> Categories)
{
    [JsonIgnore]
    public IEnumerable<StorefrontProductDto> AllProducts => Categories.SelectMany(c => c.Products);
}

public sealed record StorefrontCategoryDto(Guid Id, string Name, IReadOnlyList<StorefrontProductDto> Products);

public sealed record StorefrontProductDto(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    decimal? CompareAtPrice,
    UnitOfMeasure Unit,
    string? ImageUrl,
    bool IsFeatured);
