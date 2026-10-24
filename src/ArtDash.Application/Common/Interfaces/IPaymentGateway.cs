namespace ArtDash.Application.Common.Interfaces;

/// <summary>
/// Online payment gateway (Mercado Pago Checkout Transparente). Credentials are per tenant, so they
/// are passed on every call instead of being configured once.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayPaymentResult> CreatePaymentAsync(
        GatewayPaymentRequest request,
        string accessToken,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<GatewayPaymentResult> GetPaymentAsync(string providerPaymentId, string accessToken, CancellationToken cancellationToken);

    /// <summary>Validates the <c>x-signature</c> header of a webhook. No secret configured means "accept".</summary>
    bool IsValidWebhookSignature(string? webhookSecret, string? signatureHeader, string? requestId, string? dataId);
}

public sealed record GatewayPaymentRequest(
    decimal Amount,
    string Description,
    string PaymentMethodId,
    string? Token,
    string? IssuerId,
    int? Installments,
    string ExternalReference,
    string? NotificationUrl,
    string? PayerEmail,
    string? PayerFirstName,
    string? PayerLastName,
    string? IdentificationType,
    string? IdentificationNumber,
    DateTime? ExpiresAt);

public sealed record GatewayPaymentResult
{
    /// <summary>False when the gateway could not be reached or rejected the request itself.</summary>
    public bool Success { get; init; }

    public string? ProviderPaymentId { get; init; }
    public TransactionStatus Status { get; init; }
    public string? StatusDetail { get; init; }
    public PaymentMethod? Method { get; init; }
    public decimal? Amount { get; init; }
    public string? ExternalReference { get; init; }
    public string? PixQrCode { get; init; }
    public string? PixQrCodeBase64 { get; init; }
    public string? TicketUrl { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? RawResponse { get; init; }
    public string? ErrorMessage { get; init; }

    public static GatewayPaymentResult Failure(string message, string? raw = null) => new()
    {
        Success = false,
        Status = TransactionStatus.Failed,
        StatusDetail = message,
        ErrorMessage = message,
        RawResponse = raw
    };
}
