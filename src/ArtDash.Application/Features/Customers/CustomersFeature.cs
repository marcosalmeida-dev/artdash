using ArtDash.Shared.Contracts.Customers;

namespace ArtDash.Application.Features.Customers;

// ---------------------------------------------------------------- Queries (staff)

public sealed record SearchCustomersQuery(CustomerQuery Filter) : IQuery<PagedResult<CustomerListItemDto>>;

internal sealed class SearchCustomersQueryHandler(IApplicationDbContext db)
    : IQueryHandler<SearchCustomersQuery, PagedResult<CustomerListItemDto>>
{
    public async Task<PagedResult<CustomerListItemDto>> Handle(SearchCustomersQuery query, CancellationToken cancellationToken)
    {
        var filter = query.Filter;
        var term = filter.Search.ToSearchTerm();
        var digits = filter.Search.Digits();

        return await db.Customers.AsNoTracking()
            .WhereIf(term is not null, c =>
                c.Name.ToLower().Contains(term!) ||
                (c.TradeName != null && c.TradeName.ToLower().Contains(term!)) ||
                (c.Email != null && c.Email.ToLower().Contains(term!)) ||
                (digits.Length >= 3 && ((c.Document != null && c.Document.Contains(digits)) || (c.Phone != null && c.Phone.Contains(digits)))))
            .WhereIf(filter.IsActive is not null, c => c.IsActive == filter.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CustomerListItemDto(
                c.Id, c.Name, c.Type, c.Document, c.Email, c.Phone, c.Address.City, c.AllowInvoicing, c.IsActive, c.UserId != null))
            .ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }
}

public sealed record LookupCustomersQuery(string? Term) : IQuery<IReadOnlyList<LookupDto>>;

internal sealed class LookupCustomersQueryHandler(IApplicationDbContext db)
    : IQueryHandler<LookupCustomersQuery, IReadOnlyList<LookupDto>>
{
    public async Task<IReadOnlyList<LookupDto>> Handle(LookupCustomersQuery query, CancellationToken cancellationToken)
    {
        var term = query.Term.ToSearchTerm();
        var digits = query.Term.Digits();

        return await db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .WhereIf(term is not null, c =>
                c.Name.ToLower().Contains(term!) ||
                (c.TradeName != null && c.TradeName.ToLower().Contains(term!)) ||
                (digits.Length >= 3 && ((c.Document != null && c.Document.Contains(digits)) || (c.Phone != null && c.Phone.Contains(digits)))))
            .OrderBy(c => c.Name)
            .Take(20)
            .Select(c => new LookupDto(c.Id, c.Name, c.Address.District + " - " + c.Address.City))
            .ToListAsync(cancellationToken);
    }
}

public sealed record GetCustomerQuery(Guid Id) : IQuery<CustomerDto>;

internal sealed class GetCustomerQueryHandler(IApplicationDbContext db) : IQueryHandler<GetCustomerQuery, CustomerDto>
{
    public async Task<CustomerDto> Handle(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken)
            ?? throw new NotFoundException("Cliente", query.Id);

        var openBalance = await CustomerBalance.OpenInvoicedBalanceAsync(db, customer.Id, cancellationToken);
        return CustomerMapper.ToDto(customer, openBalance);
    }
}

// ---------------------------------------------------------------- Commands (staff)

public sealed class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Name).NotEmpty().WithName("Nome").MaximumLength(150);
        RuleFor(x => x.TradeName).MaximumLength(150);
        RuleFor(x => x.Document).BrazilianDocument();
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).MaximumLength(150);
        RuleFor(x => x.Phone).Phone();
        RuleFor(x => x.Address).SetValidator(new AddressDtoValidator());
        RuleFor(x => x.PaymentTermDays).InclusiveBetween(0, 180);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0).When(x => x.CreditLimit.HasValue);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed record CreateCustomerCommand(SaveCustomerRequest Request) : ICommand<Guid>;

public sealed class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator() => RuleFor(x => x.Request).SetValidator(new SaveCustomerRequestValidator());
}

internal sealed class CreateCustomerCommandHandler(IApplicationDbContext db) : ICommandHandler<CreateCustomerCommand, Guid>
{
    public async Task<Guid> Handle(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        await CustomerRules.EnsureUniqueDocumentAsync(db, command.Request.Document, null, cancellationToken);

        var customer = new Customer();
        CustomerRules.Apply(customer, command.Request);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return customer.Id;
    }
}

public sealed record UpdateCustomerCommand(Guid Id, SaveCustomerRequest Request) : ICommand;

public sealed class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator() => RuleFor(x => x.Request).SetValidator(new SaveCustomerRequestValidator());
}

