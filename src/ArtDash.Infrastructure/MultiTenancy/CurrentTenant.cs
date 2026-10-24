namespace ArtDash.Infrastructure.MultiTenancy;

/// <summary>Scoped holder of the tenant resolved for the current request (or set explicitly).</summary>
public sealed class CurrentTenant : ICurrentTenant, ICurrentTenantSetter
{
    public Guid? Id { get; private set; }
    public string? Slug { get; private set; }
    public bool IsResolved => Id.HasValue;

    public void Set(Guid tenantId, string slug)
    {
        Id = tenantId;
        Slug = slug;
    }
}

public sealed record TenantInfo(Guid Id, string Slug, string Name, bool IsActive);

/// <summary>Cached lookup of tenants by id, slug and host name.</summary>
public interface ITenantStore
{
    ValueTask<TenantInfo?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    ValueTask<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken);

    ValueTask<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken);

    void Invalidate();
}
