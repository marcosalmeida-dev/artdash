using ArtDash.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;

namespace ArtDash.Infrastructure.MultiTenancy;

/// <summary>
/// Keeps the (small) tenant and domain tables in memory for five minutes so resolving the tenant
/// costs nothing per request. Tenants and domains are not tenant-filtered, so any scope can read them.
/// </summary>
internal sealed class TenantStore(IServiceScopeFactory scopeFactory, IMemoryCache cache) : ITenantStore
{
    private const string CacheKey = "artdash:tenant-snapshot";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async ValueTask<TenantInfo?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot.ById.GetValueOrDefault(id);
    }

    public async ValueTask<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot.BySlug.GetValueOrDefault(slug.Trim().ToLowerInvariant());
    }

    public async ValueTask<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot.ByHost.GetValueOrDefault(TenantDomain.NormalizeHost(host));
    }

    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<Snapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out Snapshot? cached) && cached is not null)
        {
            return cached;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenants = await db.Tenants.AsNoTracking()
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.IsActive))
            .ToListAsync(cancellationToken);

        var domains = await db.TenantDomains.AsNoTracking()
            .Select(d => new { d.Host, d.TenantId })
            .ToListAsync(cancellationToken);

        var byId = tenants.ToDictionary(t => t.Id);
        var snapshot = new Snapshot(
            byId,
            tenants.ToDictionary(t => t.Slug, StringComparer.OrdinalIgnoreCase),
            domains
                .Where(d => byId.ContainsKey(d.TenantId))
                .GroupBy(d => d.Host)
                .ToDictionary(g => g.Key, g => byId[g.First().TenantId], StringComparer.OrdinalIgnoreCase));

        cache.Set(CacheKey, snapshot, CacheDuration);
        return snapshot;
    }

    private sealed record Snapshot(
        IReadOnlyDictionary<Guid, TenantInfo> ById,
        IReadOnlyDictionary<string, TenantInfo> BySlug,
        IReadOnlyDictionary<string, TenantInfo> ByHost);
}
