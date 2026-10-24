using System.Text.Json;
using ArtDash.Application.Features.Payments;
using ArtDash.Shared.Contracts.Payments;

namespace ArtDash.UI.Controllers;

/// <summary>Online payments (Mercado Pago Checkout Transparente) for the customer's own orders, or by staff.</summary>
[Route(ApiRoutes.Payments)]
[Authorize]
public sealed class PaymentsController(ISender sender) : ApiControllerBase
{
    /// <summary>Creates the payment from the Payment Brick submission (card token or PIX).</summary>
    [HttpPost]
    public Task<OnlinePaymentResultDto> Create(CreateOnlinePaymentRequest request, CancellationToken cancellationToken) =>
        sender.Send(new CreateOnlinePaymentCommand(request), cancellationToken);

    /// <summary>Re-reads a pending payment from the gateway (polled by the payment page while PIX is open).</summary>
    [HttpPost("{transactionId:guid}/sync")]
    public Task<OnlinePaymentResultDto> Sync(Guid transactionId, CancellationToken cancellationToken) =>
        sender.Send(new SyncPaymentCommand(transactionId), cancellationToken);
}

/// <summary>
/// Gateway notifications. The tenant comes from the URL (<c>{tenantSlug}</c>, read by the tenant
/// middleware); the payload is only a hint — the payment is always re-read from the gateway.
/// </summary>
[Route(ApiRoutes.Webhooks)]
[AllowAnonymous]
public sealed class WebhooksController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Mercado Pago Webhooks (<c>?data.id=…&amp;type=payment</c>, JSON body) and legacy IPN
    /// (<c>?id=…&amp;topic=payment</c>). Answers 200 once handled, 403 for a bad signature, and 409
    /// when the payment cannot be read yet (Mercado Pago retries).
    /// </summary>
    [HttpPost("mercadopago/{tenantSlug}")]
    public async Task<IActionResult> MercadoPago(string tenantSlug, CancellationToken cancellationToken)
    {
        var (bodyType, bodyDataId) = await ReadBodyAsync(cancellationToken);

        var query = Request.Query;
        var type = FirstNonEmpty(query["type"], query["topic"], bodyType);
        var dataId = FirstNonEmpty(query["data.id"], query["id"], bodyDataId);

        await sender.Send(
            new ProcessMercadoPagoNotificationCommand(
                type,
                dataId,
                Request.Headers["x-signature"].ToString(),
                Request.Headers["x-request-id"].ToString()),
            cancellationToken);

        return Ok();
    }

    private async Task<(string? Type, string? DataId)> ReadBodyAsync(CancellationToken cancellationToken)
    {
        if (Request.ContentLength is 0 || Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return (null, null);
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            var root = document.RootElement;

            var type = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;

            string? dataId = null;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("id", out var idElement))
            {
                dataId = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : idElement.ToString();
            }

            return (type, dataId);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
