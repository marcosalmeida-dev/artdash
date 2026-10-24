using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtDash.Infrastructure.Persistence.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");
        builder.Property(v => v.Plate).HasMaxLength(10).IsRequired();
        builder.Property(v => v.Model).HasMaxLength(100);
        builder.Property(v => v.Notes).HasMaxLength(500);
        builder.Property(v => v.CreatedBy).HasMaxLength(150);
        builder.Property(v => v.UpdatedBy).HasMaxLength(150);
        builder.Property(v => v.DeletedBy).HasMaxLength(150);
        builder.Ignore(v => v.IsDeleted);
        builder.HasIndex(v => new { v.TenantId, v.Plate });
    }
}

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("Drivers");
        builder.Property(d => d.Name).HasMaxLength(150).IsRequired();
        builder.Property(d => d.Phone).HasMaxLength(20);
        builder.Property(d => d.LicenseNumber).HasMaxLength(20);
        builder.Property(d => d.LicenseCategory).HasMaxLength(5);
        builder.Property(d => d.CreatedBy).HasMaxLength(150);
        builder.Property(d => d.UpdatedBy).HasMaxLength(150);
        builder.Property(d => d.DeletedBy).HasMaxLength(150);
        builder.Ignore(d => d.IsDeleted);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.TenantId, d.UserId });
    }
}

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("Deliveries");
        builder.Property(d => d.Number).HasMaxLength(20).IsRequired();
        builder.Property(d => d.Notes).HasMaxLength(500);
        builder.Property(d => d.CancellationReason).HasMaxLength(500);
        builder.Property(d => d.CreatedBy).HasMaxLength(150);
        builder.Property(d => d.UpdatedBy).HasMaxLength(150);
        builder.Ignore(d => d.HasPendingStops);

        builder.HasOne(d => d.Driver).WithMany().HasForeignKey(d => d.DriverId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Vehicle).WithMany().HasForeignKey(d => d.VehicleId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(d => d.Stops).WithOne().HasForeignKey(s => s.DeliveryId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(d => d.Stops).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => new { d.TenantId, d.Number }).IsUnique();
        builder.HasIndex(d => new { d.TenantId, d.Status });
        builder.HasIndex(d => d.DriverId);
    }
}

internal sealed class DeliveryStopConfiguration : IEntityTypeConfiguration<DeliveryStop>
{
    public void Configure(EntityTypeBuilder<DeliveryStop> builder)
    {
        builder.ToTable("DeliveryStops");
        builder.Property(s => s.RecipientName).HasMaxLength(150);
        builder.Property(s => s.Notes).HasMaxLength(500);
        builder.Property(s => s.FailureReason).HasMaxLength(500);
        builder.HasOne<Order>().WithMany().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.DeliveryId, s.Sequence });
        builder.HasIndex(s => s.OrderId);
    }
}

internal sealed class VehiclePositionConfiguration : IEntityTypeConfiguration<VehiclePosition>
{
    public void Configure(EntityTypeBuilder<VehiclePosition> builder)
    {
        builder.ToTable("VehiclePositions");
        builder.HasOne<Delivery>().WithMany().HasForeignKey(p => p.DeliveryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.DeliveryId, p.RecordedAt });
    }
}
