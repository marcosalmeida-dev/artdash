using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtDash.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.CreatedBy).HasMaxLength(150);
        builder.Property(c => c.UpdatedBy).HasMaxLength(150);
        builder.Property(c => c.DeletedBy).HasMaxLength(150);
        builder.Ignore(c => c.IsDeleted);
        builder.HasIndex(c => new { c.TenantId, c.Name });
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Sku).HasMaxLength(50);
        builder.Property(p => p.ImageUrl).HasMaxLength(500);
        builder.Property(p => p.WeightKg).HasPrecision(18, 3);
        builder.Property(p => p.CreatedBy).HasMaxLength(150);
        builder.Property(p => p.UpdatedBy).HasMaxLength(150);
        builder.Property(p => p.DeletedBy).HasMaxLength(150);
        builder.Ignore(p => p.IsDeleted);

        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.TenantId, p.Name });
        builder.HasIndex(p => new { p.TenantId, p.Sku });
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.Property(c => c.TradeName).HasMaxLength(150);
        builder.Property(c => c.Document).HasMaxLength(14);
        builder.Property(c => c.Email).HasMaxLength(150);
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.CreatedBy).HasMaxLength(150);
        builder.Property(c => c.UpdatedBy).HasMaxLength(150);
        builder.Property(c => c.DeletedBy).HasMaxLength(150);
        builder.Ignore(c => c.IsDeleted);

        builder.OwnsOne(c => c.Address, a => AddressMapping.Configure(a));
        builder.Navigation(c => c.Address).IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.TenantId, c.Name });
        builder.HasIndex(c => new { c.TenantId, c.Document });
        builder.HasIndex(c => c.UserId);
    }
}
