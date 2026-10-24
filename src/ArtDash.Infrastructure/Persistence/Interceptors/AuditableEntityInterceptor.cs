using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArtDash.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps audit fields and the tenant on new rows, and refuses to modify rows of another tenant.
/// </summary>
public sealed class AuditableEntityInterceptor(ICurrentUser currentUser, ICurrentTenant currentTenant, TimeProvider clock)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var user = currentUser.Name ?? currentUser.UserId?.ToString() ?? "sistema";

        foreach (var entry in context.ChangeTracker.Entries())
        {
            var ownedEntitiesChanged = HasChangedOwnedEntities(entry);
            if (entry.Entity is ITenantEntity tenantEntity)
            {
                StampTenant(entry, tenantEntity, ownedEntitiesChanged);
            }

            if (entry.Entity is IAuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = now;
                    auditable.CreatedBy = user;
                }
                else if (entry.State == EntityState.Modified || ownedEntitiesChanged)
                {
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = user;
                }
            }
        }
    }

    private void StampTenant(EntityEntry entry, ITenantEntity entity, bool ownedEntitiesChanged)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) && !ownedEntitiesChanged)
        {
            return;
        }

        var tenantId = currentTenant.Id
            ?? throw new InvalidOperationException($"Não é possível gravar {entry.Metadata.ClrType.Name} sem uma empresa (tenant) definida.");

        if (entry.State == EntityState.Added && entity.TenantId == Guid.Empty)
        {
            entity.TenantId = tenantId;
        }

        // Check the original owner too: changing TenantId must never transfer an existing row.
        if (entity.TenantId != tenantId ||
            (entry.State != EntityState.Added && entry.Property(nameof(ITenantEntity.TenantId)).OriginalValue is Guid original && original != tenantId))
        {
            throw new InvalidOperationException("Tentativa de alterar dados de outra empresa (tenant).");
        }
    }

    private static bool HasChangedOwnedEntities(EntityEntry entry) =>
        entry.References.Any(r =>
            r.TargetEntry is not null &&
            r.TargetEntry.Metadata.IsOwned() &&
            r.TargetEntry.State is EntityState.Added or EntityState.Modified);
}
