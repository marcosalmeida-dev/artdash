namespace ArtDash.Domain.Tenants;

/// <summary>Brand identity (owned by <see cref="Tenant"/>).</summary>
public sealed class TenantBranding
{
    public string? DisplayName { get; set; }
    public string? Tagline { get; set; }
    public string? LogoUrl { get; set; }
    public string? LogoDarkUrl { get; set; }
    public string? IconUrl { get; set; }
    public string PrimaryColor { get; set; } = "#2F6FED";
    public string SecondaryColor { get; set; } = "#0F172A";
    public string AccentColor { get; set; } = "#F59E0B";
    public string ConsoleColor { get; set; } = "#111827";
    public string FontFamily { get; set; } = "Inter";
    public int BorderRadius { get; set; } = 10;
    public bool AllowDarkMode { get; set; } = true;
}

/// <summary>Contact data and delivery pricing (owned by <see cref="Tenant"/>).</summary>
public sealed class TenantStoreSettings
{
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? Email { get; set; }

    /// <summary>CNPJ, digits only.</summary>
    public string? Document { get; set; }

    public string? OpeningHours { get; set; }
    public string TimeZoneId { get; set; } = "America/Sao_Paulo";
    public decimal DeliveryFee { get; set; }
    public decimal? FreeDeliveryThreshold { get; set; }
    public decimal? MinimumOrderAmount { get; set; }

    /// <summary>The flat delivery fee for an order subtotal, honouring the free-delivery threshold.</summary>
    public decimal DeliveryFeeFor(decimal subtotal) =>
        FreeDeliveryThreshold is > 0 && subtotal >= FreeDeliveryThreshold.Value ? 0m : Math.Max(0m, DeliveryFee);
}

/// <summary>
/// Payment configuration (owned by <see cref="Tenant"/>). Mercado Pago secrets are stored already
/// protected (ASP.NET Core Data Protection) — this class never sees them in clear text.
/// </summary>
public sealed class TenantPaymentSettings
{
    public string? MercadoPagoPublicKey { get; set; }
    public string? MercadoPagoAccessTokenProtected { get; set; }
    public string? MercadoPagoWebhookSecretProtected { get; set; }
    public bool MercadoPagoSandbox { get; set; } = true;

    public bool OnlinePixEnabled { get; set; } = true;
    public bool OnlineCreditCardEnabled { get; set; } = true;
    public bool OnlineDebitCardEnabled { get; set; } = true;
    public int MaxInstallments { get; set; } = 1;
    public int PixExpirationMinutes { get; set; } = 30;

    public bool PayOnDeliveryEnabled { get; set; } = true;
    public bool InvoicingEnabled { get; set; }
    public int DefaultPaymentTermDays { get; set; } = 28;

    public bool IsOnlinePaymentConfigured =>
        !string.IsNullOrWhiteSpace(MercadoPagoPublicKey) &&
        !string.IsNullOrWhiteSpace(MercadoPagoAccessTokenProtected) &&
        (OnlinePixEnabled || OnlineCreditCardEnabled || OnlineDebitCardEnabled);

    public bool IsOnlineMethodEnabled(PaymentMethod method) => method switch
    {
        PaymentMethod.Pix => OnlinePixEnabled,
        PaymentMethod.CreditCard => OnlineCreditCardEnabled,
        PaymentMethod.DebitCard => OnlineDebitCardEnabled,
        _ => false
    };
}