internal sealed class UpdateCustomerCommandHandler(IApplicationDbContext db) : ICommandHandler<UpdateCustomerCommand>
{
    public async Task<Unit> Handle(UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Cliente", command.Id);

        await CustomerRules.EnsureUniqueDocumentAsync(db, command.Request.Document, command.Id, cancellationToken);
        CustomerRules.Apply(customer, command.Request);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record DeleteCustomerCommand(Guid Id) : ICommand;

internal sealed class DeleteCustomerCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<DeleteCustomerCommand>
{
    public async Task<Unit> Handle(DeleteCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Cliente", command.Id);

        var hasOpenOrders = await db.Orders.AnyAsync(
            o => o.CustomerId == command.Id && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled,
            cancellationToken);

        if (hasOpenOrders)
        {
            throw new DomainException("O cliente tem pedidos em andamento.");
        }

        customer.IsActive = false;
        customer.MarkDeleted(clock.GetUtcNow().UtcDateTime, currentUser.AuditName());
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------- Own profile (customer)

public sealed record GetMyProfileQuery : IQuery<MyProfileDto>;

internal sealed class GetMyProfileQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyProfileQuery, MyProfileDto>
{
    public async Task<MyProfileDto> Handle(GetMyProfileQuery query, CancellationToken cancellationToken)
    {
        var customer = await CustomerRules.GetForUserAsync(db, currentUser.RequiredUserId(), tracking: false, cancellationToken);

        return new MyProfileDto
        {
            CustomerId = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Phone = customer.Phone,
            Document = customer.Document,
            Address = customer.Address.ToDto(),
            AllowInvoicing = customer.AllowInvoicing,
            PaymentTermDays = customer.PaymentTermDays
        };
    }
}

public sealed record UpdateMyProfileCommand(UpdateMyProfileRequest Request) : ICommand;

public sealed class UpdateMyProfileCommandValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileCommandValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().WithName("Nome").MaximumLength(150);
        RuleFor(x => x.Request.Phone).Phone();
        RuleFor(x => x.Request.Document).BrazilianDocument();
        RuleFor(x => x.Request.Address).SetValidator(new AddressDtoValidator()).When(x => !x.Request.Address.IsEmpty);
    }
}

internal sealed class UpdateMyProfileCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateMyProfileCommand>
{
    public async Task<Unit> Handle(UpdateMyProfileCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId();
        var customer = await CustomerRules.GetForUserAsync(db, userId, tracking: true, cancellationToken);
        var request = command.Request;

        await CustomerRules.EnsureUniqueDocumentAsync(db, request.Document, customer.Id, cancellationToken);

        customer.Name = request.Name.Trim();
        customer.Phone = request.Phone.NullIfWhiteSpace();
        customer.Document = Customer.NormalizeDocument(request.Document);
        if (!request.Address.IsEmpty)
        {
            customer.Address = request.Address.ToDomain();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is not null)
        {
            user.FullName = customer.Name;
            user.PhoneNumber = customer.Phone;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------- Shared helpers

internal static class CustomerRules
{
    public static void Apply(Customer customer, SaveCustomerRequest request)
    {
        customer.Type = request.Type;
        customer.Name = request.Name.Trim();
        customer.TradeName = request.TradeName.NullIfWhiteSpace();
        customer.Document = Customer.NormalizeDocument(request.Document);
        customer.Email = request.Email.NullIfWhiteSpace()?.ToLowerInvariant();
        customer.Phone = request.Phone.NullIfWhiteSpace();
        customer.Address = request.Address.ToDomain();
        customer.AllowInvoicing = request.AllowInvoicing;
        customer.PaymentTermDays = request.PaymentTermDays;
        customer.CreditLimit = request.CreditLimit is > 0 ? request.CreditLimit : null;
        customer.Notes = request.Notes.NullIfWhiteSpace();
        customer.IsActive = request.IsActive;
    }

    public static async Task EnsureUniqueDocumentAsync(IApplicationDbContext db, string? document, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = Customer.NormalizeDocument(document);
        if (normalized is null)
        {
            return;
        }

        var exists = await db.Customers.AnyAsync(
            c => c.Document == normalized && (exceptId == null || c.Id != exceptId),
            cancellationToken);

        if (exists)
        {
            throw new RequestValidationException("Document", "Já existe um cliente com este CPF/CNPJ.");
        }
    }

    public static async Task<Customer> GetForUserAsync(IApplicationDbContext db, Guid userId, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? db.Customers : db.Customers.AsNoTracking();
        return await query.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken)
            ?? throw new DomainException("Sua conta não tem um cadastro de cliente. Fale com a loja.");
    }
}

internal static class CustomerBalance
{
    /// <summary>Amount still owed on invoiced orders that are not cancelled.</summary>
    public static async Task<decimal> OpenInvoicedBalanceAsync(IApplicationDbContext db, Guid customerId, CancellationToken cancellationToken) =>
        await db.Orders
            .Where(o => o.CustomerId == customerId
                        && o.PaymentTiming == PaymentTiming.Invoiced
                        && o.Status != OrderStatus.Cancelled
                        && o.PaymentStatus != PaymentStatus.Paid)
            .SumAsync(o => (decimal?)(o.Total - o.AmountPaid), cancellationToken) ?? 0m;
}

internal static class CustomerMapper
{
    public static CustomerDto ToDto(Customer customer, decimal openBalance) => new()
    {
        Id = customer.Id,
        Type = customer.Type,
        Name = customer.Name,
        TradeName = customer.TradeName,
        Document = customer.Document,
        Email = customer.Email,
        Phone = customer.Phone,
        Address = customer.Address.ToDto(),
        AllowInvoicing = customer.AllowInvoicing,
        PaymentTermDays = customer.PaymentTermDays,
        CreditLimit = customer.CreditLimit,
        Notes = customer.Notes,
        IsActive = customer.IsActive,
        UserId = customer.UserId,
        OpenBalance = openBalance
    };
}
