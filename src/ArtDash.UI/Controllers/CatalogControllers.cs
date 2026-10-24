using ArtDash.Application.Features.Catalog;
using ArtDash.Shared.Contracts.Catalog;

namespace ArtDash.UI.Controllers;

[Route(ApiRoutes.Categories)]
[Authorize(Roles = AppRoles.Staff)]
public sealed class CategoriesController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> List([FromQuery] bool includeInactive = true, CancellationToken cancellationToken = default) =>
        sender.Send(new GetCategoriesQuery(includeInactive), cancellationToken);

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(SaveCategoryRequest request, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new CreateCategoryCommand(request), cancellationToken);
        return CreatedWithId(nameof(List), id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new UpdateCategoryCommand(id, request), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteCategoryCommand(id), cancellationToken);
        return NoContent();
    }
}

[Route(ApiRoutes.Products)]
[Authorize(Roles = AppRoles.Staff)]
public sealed class ProductsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<ProductDto>> Search([FromQuery] ProductQuery filter, CancellationToken cancellationToken) =>
        sender.Send(new SearchProductsQuery(filter), cancellationToken);

    /// <summary>Active products for the back-office order form (autocomplete).</summary>
    [HttpGet("lookup")]
    public Task<IReadOnlyList<ProductDto>> Lookup([FromQuery] string? term, CancellationToken cancellationToken) =>
        sender.Send(new LookupProductsQuery(term), cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<ProductDto> Get(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetProductQuery(id), cancellationToken);

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(SaveProductRequest request, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new CreateProductCommand(request), cancellationToken);
        return CreatedWithId(nameof(Get), id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveProductRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new UpdateProductCommand(id, request), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteProductCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Uploads a product picture and returns its URL (saved on the product with the form).</summary>
    [HttpPost("images")]
    [RequestSizeLimit(ArtDash.Application.Features.Branding.UploadRules.MaxImageBytes + 64 * 1024)]
    public async Task<ActionResult<UploadResultDto>> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        UploadGuard.EnsureImage(file);

        await using var stream = file.OpenReadStream();
        return await sender.Send(new UploadProductImageCommand(stream, file.FileName, file.ContentType), cancellationToken);
    }
}
