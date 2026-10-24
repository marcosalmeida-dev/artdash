namespace ArtDash.Shared.Enums;

/// <summary>Payment state of an order (as opposed to a single gateway transaction).</summary>
public enum PaymentStatus
{
    Pending = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Refunded = 4
}
