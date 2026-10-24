using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArtDash.Infrastructure.Payments.MercadoPago;

/// <summary>
/// Mercado Pago Checkout Transparente (POST/GET /v1/payments). Ported from artstore-fs
/// (<c>MercadoPagoCheckoutApiService</c>, status mapping and webhook signature), with per-tenant credentials.
/// </summary>
internal sealed class MercadoPagoGateway(HttpClient http, ILogger<MercadoPagoGateway> logger) : IPaymentGateway
{
    public const string BaseAddress = "https://api.mercadopago.com/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<GatewayPaymentResult> CreatePaymentAsync(
        GatewayPaymentRequest request,
        string accessToken,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = new MpCreatePaymentRequest
        {
            TransactionAmount = decimal.Round(request.Amount, 2),
            Description = request.Description,
            PaymentMethodId = request.PaymentMethodId,
            Token = request.Token,
            Installments = request.Installments,
            IssuerId = request.IssuerId,
            ExternalReference = request.ExternalReference,
            NotificationUrl = request.NotificationUrl,
            DateOfExpiration = request.ExpiresAt is { } expires
                ? new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture)
                : null,
            Payer = new MpPayer
            {
                Email = request.PayerEmail,
                FirstName = request.PayerFirstName,
                LastName = request.PayerLastName,
                Identification = string.IsNullOrWhiteSpace(request.IdentificationNumber)
                    ? null
                    : new MpIdentification
                    {
                        Type = request.IdentificationType ?? (request.IdentificationNumber!.Length > 11 ? "CNPJ" : "CPF"),
                        Number = request.IdentificationNumber
                    }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/payments")
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        // Prevents a retry or a double click from charging the buyer twice.
        message.Headers.Add("X-Idempotency-Key", idempotencyKey);

        return await SendAsync(message, cancellationToken);
    }

    public async Task<GatewayPaymentResult> GetPaymentAsync(string providerPaymentId, string accessToken, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"v1/payments/{Uri.EscapeDataString(providerPaymentId)}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync(message, cancellationToken);
    }

    public bool IsValidWebhookSignature(string? webhookSecret, string? signatureHeader, string? requestId, string? dataId) =>
        MercadoPagoWebhookSignature.IsValid(webhookSecret, signatureHeader, requestId, dataId);

    private async Task<GatewayPaymentResult> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Mercado Pago returned {StatusCode}: {Body}", (int)response.StatusCode, Truncate(content));
                return GatewayPaymentResult.Failure(TryReadError(content) ?? $"Mercado Pago recusou a requisição ({(int)response.StatusCode}).", content);
            }

            var payment = JsonSerializer.Deserialize<MpPaymentResponse>(content, JsonOptions);
            if (payment?.Id is null)
            {
                return GatewayPaymentResult.Failure("Resposta inválida do Mercado Pago.", content);
            }

            var transactionData = payment.PointOfInteraction?.TransactionData;
            return new GatewayPaymentResult
            {
                Success = true,
                ProviderPaymentId = payment.Id.Value.ToString(CultureInfo.InvariantCulture),
                Status = MapStatus(payment.Status),
                StatusDetail = payment.StatusDetail,
                Method = MapMethod(payment.PaymentMethodId, payment.PaymentTypeId),
                Amount = payment.TransactionAmount,
                ExternalReference = payment.ExternalReference,
                PixQrCode = transactionData?.QrCode,
                PixQrCodeBase64 = transactionData?.QrCodeBase64,
                TicketUrl = transactionData?.TicketUrl,
                ExpiresAt = payment.DateOfExpiration?.UtcDateTime,
                ApprovedAt = payment.DateApproved?.UtcDateTime,
                RawResponse = content
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(ex, "Error calling Mercado Pago");
            return GatewayPaymentResult.Failure("Não foi possível comunicar com o Mercado Pago. Tente novamente.");
        }
    }

    /// <summary>Unknown statuses stay non-final on purpose, so polling keeps going (lesson from artstore-fs).</summary>
    private static TransactionStatus MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "approved" => TransactionStatus.Approved,
        "authorized" => TransactionStatus.InProcess,
        "pending" => TransactionStatus.Pending,
        "in_process" => TransactionStatus.InProcess,
        "in_mediation" => TransactionStatus.InProcess,
        "rejected" => TransactionStatus.Rejected,
        "cancelled" => TransactionStatus.Cancelled,
        "refunded" => TransactionStatus.Refunded,
        "charged_back" => TransactionStatus.ChargedBack,
        "expired" => TransactionStatus.Expired,
        _ => TransactionStatus.InProcess
    };

    private static PaymentMethod? MapMethod(string? paymentMethodId, string? paymentTypeId) => paymentTypeId switch
    {
        "credit_card" => PaymentMethod.CreditCard,
        "debit_card" or "prepaid_card" => PaymentMethod.DebitCard,
        "bank_transfer" => PaymentMethod.Pix,
        "ticket" => PaymentMethod.BankSlip,
        _ => string.Equals(paymentMethodId, "pix", StringComparison.OrdinalIgnoreCase) ? (PaymentMethod?)PaymentMethod.Pix : null
    };

    private static string? TryReadError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("status_detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return $"Pagamento recusado ({detail.GetString()}).";
            }

            if (root.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string Truncate(string value) => value.Length <= 1000 ? value : value[..1000];
}

/// <summary>
/// Validates the <c>x-signature</c> header (HMAC-SHA256 of <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c>).
/// Ported from artstore-fs, including the lowercase/verbatim data.id fallback.
/// </summary>
internal static class MercadoPagoWebhookSignature
{
    public static bool IsValid(string? webhookSecret, string? signatureHeader, string? requestId, string? dataId)
    {
        // No secret configured: nothing to check against (the payment is re-read from the API anyway).
        if (string.IsNullOrEmpty(webhookSecret))
        {
            return true;
        }

        if (string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        string? ts = null;
        string? v1 = null;
        foreach (var part in signatureHeader.Split(','))
        {
            var separator = part.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (key == "ts")
            {
                ts = value;
            }
            else if (key == "v1")
            {
                v1 = value;
            }
        }

        if (string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(v1))
        {
            return false;
        }

        foreach (var id in CandidateIds(dataId))
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(id))
            {
                parts.Add($"id:{id}");
            }

            if (!string.IsNullOrEmpty(requestId))
            {
                parts.Add($"request-id:{requestId}");
            }

            parts.Add($"ts:{ts}");
            var manifest = string.Join(";", parts) + ";";

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
            var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();

            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(v1)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string?> CandidateIds(string? dataId)
    {
        if (string.IsNullOrEmpty(dataId))
        {
            yield return dataId;
            yield break;
        }

        var lowered = dataId.ToLowerInvariant();
        yield return lowered;

        if (!string.Equals(lowered, dataId, StringComparison.Ordinal))
        {
            yield return dataId;
        }
    }
}
