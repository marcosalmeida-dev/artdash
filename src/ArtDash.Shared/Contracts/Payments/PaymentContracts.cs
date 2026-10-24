using ArtDash.Shared.Enums;

namespace ArtDash.Shared.Contracts.Payments;

/// <summary>
/// Online payment request built from the Mercado Pago Payment Brick submission. The card is tokenized
/// in the browser by the Mercado Pago SDK; only the token reaches the API.
/// </summary>
public sealed class CreateOnlinePaymentRequest
{
    public Guid OrderId { get; set; }

    /// <summary>Mercado Pago payment method id: <c>pix</c>, <c>visa</c>, <c>master</c>, <c>debvisa</c>...</summary>
    public string PaymentMethodId { get; set; } = string.Empty;

    /// <summary><c>credit_card</c>, <c>debit_card</c> or <c>bank_transfer</c> (PIX).</summary>
    public string? PaymentTypeId { get; set; }

    public string? Token { get; set; }
    public string? IssuerId { get; set; }
    public int? Installments { get; set; }
    public string? PayerEmail { get; set; }
    public string? PayerFirstName { get; set; }
    public string? PayerLastName { get; set; }
    public string? IdentificationType { get; set; }
    public string? IdentificationNumber { get; set; }
}

public sealed record OnlinePaymentResultDto
{
    public Guid TransactionId { get; init; }
    public TransactionStatus Status { get; init; }
    public string? StatusDetail { get; init; }
    public PaymentMethod Method { get; init; }
    public bool IsPix { get; init; }
    public string? PixQrCode { get; init; }
    public string? PixQrCodeBase64 { get; init; }
    public string? TicketUrl { get; init; }
    public DateTime? ExpiresAt { get; init; }

    /// <summary>True once the order is fully paid (the UI can stop waiting).</summary>
    public bool OrderPaid { get; init; }
}

public sealed record PaymentTransactionDto(
    Guid Id,
    string Provider,
    string? ProviderPaymentId,
    PaymentMethod Method,
    TransactionStatus Status,
    string? StatusDetail,
    decimal Amount,
    int? Installments,
    DateTime CreatedAt,
    DateTime? ApprovedAt);
