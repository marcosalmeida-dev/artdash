namespace ArtDash.Domain.Catalog;

/// <summary>
/// A sellable item. Catalog entities are plain records by design: their rules are input validation,
/// which lives in the application validators. Orders copy name and price when placed, so editing a
/// product never rewrites past orders.
/// </summary>
public sealed class Product : BaseSoftDeleteTenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Sku { get; set; }
    public decimal Price { get; set; }

    /// <summary>Optional "from" price shown struck through. Never charged.</summary>
    public decimal? CompareAtPrice { get; set; }

    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Unit;

    /// <summary>Weight of one unit, used to check vehicle capacity when planning deliveries.</summary>
    public decimal? WeightKg { get; set; }

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
}
