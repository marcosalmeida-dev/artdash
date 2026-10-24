namespace ArtDash.Shared.Enums;

/// <summary>When the customer pays for an order.</summary>
public enum PaymentTiming
{
    /// <summary>Paid before the order is confirmed — online (PIX/card) or registered by staff.</summary>
    Prepaid = 1,

    /// <summary>Paid to the driver on delivery (card machine, PIX or cash).</summary>
    OnDelivery = 2,

    /// <summary>Invoiced: paid later, within the customer's payment term (accounts receivable).</summary>
    Invoiced = 3
}
