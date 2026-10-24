namespace ArtDash.Domain.Common;

/// <summary>
/// Root of every entity: a sortable GUID v7 key assigned on creation (so aggregates can reference
/// each other before they are saved) and a list of domain events raised by business methods.
/// </summary>
/// <remarks>
/// Events are exposed through methods rather than a property so EF Core never tries to map them.
/// They are dispatched by the persistence layer only after the changes are committed.
/// </remarks>
public abstract class BaseEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public IReadOnlyCollection<IDomainEvent> GetDomainEvents() => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}

/// <summary>Who created/changed a row and when. Filled by the persistence layer.</summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}

public abstract class BaseAuditableEntity : BaseEntity, IAuditableEntity
{
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Data owned by one tenant. A global query filter keeps every query inside the current tenant and the
/// persistence layer stamps <see cref="TenantId"/> on insert, so application code never handles it.
/// </summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

public abstract class BaseTenantEntity : BaseAuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
}

/// <summary>Rows that are hidden instead of deleted (they may still be referenced by history).</summary>
public interface ISoftDelete
{
    DateTime? DeletedAt { get; }
    string? DeletedBy { get; }
    bool IsDeleted { get; }
    void MarkDeleted(DateTime deletedAt, string? deletedBy);
}

public abstract class BaseSoftDeleteTenantEntity : BaseTenantEntity, ISoftDelete
{
    public DateTime? DeletedAt { get; private set; }
    public string? DeletedBy { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    public void MarkDeleted(DateTime deletedAt, string? deletedBy)
    {
        DeletedAt = deletedAt;
        DeletedBy = deletedBy;
    }
}
