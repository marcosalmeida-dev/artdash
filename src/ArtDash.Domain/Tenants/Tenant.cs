namespace ArtDash.Domain.Tenants;

/// <summary>
/// A white-label customer of the platform: its own brand, settings, users and data.
/// Requests are routed to a tenant by host name (custom domain or <c>slug.root-domain</c>).
/// </summary>
public sealed class Tenant : BaseAuditableEntity
{
    private readonly List<TenantDomain> _domains = [];

    private Tenant()
    {
    }

    public Tenant(string name, string slug)
    {
        Rename(name);
        Slug = NormalizeSlug(slug);
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Lowercase identifier used in sub-domains and webhook URLs.</summary>
    public string Slug { get; private set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public TenantBranding Branding { get; set; } = new();
    public TenantStoreSettings Store { get; set; } = new();
    public TenantPaymentSettings Payments { get; set; } = new();

    /// <summary>Store address: shown to customers and used as the map centre.</summary>
    public Address Address { get; set; } = new();

    public IReadOnlyCollection<TenantDomain> Domains => _domains;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("O nome da empresa é obrigatório.");
        }

        Name = name.Trim();
    }

    public void AddDomain(string host)
    {
        var normalized = TenantDomain.NormalizeHost(host);
        if (_domains.Any(d => d.Host == normalized))
        {
            return;
        }

        _domains.Add(new TenantDomain(Id, normalized));
    }

    public static string NormalizeSlug(string slug)
    {
        var normalized = new string((slug ?? string.Empty).Trim().ToLowerInvariant()
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            .ToArray());

        if (normalized.Length is < 2 or > 50)
        {
            throw new DomainException("O identificador da empresa deve ter entre 2 e 50 caracteres (letras, números ou hífen).");
        }

        return normalized;
    }
}

/// <summary>A host name that routes to a tenant (e.g. <c>pedidos.acme.com.br</c>).</summary>
public sealed class TenantDomain : BaseEntity
{
    private TenantDomain()
    {
    }

    internal TenantDomain(Guid tenantId, string host)
    {
        TenantId = tenantId;
        Host = host;
    }

    public Guid TenantId { get; private set; }
    public string Host { get; private set; } = string.Empty;

    public static string NormalizeHost(string host)
    {
        var value = (host ?? string.Empty).Trim().ToLowerInvariant();
        var portIndex = value.IndexOf(':');
        return portIndex >= 0 ? value[..portIndex] : value;
    }
}

/// <summary>
/// Per-tenant counters for human-friendly numbers (order 000123, delivery E00042).
/// <see cref="Value"/> is a concurrency token, so two requests can never hand out the same number.
/// </summary>
public sealed class TenantSequence : BaseEntity, ITenantEntity
{
    private TenantSequence()
    {
    }

    public TenantSequence(string name)
    {
        Name = name;
    }

    public Guid TenantId { get; set; }
    public string Name { get; private set; } = string.Empty;
    public long Value { get; private set; }

    public long Next() => ++Value;
}

public static class SequenceNames
{
    public const string Order = "order";
    public const string Delivery = "delivery";
}
