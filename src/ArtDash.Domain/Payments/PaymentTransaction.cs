namespace ArtDash.Domain.Payments;

/// <summary>
/// One online payment attempt at the gateway. Created before calling the gateway (so a crash in the
/// middle still leaves a trace) and updated from the gateway response, polling and webhooks.
/// </summary>
public sealed class PaymentTransaction : BaseTenantEntity
{
    public const string MercadoPagoProvider = "MercadoPago";

    private PaymentTransaction()
    {
    }

    public static PaymentTransaction Start(Guid orderId, PaymentMethod method, decimal amount, int? installments, string idempotencyKey)
    {
        if (amount <= 0)
        {
            throw new DomainException("Não há valor a pagar neste pedido.");
        }

        return new PaymentTransaction
        {
            OrderId = orderId,
            Provider = MercadoPagoProvider,
            Method = method,
            Amount = amount,
            Installments = installments,
            IdempotencyKey = idempotencyKey,
            Status = TransactionStatus.Pending
        };
    }

    public Guid OrderId { get; private set; }
    public string Provider { get; private set; } = MercadoPagoProvider;
    public string? ProviderPaymentId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public TransactionStatus Status { get; private set; }
    public string? StatusDetail { get; private set; }
    public decimal Amount { get; private set; }
    public int? Installments { get; private set; }
    public string? PixQrCode { get; private set; }
    public string? PixQrCodeBase64 { get; private set; }
    public string? TicketUrl { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? LastCheckedAt { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? RawResponse { get; private set; }

    public bool IsFinal => Status is TransactionStatus.Approved or TransactionStatus.Rejected or TransactionStatus.Cancelled
        or TransactionStatus.Refunded or TransactionStatus.ChargedBack or TransactionStatus.Expired or TransactionStatus.Failed;

    public bool IsPix => Method == PaymentMethod.Pix;

    public void ApplyGatewayResult(
        string? providerPaymentId,
        TransactionStatus status,
        string? statusDetail,
        PaymentMethod? method,
        string? pixQrCode,
        string? pixQrCodeBase64,
        string? ticketUrl,
        DateTime? expiresAt,
        DateTime? approvedAt,
        string? rawResponse,
        DateTime checkedAt)
    {
        ProviderPaymentId = providerPaymentId ?? ProviderPaymentId;
        Status = status;
        StatusDetail = statusDetail;
        Method = method ?? Method;
        PixQrCode = pixQrCode ?? PixQrCode;
        PixQrCodeBase64 = pixQrCodeBase64 ?? PixQrCodeBase64;
        TicketUrl = ticketUrl ?? TicketUrl;
        ExpiresAt = expiresAt ?? ExpiresAt;
        ApprovedAt = status == TransactionStatus.Approved ? approvedAt ?? ApprovedAt ?? checkedAt : ApprovedAt;
        RawResponse = rawResponse ?? RawResponse;
        LastCheckedAt = checkedAt;
    }

    public void MarkFailed(string detail, DateTime checkedAt)
    {
        Status = TransactionStatus.Failed;
        StatusDetail = detail;
        LastCheckedAt = checkedAt;
    }

    public void MarkChecked(DateTime checkedAt) => LastCheckedAt = checkedAt;
}
