using System.Text.Json.Serialization;

namespace ArtDash.Infrastructure.Payments.MercadoPago;

// Wire models for POST/GET /v1/payments (ported from artstore-fs, trimmed to what ArtDash uses).

internal sealed class MpCreatePaymentRequest
{
    [JsonPropertyName("transaction_amount")]
    public decimal TransactionAmount { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("payment_method_id")]
    public string PaymentMethodId { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("installments")]
    public int? Installments { get; set; }

    [JsonPropertyName("issuer_id")]
    public string? IssuerId { get; set; }

    [JsonPropertyName("external_reference")]
    public string? ExternalReference { get; set; }

    [JsonPropertyName("notification_url")]
    public string? NotificationUrl { get; set; }

    [JsonPropertyName("date_of_expiration")]
    public string? DateOfExpiration { get; set; }

    [JsonPropertyName("payer")]
    public MpPayer Payer { get; set; } = new();
}

internal sealed class MpPayer
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("identification")]
    public MpIdentification? Identification { get; set; }
}

internal sealed class MpIdentification
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }
}

internal sealed class MpPaymentResponse
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("status_detail")]
    public string? StatusDetail { get; set; }

    [JsonPropertyName("transaction_amount")]
    public decimal? TransactionAmount { get; set; }

    [JsonPropertyName("payment_method_id")]
    public string? PaymentMethodId { get; set; }

    [JsonPropertyName("payment_type_id")]
    public string? PaymentTypeId { get; set; }

    [JsonPropertyName("date_approved")]
    public DateTimeOffset? DateApproved { get; set; }

    [JsonPropertyName("date_of_expiration")]
    public DateTimeOffset? DateOfExpiration { get; set; }

    [JsonPropertyName("external_reference")]
    public string? ExternalReference { get; set; }

    [JsonPropertyName("point_of_interaction")]
    public MpPointOfInteraction? PointOfInteraction { get; set; }
}

internal sealed class MpPointOfInteraction
{
    [JsonPropertyName("transaction_data")]
    public MpTransactionData? TransactionData { get; set; }
}

internal sealed class MpTransactionData
{
    [JsonPropertyName("qr_code")]
    public string? QrCode { get; set; }

    [JsonPropertyName("qr_code_base64")]
    public string? QrCodeBase64 { get; set; }

    [JsonPropertyName("ticket_url")]
    public string? TicketUrl { get; set; }
}
