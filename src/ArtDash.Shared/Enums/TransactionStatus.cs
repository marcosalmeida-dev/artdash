namespace ArtDash.Shared.Enums;

/// <summary>State of a single payment attempt at the gateway (Mercado Pago).</summary>
public enum TransactionStatus
{
    Pending = 1,
    InProcess = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
    Refunded = 6,
    ChargedBack = 7,
    Expired = 8,
    Failed = 9
}
