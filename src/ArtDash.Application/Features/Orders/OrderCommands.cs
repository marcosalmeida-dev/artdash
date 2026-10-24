using ArtDash.Application.Features.Customers;
using ArtDash.Shared.Contracts.Orders;

namespace ArtDash.Application.Features.Orders;

// ---------------------------------------------------------------- Place

public sealed record PlaceOrderCommand(PlaceOrderRequest Request, OrderChannel Channel) : ICommand<PlaceOrderResultDto>;

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.Request.Items).NotEmpty().WithMessage("Adicione pelo menos um item ao pedido.");
        RuleForEach(x => x.Request.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0).LessThanOrEqualTo(100_000).WithName("Quantidade");
        });
        RuleFor(x => x.Request.DeliveryAddress).SetValidator(new AddressDtoValidator());
        RuleFor(x => x.Request.PaymentTiming).IsInEnum();
        RuleFor(x => x.Request.PaymentMethod).IsInEnum().When(x => x.Request.PaymentMethod.HasValue);
        RuleFor(x => x.Request.Notes).MaximumLength(1000);
        RuleFor(x => x.Request.CustomerId)
            .NotEmpty().When(x => x.Channel == OrderChannel.Backoffice)
            .WithMessage("Selecione o cliente.");
        RuleFor(x => x.Request.DeliveryFeeOverride).GreaterThanOrEqualTo(0).When(x => x.Request.DeliveryFeeOverride.HasValue);
        RuleFor(x => x.Request.Discount).GreaterThanOrEqualTo(0).When(x => x.Request.Discount.HasValue);
    }
}

