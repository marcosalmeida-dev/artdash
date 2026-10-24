using ArtDash.Application.Features.Branding;
using ArtDash.Shared.Contracts.Catalog;

namespace ArtDash.Application.Features.Catalog;

// ---------------------------------------------------------------- Queries

public sealed record SearchProductsQuery(ProductQuery Filter) : IQuery<PagedResult<ProductDto>>;

internal sealed class SearchProductsQueryHandler(IApplicationDbContext db)
    : IQueryHandler<SearchProductsQuery, PagedResult<ProductDto>>
{
    public async Task<PagedResult<ProductDto>> Handle(SearchProductsQuery query, CancellationToken cancellationToken)
    {
        var filter = query.Filter;
        var term = filter.Search.ToSearchTerm();

        return await db.Products.AsNoTracking()
            .WhereIf(term is not null, p => p.Name.ToLower().Contains(term!) || (p.Sku != null && p.Sku.ToLower().Contains(term!)))
            .WhereIf(filter.CategoryId is not null, p => p.CategoryId == filter.CategoryId)
            .WhereIf(filter.IsActive is not null, p => p.IsActive == filter.IsActive)
            .OrderBy(p => p.Category.SortOrder).ThenBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(ProductProjections.ToDto)
            .ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }
}

public sealed record GetProductQuery(Guid Id) : IQuery<ProductDto>;

internal sealed class GetProductQueryHandler(IApplicationDbContext db) : IQueryHandler<GetProductQuery, ProductDto>
{
    public async Task<ProductDto> Handle(GetProductQuery query, CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking()
            .Where(p => p.Id == query.Id)
            .Select(ProductProjections.ToDto)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Produto", query.Id);
}

/// <summary>Lightweight product search for the back-office order screen.</summary>
public sealed record LookupProductsQuery(string? Term) : IQuery<IReadOnlyList<ProductDto>>;

internal sealed class LookupProductsQueryHandler(IApplicationDbContext db)
    : IQueryHandler<LookupProductsQuery, IReadOnlyList<ProductDto>>
{
    public async Task<IReadOnlyList<ProductDto>> Handle(LookupProductsQuery query, CancellationToken cancellationToken)
    {
        var term = query.Term.ToSearchTerm();
        return await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .WhereIf(term is not null, p => p.Name.ToLower().Contains(term!) || (p.Sku != null && p.Sku.ToLower().Contains(term!)))
            .OrderBy(p => p.Name)
            .Take(20)
            .Select(ProductProjections.ToDto)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Active catalog grouped by category, for the storefront (anonymous).</summary>
public sealed record GetStorefrontCatalogQuery : IQuery<StorefrontCatalogDto>;

internal sealed class GetStorefrontCatalogQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetStorefrontCatalogQuery, StorefrontCatalogDto>
{
    public async Task<StorefrontCatalogDto> Handle(GetStorefrontCatalogQuery query, CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.Category.IsActive)
            .OrderBy(p => p.Category.SortOrder).ThenBy(p => p.Category.Name)
            .ThenBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(p => new
            {
                p.CategoryId,
                CategoryName = p.Category.Name,
                CategorySort = p.Category.SortOrder,
                Product = new StorefrontProductDto(p.Id, p.Name, p.Description, p.Price, p.CompareAtPrice, p.Unit, p.ImageUrl, p.IsFeatured)
            })
            .ToListAsync(cancellationToken);

        var categories = products
            .GroupBy(p => new { p.CategoryId, p.CategoryName, p.CategorySort })
            .OrderBy(g => g.Key.CategorySort).ThenBy(g => g.Key.CategoryName)
            .Select(g => new StorefrontCategoryDto(g.Key.CategoryId, g.Key.CategoryName, g.Select(x => x.Product).ToList()))
            .ToList();

        return new StorefrontCatalogDto(categories);
    }
}

// ---------------------------------------------------------------- Commands

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithName("Nome").MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Sku).MaximumLength(50);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).WithName("Preço");
        RuleFor(x => x.CompareAtPrice).GreaterThan(x => x.Price)
            .When(x => x.CompareAtPrice.HasValue)
            .WithMessage("O preço \"de\" deve ser maior que o preço de venda.");
        RuleFor(x => x.WeightKg).GreaterThan(0).When(x => x.WeightKg.HasValue);
        RuleFor(x => x.CategoryId).NotNull().WithMessage("Selecione a categoria.");
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x.ImageUrl).MaximumLength(500);
    }
}

