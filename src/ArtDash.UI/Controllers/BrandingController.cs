using ArtDash.Application.Features.Branding;
using ArtDash.Shared.Contracts.Branding;

namespace ArtDash.UI.Controllers;

/// <summary>The tenant's white-label identity: names, logos, palette and typography.</summary>
[Route(ApiRoutes.Branding)]
public sealed class BrandingController(ISender sender) : ApiControllerBase
{
    /// <summary>Anonymous: the storefront and the login page need the brand before anyone signs in.</summary>
    [HttpGet]
    [AllowAnonymous]
    public Task<BrandingDto> Get(CancellationToken cancellationToken) =>
        sender.Send(new GetBrandingQuery(), cancellationToken);

    [HttpPut]
    [Authorize(Roles = AppRoles.Admin)]
    public Task<BrandingDto> Update(UpdateBrandingRequest request, CancellationToken cancellationToken) =>
        sender.Send(new UpdateBrandingCommand(request), cancellationToken);

    /// <summary>Uploads a logo (<c>logo</c>, <c>logo-dark</c>) or the square icon (<c>icon</c>).</summary>
    [HttpPost("assets/{kind}")]
    [Authorize(Roles = AppRoles.Admin)]
    [RequestSizeLimit(UploadRules.MaxImageBytes + 64 * 1024)]
    public async Task<ActionResult<UploadResultDto>> UploadAsset(string kind, IFormFile file, CancellationToken cancellationToken)
    {
        UploadGuard.EnsureImage(file);

        await using var stream = file.OpenReadStream();
        return await sender.Send(new UploadBrandAssetCommand(kind, stream, file.FileName, file.ContentType), cancellationToken);
    }
}

/// <summary>Request-level checks shared by the upload endpoints (the content type is validated by the commands).</summary>
internal static class UploadGuard
{
    public static void EnsureImage(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            throw new RequestValidationException("file", "Selecione uma imagem.");
        }

        if (file.Length > UploadRules.MaxImageBytes)
        {
            throw new RequestValidationException("file", $"A imagem deve ter no máximo {UploadRules.MaxImageBytes / 1024 / 1024} MB.");
        }
    }
}
