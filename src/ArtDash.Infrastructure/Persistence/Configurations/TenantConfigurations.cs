using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtDash.Infrastructure.Persistence.Configurations;

internal static class AddressMapping
{
    public static void Configure<TOwner>(OwnedNavigationBuilder<TOwner, Address> address)
        where TOwner : class
    {
        address.Property(a => a.ZipCode).HasMaxLength(8).IsRequired();
        address.Property(a => a.Street).HasMaxLength(200).IsRequired();
        address.Property(a => a.Number).HasMaxLength(20).IsRequired();
        address.Property(a => a.Complement).HasMaxLength(100);
        address.Property(a => a.District).HasMaxLength(100).IsRequired();
        address.Property(a => a.City).HasMaxLength(100).IsRequired();
        address.Property(a => a.State).HasMaxLength(2).IsRequired();
        address.Ignore(a => a.HasCoordinates);
    }
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.Property(t => t.Name).HasMaxLength(150).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(50).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();

        builder.OwnsOne(t => t.Branding, b =>
        {
            b.Property(x => x.DisplayName).HasMaxLength(100);
            b.Property(x => x.Tagline).HasMaxLength(160);
            b.Property(x => x.LogoUrl).HasMaxLength(500);
            b.Property(x => x.LogoDarkUrl).HasMaxLength(500);
            b.Property(x => x.IconUrl).HasMaxLength(500);
            b.Property(x => x.PrimaryColor).HasMaxLength(7).IsRequired();
            b.Property(x => x.SecondaryColor).HasMaxLength(7).IsRequired();
            b.Property(x => x.AccentColor).HasMaxLength(7).IsRequired();
            b.Property(x => x.ConsoleColor).HasMaxLength(7).IsRequired();
            b.Property(x => x.FontFamily).HasMaxLength(50).IsRequired();
        });
        builder.Navigation(t => t.Branding).IsRequired();

        builder.OwnsOne(t => t.Store, s =>
        {
            s.Property(x => x.Phone).HasMaxLength(20);
            s.Property(x => x.WhatsApp).HasMaxLength(20);
            s.Property(x => x.Email).HasMaxLength(150);
            s.Property(x => x.Document).HasMaxLength(14);
            s.Property(x => x.OpeningHours).HasMaxLength(300);
            s.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        });
        builder.Navigation(t => t.Store).IsRequired();

        builder.OwnsOne(t => t.Payments, p =>
        {
            p.Property(x => x.MercadoPagoPublicKey).HasMaxLength(200);
            p.Property(x => x.MercadoPagoAccessTokenProtected).HasMaxLength(2000);
            p.Property(x => x.MercadoPagoWebhookSecretProtected).HasMaxLength(2000);
            p.Ignore(x => x.IsOnlinePaymentConfigured);
        });
        builder.Navigation(t => t.Payments).IsRequired();

        builder.OwnsOne(t => t.Address, a => AddressMapping.Configure(a));
        builder.Navigation(t => t.Address).IsRequired();

        builder.HasMany(t => t.Domains)
            .WithOne()
            .HasForeignKey(d => d.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Domains).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TenantDomainConfiguration : IEntityTypeConfiguration<TenantDomain>
{
    public void Configure(EntityTypeBuilder<TenantDomain> builder)
    {
        builder.ToTable("TenantDomains");
        builder.Property(d => d.Host).HasMaxLength(253).IsRequired();
        builder.HasIndex(d => d.Host).IsUnique();
    }
}

internal sealed class TenantSequenceConfiguration : IEntityTypeConfiguration<TenantSequence>
{
    public void Configure(EntityTypeBuilder<TenantSequence> builder)
    {
        builder.ToTable("TenantSequences");
        builder.Property(s => s.Name).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Value).IsConcurrencyToken();
        builder.HasIndex(s => new { s.TenantId, s.Name }).IsUnique();
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.ReplacedByHash).HasMaxLength(128);
        builder.Property(t => t.CreatedByIp).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
