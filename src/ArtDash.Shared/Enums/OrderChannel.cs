namespace ArtDash.Shared.Enums;

/// <summary>Where an order was placed.</summary>
public enum OrderChannel
{
    /// <summary>By the customer, on the storefront.</summary>
    Storefront = 1,

    /// <summary>By staff, on the back office (phone, WhatsApp, sales rep...).</summary>
    Backoffice = 2
}
