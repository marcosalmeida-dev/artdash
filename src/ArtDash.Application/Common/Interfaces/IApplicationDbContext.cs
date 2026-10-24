using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ArtDash.Application.Common.Interfaces;

/// <summary>
/// The persistence port. Every tenant-owned set is already filtered to the current tenant (and to
/// rows that are not soft-deleted) by global query filters — handlers never filter by tenant.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<TenantDomain> TenantDomains { get; }
    DbSet<ApplicationUser> Users { get; }
    DbSet<ApplicationRole> Roles { get; }
    DbSet<IdentityUserRole<Guid>> UserRoles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderPayment> OrderPayments { get; }
    DbSet<OrderStatusHistory> OrderStatusHistory { get; }
    DbSet<Vehicle> Vehicles { get; }
    DbSet<Driver> Drivers { get; }
    DbSet<Delivery> Deliveries { get; }
    DbSet<DeliveryStop> DeliveryStops { get; }
    DbSet<VehiclePosition> VehiclePositions { get; }
    DbSet<PaymentTransaction> PaymentTransactions { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