public sealed record CreateProductCommand(SaveProductRequest Request) : ICommand<Guid>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator() => RuleFor(x => x.Request).SetValidator(new SaveProductRequestValidator());
}

internal sealed class CreateProductCommandHandler(IApplicationDbContext db) : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        await ProductRules.ValidateReferencesAsync(db, command.Request, null, cancellationToken);

        var product = new Product();
        ProductRules.Apply(product, command.Request);
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return product.Id;
    }
}

public sealed record UpdateProductCommand(Guid Id, SaveProductRequest Request) : ICommand;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator() => RuleFor(x => x.Request).SetValidator(new SaveProductRequestValidator());
}

internal sealed class UpdateProductCommandHandler(IApplicationDbContext db, IFileStorage storage) : ICommandHandler<UpdateProductCommand>
{
    public async Task<Unit> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Produto", command.Id);

        await ProductRules.ValidateReferencesAsync(db, command.Request, command.Id, cancellationToken);

        var previousImage = product.ImageUrl;
        ProductRules.Apply(product, command.Request);
        await db.SaveChangesAsync(cancellationToken);

        if (previousImage is not null && previousImage != product.ImageUrl)
        {
            await storage.DeleteAsync(previousImage, cancellationToken);
        }

        return Unit.Value;
    }
}

public sealed record DeleteProductCommand(Guid Id) : ICommand;

internal sealed class DeleteProductCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<DeleteProductCommand>
{
    public async Task<Unit> Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Produto", command.Id);

        // Soft delete: past orders keep pointing at it.
        product.IsActive = false;
        product.MarkDeleted(clock.GetUtcNow().UtcDateTime, currentUser.AuditName());
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Uploads a product picture and returns its URL (the form saves it with the product).</summary>
public sealed record UploadProductImageCommand(Stream Content, string FileName, string ContentType) : ICommand<UploadResultDto>;

public sealed class UploadProductImageCommandValidator : AbstractValidator<UploadProductImageCommand>
{
    public UploadProductImageCommandValidator()
    {
        RuleFor(x => x.ContentType).Must(UploadRules.IsAllowedImage).WithMessage("Envie uma imagem PNG, JPG, WEBP ou SVG.");
    }
}

internal sealed class UploadProductImageCommandHandler(IFileStorage storage) : ICommandHandler<UploadProductImageCommand, UploadResultDto>
{
    public async Task<UploadResultDto> Handle(UploadProductImageCommand command, CancellationToken cancellationToken) =>
        new(await storage.SaveAsync(command.Content, command.FileName, command.ContentType, "products", cancellationToken));
}

internal static class ProductRules
{
    public static void Apply(Product product, SaveProductRequest request)
    {
        product.Name = request.Name.Trim();
        product.Description = request.Description.NullIfWhiteSpace();
        product.Sku = request.Sku.NullIfWhiteSpace()?.ToUpperInvariant();
        product.Price = decimal.Round(request.Price, 2);
        product.CompareAtPrice = request.CompareAtPrice is > 0 ? decimal.Round(request.CompareAtPrice.Value, 2) : null;
        product.Unit = request.Unit;
        product.WeightKg = request.WeightKg;
        product.CategoryId = request.CategoryId!.Value;
        product.ImageUrl = request.ImageUrl.NullIfWhiteSpace();
        product.IsActive = request.IsActive;
        product.IsFeatured = request.IsFeatured;
        product.SortOrder = request.SortOrder;
    }

    public static async Task ValidateReferencesAsync(IApplicationDbContext db, SaveProductRequest request, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            throw new RequestValidationException(nameof(SaveProductRequest.CategoryId), "Categoria não encontrada.");
        }

        var sku = request.Sku.NullIfWhiteSpace()?.ToUpperInvariant();
        if (sku is not null &&
            await db.Products.AnyAsync(p => p.Sku == sku && (exceptId == null || p.Id != exceptId), cancellationToken))
        {
            throw new RequestValidationException(nameof(SaveProductRequest.Sku), "Já existe um produto com este código (SKU).");
        }
    }
}

internal static class ProductProjections
{
    public static readonly System.Linq.Expressions.Expression<Func<Product, ProductDto>> ToDto = p => new ProductDto
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Sku = p.Sku,
        Price = p.Price,
        CompareAtPrice = p.CompareAtPrice,
        Unit = p.Unit,
        WeightKg = p.WeightKg,
        CategoryId = p.CategoryId,
        CategoryName = p.Category.Name,
        ImageUrl = p.ImageUrl,
        IsActive = p.IsActive,
        IsFeatured = p.IsFeatured,
        SortOrder = p.SortOrder,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };
}
