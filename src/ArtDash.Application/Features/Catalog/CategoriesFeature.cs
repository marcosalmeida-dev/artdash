using ArtDash.Shared.Contracts.Catalog;

namespace ArtDash.Application.Features.Catalog;

public sealed record GetCategoriesQuery(bool IncludeInactive = true) : IQuery<IReadOnlyList<CategoryDto>>;

internal sealed class GetCategoriesQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<IReadOnlyList<CategoryDto>> Handle(GetCategoriesQuery query, CancellationToken cancellationToken) =>
        await db.Categories.AsNoTracking()
            .WhereIf(!query.IncludeInactive, c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id,
                c.Name,
                c.Description,
                c.SortOrder,
                c.IsActive,
                db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(cancellationToken);
}

public sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithName("Nome").MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed record CreateCategoryCommand(SaveCategoryRequest Request) : ICommand<Guid>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator() => RuleFor(x => x.Request).SetValidator(new SaveCategoryRequestValidator());
}

internal sealed class CreateCategoryCommandHandler(IApplicationDbContext db) : ICommandHandler<CreateCategoryCommand, Guid>
{
    public async Task<Guid> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        await CategoryRules.EnsureUniqueNameAsync(db, command.Request.Name, null, cancellationToken);

        var category = new Category();
        CategoryRules.Apply(category, command.Request);
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return category.Id;
    }
}

public sealed record UpdateCategoryCommand(Guid Id, SaveCategoryRequest Request) : ICommand;

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator() => RuleFor(x => x.Request).SetValidator(new SaveCategoryRequestValidator());
}

internal sealed class UpdateCategoryCommandHandler(IApplicationDbContext db) : ICommandHandler<UpdateCategoryCommand>
{
    public async Task<Unit> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Categoria", command.Id);

        await CategoryRules.EnsureUniqueNameAsync(db, command.Request.Name, command.Id, cancellationToken);
        CategoryRules.Apply(category, command.Request);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record DeleteCategoryCommand(Guid Id) : ICommand;

internal sealed class DeleteCategoryCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<DeleteCategoryCommand>
{
    public async Task<Unit> Handle(DeleteCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Categoria", command.Id);

        if (await db.Products.AnyAsync(p => p.CategoryId == command.Id, cancellationToken))
        {
            throw new DomainException("A categoria tem produtos. Mova ou exclua os produtos antes.");
        }

        category.MarkDeleted(clock.GetUtcNow().UtcDateTime, currentUser.AuditName());
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal static class CategoryRules
{
    public static void Apply(Category category, SaveCategoryRequest request)
    {
        category.Name = request.Name.Trim();
        category.Description = request.Description.NullIfWhiteSpace();
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
    }

    public static async Task EnsureUniqueNameAsync(IApplicationDbContext db, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToLower();
        var exists = await db.Categories.AnyAsync(
            c => c.Name.ToLower() == normalized && (exceptId == null || c.Id != exceptId),
            cancellationToken);

        if (exists)
        {
            throw new RequestValidationException(nameof(SaveCategoryRequest.Name), "Já existe uma categoria com este nome.");
        }
    }
}
