using ArtDash.Application.Features.Catalog;
using ArtDash.Application.Features.Settings;
using ArtDash.Shared.Contracts.Catalog;
using ArtDash.Shared.Contracts.Settings;

namespace ArtDash.UI.Controllers;

/// <summary>Public storefront data: store information and the catalog. No sign-in required.</summary>
[Route(ApiRoutes.Store)]
[AllowAnonymous]
public sealed class StoreController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<StoreInfoDto> Get(CancellationToken cancellationToken) =>
        sender.Send(new GetStoreInfoQuery(), cancellationToken);

    [HttpGet("catalog")]
    public Task<StorefrontCatalogDto> Catalog(CancellationToken cancellationToken) =>
        sender.Send(new GetStorefrontCatalogQuery(), cancellationToken);
}

/// <summary>Store configuration (admin): contact data, delivery pricing and payment options.</summary>
[Route(ApiRoutes.Settings)]
[Authorize(Roles = AppRoles.Admin)]
public sealed class SettingsController(ISender sender) : ApiControllerBase
{
    [HttpGet("store")]
    public Task<StoreSettingsDto> GetStore(CancellationToken cancellationToken) =>
        sender.Send(new GetStoreSettingsQuery(), cancellationToken);

    [HttpPut("store")]
    public async Task<IActionResult> UpdateStore(StoreSettingsDto request, CancellationToken cancellationToken)
    {
        await sender.Send(new UpdateStoreSettingsCommand(request), cancellationToken);
        return NoContent();
    }

    /// <summary>Secrets come back empty; <c>hasAccessToken</c> / <c>hasWebhookSecret</c> say whether one is stored.</summary>
    [HttpGet("payments")]
    public Task<PaymentSettingsDto> GetPayments(CancellationToken cancellationToken) =>
        sender.Send(new GetPaymentSettingsQuery(), cancellationToken);

    [HttpPut("payments")]
    public async Task<IActionResult> UpdatePayments(PaymentSettingsDto request, CancellationToken cancellationToken)
    {
        await sender.Send(new UpdatePaymentSettingsCommand(request), cancellationToken);
        return NoContent();
    }
}