internal sealed class PlaceOrderCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    ISequenceGenerator sequences,
    TimeProvider clock)
    : ICommandHandler<PlaceOrderCommand, PlaceOrderResultDto>
{
    public async Task<PlaceOrderResultDto> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var isBackoffice = command.Channel == OrderChannel.Backoffice;

        if (isBackoffice && !currentUser.IsStaff())
        {
            throw new ForbiddenAccessException();
        }

        var tenantId = currentTenant.RequiredId();
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);

        var customer = isBackoffice
            ? await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
              ?? throw new NotFoundException("Cliente", request.CustomerId ?? Guid.Empty)
            : await CustomerRules.GetForUserAsync(db, currentUser.RequiredUserId(), tracking: false, cancellationToken);

        EnsurePaymentTimingAllowed(request.PaymentTiming, tenant, customer, isBackoffice);

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        if (productIds.Any(id => !products.TryGetValue(id, out var product) || !product.IsActive))
        {
            throw new DomainException("Um ou mais produtos não estão mais disponíveis. Atualize o carrinho.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var by = currentUser.AuditName();

        // Numbers are handed out first: the generator saves, so nothing else may be pending yet.
        var number = await sequences.NextAsync(SequenceNames.Order, cancellationToken);

        var order = Order.Create(
            number.ToString("D6"),
            TrackingCodeGenerator.New(),
            customer,
            command.Channel,
            request.PaymentTiming,
            request.PaymentMethod,
            request.DeliveryAddress.ToDomain(),
            request.Notes,
            now,
            by);

        foreach (var item in request.Items)
        {
            var product = products[item.ProductId];
            order.AddItem(product.Id, product.Name, product.Sku, product.Unit, product.Price, item.Quantity);
        }

        var fee = isBackoffice && request.DeliveryFeeOverride is { } overrideFee
            ? overrideFee
            : tenant.Store.DeliveryFeeFor(order.Subtotal);
        order.SetDeliveryFee(fee);

        if (isBackoffice && request.Discount is > 0)
        {
            order.ApplyDiscount(request.Discount.Value);
        }

        if (!isBackoffice && tenant.Store.MinimumOrderAmount is > 0 && order.Subtotal < tenant.Store.MinimumOrderAmount.Value)
        {
            throw new DomainException($"O pedido mínimo é de {tenant.Store.MinimumOrderAmount.Value:C}.");
        }

        if (order.PaymentTiming == PaymentTiming.Invoiced)
        {
            var openBalance = await CustomerBalance.OpenInvoicedBalanceAsync(db, customer.Id, cancellationToken);
            customer.EnsureCanInvoice(order.Total, openBalance);

            var termDays = customer.PaymentTermDays > 0 ? customer.PaymentTermDays : tenant.Payments.DefaultPaymentTermDays;
            var today = TenantTime.Today(now, TenantClock.Resolve(tenant.Store.TimeZoneId));
            order.SetDueDate(today.AddDays(termDays));
        }

        order.EnsureReadyToPlace();

        // Staff-entered post-paid orders were agreed with the customer already.
        if (isBackoffice && order.PaymentTiming != PaymentTiming.Prepaid)
        {
            order.Confirm(now, by);
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        return new PlaceOrderResultDto(
            order.Id,
            order.Number,
            order.TrackingCode,
            order.PaymentTiming == PaymentTiming.Prepaid && order.PaymentStatus != PaymentStatus.Paid);
    }

    private static void EnsurePaymentTimingAllowed(PaymentTiming timing, Tenant tenant, Customer customer, bool isBackoffice)
    {
        switch (timing)
        {
            case PaymentTiming.Prepaid when !isBackoffice && !tenant.Payments.IsOnlinePaymentConfigured:
                throw new DomainException("O pagamento online não está disponível nesta loja.");

            case PaymentTiming.OnDelivery when !isBackoffice && !tenant.Payments.PayOnDeliveryEnabled:
                throw new DomainException("Esta loja não aceita pagamento na entrega.");

            case PaymentTiming.Invoiced when !isBackoffice && !tenant.Payments.InvoicingEnabled:
                throw new DomainException("Esta loja não trabalha com pedidos faturados.");

            case PaymentTiming.Invoiced when !customer.AllowInvoicing:
                throw new DomainException("Este cliente não está habilitado para pedidos faturados.");
        }
    }
}

// ---------------------------------------------------------------- Status changes (staff)

public sealed record ChangeOrderStatusCommand(Guid Id, OrderStatus Status, string? Note) : ICommand;

public sealed class ChangeOrderStatusCommandValidator : AbstractValidator<ChangeOrderStatusCommand>
{
    public ChangeOrderStatusCommandValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

internal sealed class ChangeOrderStatusCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<ChangeOrderStatusCommand>
{
    public async Task<Unit> Handle(ChangeOrderStatusCommand command, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Pedido", command.Id);

        order.MoveTo(command.Status, clock.GetUtcNow().UtcDateTime, currentUser.AuditName(), command.Note.NullIfWhiteSpace());
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record CancelOrderCommand(Guid Id, string Reason) : ICommand;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator() => RuleFor(x => x.Reason).NotEmpty().WithName("Motivo").MaximumLength(500);
}

internal sealed class CancelOrderCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<CancelOrderCommand>
{
    public async Task<Unit> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Pedido", command.Id);

        order.Cancel(command.Reason, clock.GetUtcNow().UtcDateTime, currentUser.AuditName());
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>A customer may cancel their own order while it is still pending and unpaid.</summary>
public sealed record CancelMyOrderCommand(Guid Id) : ICommand;

internal sealed class CancelMyOrderCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<CancelMyOrderCommand>
{
    public async Task<Unit> Handle(CancelMyOrderCommand command, CancellationToken cancellationToken)
    {
        var customer = await CustomerRules.GetForUserAsync(db, currentUser.RequiredUserId(), tracking: false, cancellationToken);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == command.Id && o.CustomerId == customer.Id, cancellationToken)
            ?? throw new NotFoundException("Pedido", command.Id);

        if (order.Status != OrderStatus.Pending || order.AmountPaid > 0)
        {
            throw new DomainException("Este pedido já está em andamento. Fale com a loja para cancelar.");
        }

        order.Cancel("Cancelado pelo cliente", clock.GetUtcNow().UtcDateTime, currentUser.AuditName());
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------- Manual payment (staff)

public sealed record RegisterOrderPaymentCommand(Guid Id, RegisterPaymentRequest Request) : ICommand;

public sealed class RegisterOrderPaymentCommandValidator : AbstractValidator<RegisterOrderPaymentCommand>
{
    public RegisterOrderPaymentCommandValidator()
    {
        RuleFor(x => x.Request.Method).IsInEnum();
        RuleFor(x => x.Request.Amount).GreaterThan(0).WithName("Valor");
        RuleFor(x => x.Request.Reference).MaximumLength(200);
    }
}

internal sealed class RegisterOrderPaymentCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    TimeProvider clock)
    : ICommandHandler<RegisterOrderPaymentCommand>
{
    public async Task<Unit> Handle(RegisterOrderPaymentCommand command, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Pedido", command.Id);

        var request = command.Request;
        if (request.Amount > order.OutstandingAmount + 0.009m)
        {
            throw new DomainException($"O valor informado é maior que o saldo em aberto ({order.OutstandingAmount:C}).");
        }

        var now = clock.GetUtcNow().UtcDateTime;

        // The back office sends the store's wall-clock time (a date picker); UTC values pass through.
        var paidAt = now;
        if (request.PaidAt is { } date)
        {
            paidAt = date.Kind == DateTimeKind.Utc
                ? date
                : TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(date, DateTimeKind.Unspecified),
                    await TenantTime.GetZoneAsync(db, currentTenant, cancellationToken));
        }

        order.RegisterPayment(request.Method, request.Amount, paidAt > now ? now : paidAt, request.Reference.NullIfWhiteSpace(), currentUser.AuditName());
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
