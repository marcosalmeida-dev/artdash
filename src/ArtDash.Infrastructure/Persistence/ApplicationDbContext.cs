using System.Linq.Expressions;
using ArtDash.Infrastructure.Persistence.Converters;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace ArtDash.Infrastructure.Persistence;

/// <summary>
/// The EF Core unit of work.
/// <para>
/// Multi-tenancy is enforced here, not in the handlers: every entity implementing <see cref="ITenantEntity"/>
/// gets a global query filter on <see cref="CurrentTenantId"/>, and every <see cref="ISoftDelete"/> entity a
/// filter hiding deleted rows. EF evaluates <see cref="CurrentTenantId"/> per query, so one cached model
/// serves all tenants. (artstore-fs filtered by tenant by hand in each query.)
/// </para>
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenant currentTenant)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IApplicationDbContext, IDataProtectionKeyContext
{
    /// <summary>Read by the global query filters. Guid.Empty (no tenant) matches nothing.</summary>
    public Guid CurrentTenantId => currentTenant.Id ?? Guid.Empty;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryStop> DeliveryStops => Set<DeliveryStop>();
    public DbSet<VehiclePosition> VehiclePositions => Set<VehiclePosition>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureIdentityTables(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        ApplyConventions(builder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<decimal?>().HavePrecision(18, 2);

        // Timestamps are stored in UTC and always come back marked as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    private static void ConfigureIdentityTables(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("Users");
            b.Property(u => u.FullName).HasMaxLength(150).IsRequired();

            // Identity makes user names globally unique; here they are unique per tenant.
            b.HasIndex(u => u.NormalizedUserName).HasDatabaseName("UserNameIndex").IsUnique(false);
            b.HasIndex(u => new { u.TenantId, u.NormalizedUserName }).IsUnique().HasDatabaseName("IX_Users_Tenant_UserName");
            b.HasIndex(u => new { u.TenantId, u.NormalizedEmail }).HasDatabaseName("IX_Users_Tenant_Email");
        });

        builder.Entity<ApplicationRole>(b =>
        {
            b.ToTable("Roles");
            b.Property(r => r.Description).HasMaxLength(200);
        });

        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
    }

    /// <summary>Keys, tenant foreign keys and the global query filters, applied to every entity type at once.</summary>
    private void ApplyConventions(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes().ToList())
        {
            if (entityType.IsOwned() || entityType.BaseType is not null)
            {
                continue;
            }

            var clrType = entityType.ClrType;

            // Ids are assigned by the domain (GUID v7) — never by the database. Without this EF would
            // treat new children added to a loaded aggregate as existing rows and issue UPDATEs.
            if (typeof(BaseEntity).IsAssignableFrom(clrType))
            {
                builder.Entity(clrType).Property(nameof(BaseEntity.Id)).ValueGeneratedNever();
            }

            var isTenantOwned = typeof(ITenantEntity).IsAssignableFrom(clrType);
            var isSoftDeletable = typeof(ISoftDelete).IsAssignableFrom(clrType);

            if (isTenantOwned)
            {
                builder.Entity(clrType)
                    .HasOne(typeof(Tenant))
                    .WithMany()
                    .HasForeignKey(nameof(ITenantEntity.TenantId))
                    .OnDelete(DeleteBehavior.Restrict);

                builder.Entity(clrType).HasIndex(nameof(ITenantEntity.TenantId));
            }

            if (!isTenantOwned && !isSoftDeletable)
            {
                continue;
            }

            var entity = Expression.Parameter(clrType, "e");
            Expression? condition = null;

            if (isTenantOwned)
            {
                condition = Expression.Equal(
                    Expression.Property(entity, nameof(ITenantEntity.TenantId)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
            }

            if (isSoftDeletable)
            {
                var notDeleted = Expression.Equal(
                    Expression.Property(entity, nameof(ISoftDelete.DeletedAt)),
                    Expression.Constant(null, typeof(DateTime?)));

                condition = condition is null ? notDeleted : Expression.AndAlso(condition, notDeleted);
            }

            builder.Entity(clrType).HasQueryFilter(Expression.Lambda(condition!, entity));
        }
    }
}
