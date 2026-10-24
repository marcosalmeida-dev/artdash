using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtDash.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.Property(o => o.Number).HasMaxLength(20).IsRequired();
        builder.Property(o => o.TrackingCode).HasMaxLength(16).IsRequired();
        builder.Property(o => o.CustomerName).HasMaxLength(150).IsRequired();
        builder.Property(o => o.CustomerPhone).HasMaxLength(20);
        builder.Property(o => o.CustomerDocument).HasMaxLength(14);
        builder.Property(o => o.Notes).HasMaxLength(1000);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.CreatedBy).HasMaxLength(150);
        builder.Property(o => o.UpdatedBy).HasMaxLength(150);
        builder.Property(o => o.RowVersion).IsRowVersion();
        builder.Ignore(o => o.OutstandingAmount);
        builder.Ignore(o => o.IsFinal);

        builder.OwnsOne(o => o.DeliveryAddress, a => AddressMapping.Configure(a));
        builder.Navigation(o => o.DeliveryAddress).IsRequired();

        builder.HasOne(o => o.Customer)
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Delivery>()
            .WithMany()
            .HasForeignKey(o => o.DeliveryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.StatusHistory).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.Payments).WithOne().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.StatusHistory).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(o => new { o.TenantId, o.Number }).IsUnique();
        builder.HasIndex(o => o.TrackingCode).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.CustomerId });
        builder.HasIndex(o => new { o.TenantId, o.PlacedAt });
        builder.HasIndex(o => new { o.TenantId, o.PaymentTiming, o.PaymentStatus });
        builder.HasIndex(o => o.DeliveryId);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.Property(i => i.ProductName).HasMaxLength(150).IsRequired();
        builder.Property(i => i.Sku).HasMaxLength(50);
        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.OrderId);
    }
}

internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistory");
        builder.Property(h => h.ChangedBy).HasMaxLength(150);
        builder.Property(h => h.Note).HasMaxLength(500);
        builder.HasIndex(h => h.OrderId);
    }
}

internal sealed class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.Property(p => p.Reference).HasMaxLength(200);
        builder.Property(p => p.RegisteredBy).HasMaxLength(150);
        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.PaymentTransactionId);
    }
}

internal sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("PaymentTransactions");
        builder.Property(t => t.Provider).HasMaxLength(30).IsRequired();
        builder.Property(t => t.ProviderPaymentId).HasMaxLength(50);
        builder.Property(t => t.StatusDetail).HasMaxLength(200);
        builder.Property(t => t.PixQrCode).HasMaxLength(2000);
        builder.Property(t => t.TicketUrl).HasMaxLength(500);
        builder.Property(t => t.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(t => t.CreatedBy).HasMaxLength(150);
        builder.Property(t => t.UpdatedBy).HasMaxLength(150);
        builder.Ignore(t => t.IsFinal);
        builder.Ignore(t => t.IsPix);

        builder.HasOne<Order>().WithMany().HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.OrderId);
        builder.HasIndex(t => t.ProviderPaymentId);
    }
}
