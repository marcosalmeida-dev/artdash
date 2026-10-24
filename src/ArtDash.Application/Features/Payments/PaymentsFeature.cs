using ArtDash.Application.Features.Customers;
using ArtDash.Shared.Contracts.Payments;
using Microsoft.Extensions.Logging;

namespace ArtDash.Application.Features.Payments;

// Mercado Pago Checkout Transparente: the Payment Brick tokenizes the card (or picks PIX) in the browser,
// the API creates the payment server-side, and the result arrives synchronously (cards) or later through
// polling and webhooks (PIX). Every path ends in PaymentSettlement, so an approval is applied exactly once.

public sealed record CreateOnlinePaymentCommand(CreateOnlinePaymentRequest Request) : ICommand<OnlinePaymentResultDto>;

public sealed class CreateOnlinePaymentCommandValidator : AbstractValidator<CreateOnlinePaymentCommand>
{
    public CreateOnlinePaymentCommandValidator()
    {
        RuleFor(x => x.Request.OrderId).NotEmpty();
        RuleFor(x => x.Request.PaymentMethodId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Token).NotEmpty()
            .When(x => !PaymentMethodMapper.IsPix(x.Request.PaymentMethodId, x.Request.PaymentTypeId))
            .WithMessage("Dados do cartão inválidos. Tente novamente.");
        RuleFor(x => x.Request.Installments).InclusiveBetween(1, 12).When(x => x.Request.Installments.HasValue);
        RuleFor(x => x.Request.PayerEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Request.PayerEmail));
    }
}

internal sealed class CreateOnlinePaymentCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IPaymentGateway gateway,
    ISecretProtector protector,
    IAppUrls urls,
    TimeProvider clock)
    : ICommandHandler<CreateOnlinePaymentCommand, OnlinePaymentResultDto>
{
    public async Task<OnlinePaymentResultDto> Handle(CreateOnlinePaymentCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var order = await PaymentAccess.LoadOrderAsync(db, currentUser, request.OrderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new DomainException("Este pedido foi cancelado.");
        }

        if (order.PaymentStatus == PaymentStatus.Paid || order.OutstandingAmount <= 0)
        {
            throw new DomainException("Este pedido já está pago.");
        }

        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == currentTenant.RequiredId(), cancellationToken);
        var settings = tenant.Payments;
        var accessToken = protector.Unprotect(settings.MercadoPagoAccessTokenProtected);

        if (!settings.IsOnlinePaymentConfigured || string.IsNullOrWhiteSpace(accessToken))
        {
            throw new DomainException("O pagamento online não está configurado nesta loja.");
        }

        var method = PaymentMethodMapper.FromBrick(request.PaymentMethodId, request.PaymentTypeId);
        if (!settings.IsOnlineMethodEnabled(method))
        {
            throw new DomainException("Esta forma de pagamento não está habilitada.");
        }

        var installments = method == PaymentMethod.CreditCard
            ? Math.Clamp(request.Installments ?? 1, 1, Math.Max(1, settings.MaxInstallments))
            : 1;

        var now = clock.GetUtcNow().UtcDateTime;
        var idempotencyKey = Guid.NewGuid().ToString("N");

        // Persist the attempt before calling out, so even a crash mid-call leaves a trace.
        var transaction = PaymentTransaction.Start(order.Id, method, order.OutstandingAmount, installments, idempotencyKey);
        db.PaymentTransactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);

        var gatewayRequest = new GatewayPaymentRequest(
            Amount: transaction.Amount,
            Description: $"Pedido {order.Number} - {tenant.Name}",
            PaymentMethodId: method == PaymentMethod.Pix ? "pix" : request.PaymentMethodId,
            Token: method == PaymentMethod.Pix ? null : request.Token,
            IssuerId: request.IssuerId,
            Installments: method == PaymentMethod.Pix ? null : installments,
            ExternalReference: order.Id.ToString(),
            NotificationUrl: urls.MercadoPagoWebhookUrl(tenant.Slug),
            PayerEmail: request.PayerEmail.NullIfWhiteSpace() ?? await PaymentAccess.CustomerEmailAsync(db, order.CustomerId, cancellationToken),
            PayerFirstName: request.PayerFirstName,
            PayerLastName: request.PayerLastName,
            IdentificationType: request.IdentificationType,
            IdentificationNumber: request.IdentificationNumber.Digits().NullIfWhiteSpace() ?? order.CustomerDocument,
            ExpiresAt: method == PaymentMethod.Pix ? now.AddMinutes(Math.Max(5, settings.PixExpirationMinutes)) : null);

        var result = await gateway.CreatePaymentAsync(gatewayRequest, accessToken, idempotencyKey, cancellationToken);

        if (!result.Success)
        {
            transaction.MarkFailed(result.ErrorMessage ?? "Falha ao comunicar com o gateway.", now);
        }
        else
        {
            PaymentSettlement.Apply(transaction, order, result, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return PaymentResultMapper.ToDto(transaction, order);
    }
}

