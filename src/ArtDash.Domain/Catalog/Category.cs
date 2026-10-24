namespace ArtDash.Domain.Catalog;

/// <summary>Product grouping shown as sections on the storefront.</summary>
public sealed class Category : BaseSoftDeleteTenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
