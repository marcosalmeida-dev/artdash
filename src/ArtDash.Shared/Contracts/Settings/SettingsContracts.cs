using ArtDash.Shared.Contracts.Common;

namespace ArtDash.Shared.Contracts.Settings;

/// <summary>Public store information used by the storefront (anonymous).</summary>
public sealed record StoreInfoDto
{
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? WhatsApp { get; init; }
    public string? Email { get; init; }
    public string? OpeningHours { get; init; }
    public string TimeZoneId { get; init; } = "America/Sao_Paulo";
    public AddressDto? Address { get; init; }
    public decimal DeliveryFee { get; init; }
    public decimal? FreeDeliveryThreshold { get; init; }
    public decimal? MinimumOrderAmount { get; init; }
    public PaymentOptionsDto Payments { get; init; } = new();
}

/// <summary>Which payment options the storefront may offer. Never carries secrets.</summary>
public sealed record PaymentOptionsDto
{
    public bool OnlinePaymentAvailable { get; init; }
    public bool PixEnabled { get; init; }
    public bool CreditCardEnabled { get; init; }
    public bool DebitCardEnabled { get; init; }
    public int MaxInstallments { get; init; } = 1;
    public bool PayOnDeliveryEnabled { get; init; }
    public bool InvoicingEnabled { get; init; }
    public string? MercadoPagoPublicKey { get; init; }
}

/// <summary>Admin form for the store's contact data, address and delivery pricing.</summary>
public sealed class StoreSettingsDto
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? Email { get; set; }
    public string? Document { get; set; }
    public string? OpeningHours { get; set; }
    public string TimeZoneId { get; set; } = "America/Sao_Paulo";
    public AddressDto Address { get; set; } = new();
    public decimal DeliveryFee { get; set; }
    public decimal? FreeDeliveryThreshold { get; set; }
    public decimal? MinimumOrderAmount { get; set; }
}

/// <summary>
/// Admin form for payment configuration. Secrets are write-only: the API returns them empty and
/// reports only whether one is stored; sending an empty value keeps the stored one.
/// </summary>
public sealed class PaymentSettingsDto
{
    public string? MercadoPagoPublicKey { get; set; }
    public string? MercadoPagoAccessToken { get; set; }
    public string? MercadoPagoWebhookSecret { get; set; }
    public bool HasAccessToken { get; set; }
    public bool HasWebhookSecret { get; set; }
    public bool MercadoPagoSandbox { get; set; } = true;
    public bool OnlinePixEnabled { get; set; } = true;
    public bool OnlineCreditCardEnabled { get; set; } = true;
    public bool OnlineDebitCardEnabled { get; set; } = true;
    public int MaxInstallments { get; set; } = 1;
    public int PixExpirationMinutes { get; set; } = 30;
    public bool PayOnDeliveryEnabled { get; set; } = true;
    public bool InvoicingEnabled { get; set; }
    public int DefaultPaymentTermDays { get; set; } = 28;

    /// <summary>Read-only: the URL to register in the Mercado Pago panel for notifications.</summary>
    public string? WebhookUrl { get; set; }
}