/// <summary>Re-reads a pending transaction from the gateway (the payment page polls this while PIX is open).</summary>
public sealed record SyncPaymentCommand(Guid TransactionId) : ICommand<OnlinePaymentResultDto>;

internal sealed class SyncPaymentCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IPaymentGateway gateway,
    ISecretProtector protector,
    TimeProvider clock)
    : ICommandHandler<SyncPaymentCommand, OnlinePaymentResultDto>
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(3);

    public async Task<OnlinePaymentResultDto> Handle(SyncPaymentCommand command, CancellationToken cancellationToken)
    {
        var transaction = await db.PaymentTransactions.FirstOrDefaultAsync(t => t.Id == command.TransactionId, cancellationToken)
            ?? throw new NotFoundException("Pagamento", command.TransactionId);

        var order = await PaymentAccess.LoadOrderAsync(db, currentUser, transaction.OrderId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        var recentlyChecked = transaction.LastCheckedAt is { } last && now - last < MinimumInterval;
        if (!transaction.IsFinal && !recentlyChecked && transaction.ProviderPaymentId is not null)
        {
            var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == currentTenant.RequiredId(), cancellationToken)).Payments;
            var accessToken = protector.Unprotect(settings.MercadoPagoAccessTokenProtected);

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                var result = await gateway.GetPaymentAsync(transaction.ProviderPaymentId, accessToken, cancellationToken);
                if (result.Success)
                {
                    PaymentSettlement.Apply(transaction, order, result, now);
                }
                else
                {
                    transaction.MarkChecked(now);
                }

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return PaymentResultMapper.ToDto(transaction, order);
    }
}

/// <summary>
/// Mercado Pago notification (webhook). The tenant comes from the URL; the signature is checked with the
/// tenant's webhook secret; the payment is then re-read from the gateway — the payload itself is never trusted.
/// </summary>
public sealed record ProcessMercadoPagoNotificationCommand(
    string? Type,
    string? DataId,
    string? SignatureHeader,
    string? RequestId) : ICommand<bool>;

internal sealed class ProcessMercadoPagoNotificationCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant currentTenant,
    IPaymentGateway gateway,
    ISecretProtector protector,
    TimeProvider clock,
    ILogger<ProcessMercadoPagoNotificationCommandHandler> logger)
    : ICommandHandler<ProcessMercadoPagoNotificationCommand, bool>
{
    public async Task<bool> Handle(ProcessMercadoPagoNotificationCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == currentTenant.RequiredId(), cancellationToken);
        if (tenant is null)
        {
            return false;
        }

        var secret = protector.Unprotect(tenant.Payments.MercadoPagoWebhookSecretProtected);
        if (!gateway.IsValidWebhookSignature(secret, command.SignatureHeader, command.RequestId, command.DataId))
        {
            logger.LogWarning("Rejected Mercado Pago notification with an invalid signature for tenant {Tenant}", tenant.Slug);
            throw new ForbiddenAccessException("Assinatura inválida.");
        }

        if (!string.Equals(command.Type, "payment", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(command.DataId))
        {
            return true; // Acknowledge other topics so Mercado Pago stops retrying them.
        }

        var accessToken = protector.Unprotect(tenant.Payments.MercadoPagoAccessTokenProtected);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return true;
        }

        var result = await gateway.GetPaymentAsync(command.DataId, accessToken, cancellationToken);
        if (!result.Success)
        {
            // Let Mercado Pago retry later.
            throw new ConflictException("Não foi possível consultar o pagamento agora.");
        }

        var transaction = await db.PaymentTransactions.FirstOrDefaultAsync(t => t.ProviderPaymentId == command.DataId, cancellationToken);
        if (transaction is null)
        {
            logger.LogInformation("Mercado Pago payment {PaymentId} does not match any transaction of {Tenant}", command.DataId, tenant.Slug);
            return true;
        }

        var order = await db.Orders.Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == transaction.OrderId, cancellationToken);
        if (order is null)
        {
            return true;
        }

        PaymentSettlement.Apply(transaction, order, result, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

// ---------------------------------------------------------------- Helpers

internal static class PaymentSettlement
{
    /// <summary>Applies a gateway result to the transaction and, on approval, registers the payment on the order once.</summary>
    public static void Apply(PaymentTransaction transaction, Order order, GatewayPaymentResult result, DateTime now)
    {
        var wasApproved = transaction.Status == TransactionStatus.Approved;

        transaction.ApplyGatewayResult(
            result.ProviderPaymentId,
            result.Status,
            result.StatusDetail,
            result.Method,
            result.PixQrCode,
            result.PixQrCodeBase64,
            result.TicketUrl,
            result.ExpiresAt,
            result.ApprovedAt,
            result.RawResponse,
            now);

        var alreadyRegistered = order.Payments.Any(p => p.PaymentTransactionId == transaction.Id);
        if (result.Status == TransactionStatus.Approved && !wasApproved && !alreadyRegistered && order.PaymentStatus != PaymentStatus.Paid)
        {
            var amount = Math.Min(result.Amount ?? transaction.Amount, order.OutstandingAmount);
            if (amount > 0)
            {
                order.RegisterPayment(
                    transaction.Method,
                    amount,
                    result.ApprovedAt ?? now,
                    $"Mercado Pago {result.ProviderPaymentId}",
                    "Mercado Pago",
                    transaction.Id);
            }
        }
    }
}

internal static class PaymentAccess
{
    /// <summary>Loads an order (tracked, with its payments) the current user may pay: staff, or its own customer.</summary>
    public static async Task<Order> LoadOrderAsync(IApplicationDbContext db, ICurrentUser currentUser, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Pedido", orderId);

        if (currentUser.IsStaff())
        {
            return order;
        }

        var customer = await CustomerRules.GetForUserAsync(db, currentUser.RequiredUserId(), tracking: false, cancellationToken);
        return order.CustomerId == customer.Id ? order : throw new NotFoundException("Pedido", orderId);
    }

    public static async Task<string?> CustomerEmailAsync(IApplicationDbContext db, Guid customerId, CancellationToken cancellationToken) =>
        await db.Customers.AsNoTracking().Where(c => c.Id == customerId).Select(c => c.Email).FirstOrDefaultAsync(cancellationToken);
}

public static class PaymentMethodMapper
{
    public static bool IsPix(string? paymentMethodId, string? paymentTypeId) =>
        string.Equals(paymentMethodId, "pix", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(paymentTypeId, "bank_transfer", StringComparison.OrdinalIgnoreCase);

    public static PaymentMethod FromBrick(string? paymentMethodId, string? paymentTypeId)
    {
        if (IsPix(paymentMethodId, paymentTypeId))
        {
            return PaymentMethod.Pix;
        }

        if (string.Equals(paymentTypeId, "debit_card", StringComparison.OrdinalIgnoreCase) ||
            (paymentMethodId?.StartsWith("deb", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return PaymentMethod.DebitCard;
        }

        return PaymentMethod.CreditCard;
    }
}

internal static class PaymentResultMapper
{
    public static OnlinePaymentResultDto ToDto(PaymentTransaction transaction, Order order) => new()
    {
        TransactionId = transaction.Id,
        Status = transaction.Status,
        StatusDetail = transaction.StatusDetail,
        Method = transaction.Method,
        IsPix = transaction.IsPix,
        PixQrCode = transaction.PixQrCode,
        PixQrCodeBase64 = transaction.PixQrCodeBase64,
        TicketUrl = transaction.TicketUrl,
        ExpiresAt = transaction.ExpiresAt,
        OrderPaid = order.PaymentStatus == PaymentStatus.Paid
    };
}
