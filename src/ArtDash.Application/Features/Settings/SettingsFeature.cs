using ArtDash.Shared.Contracts.Settings;

namespace ArtDash.Application.Features.Settings;

// ---------------------------------------------------------------- Public store info (anonymous)

public sealed record GetStoreInfoQuery : IQuery<StoreInfoDto>;

internal sealed class GetStoreInfoQueryHandler(IApplicationDbContext db, ICurrentTenant currentTenant)
    : IQueryHandler<GetStoreInfoQuery, StoreInfoDto>
{
    public async Task<StoreInfoDto> Handle(GetStoreInfoQuery query, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == currentTenant.RequiredId(), cancellationToken)
            ?? throw new NotFoundException("Empresa", currentTenant.RequiredId());

        var payments = tenant.Payments;
        var online = payments.IsOnlinePaymentConfigured;

        return new StoreInfoDto
        {
            Name = string.IsNullOrWhiteSpace(tenant.Branding.DisplayName) ? tenant.Name : tenant.Branding.DisplayName,
            Phone = tenant.Store.Phone,
            WhatsApp = tenant.Store.WhatsApp,
            Email = tenant.Store.Email,
            OpeningHours = tenant.Store.OpeningHours,
            TimeZoneId = tenant.Store.TimeZoneId,
            Address = tenant.Address.Street.Length == 0 ? null : tenant.Address.ToDto(),
            DeliveryFee = tenant.Store.DeliveryFee,
            FreeDeliveryThreshold = tenant.Store.FreeDeliveryThreshold,
            MinimumOrderAmount = tenant.Store.MinimumOrderAmount,
            Payments = new PaymentOptionsDto
            {
                OnlinePaymentAvailable = online,
                PixEnabled = online && payments.OnlinePixEnabled,
                CreditCardEnabled = online && payments.OnlineCreditCardEnabled,
                DebitCardEnabled = online && payments.OnlineDebitCardEnabled,
                MaxInstallments = Math.Max(1, payments.MaxInstallments),
                PayOnDeliveryEnabled = payments.PayOnDeliveryEnabled,
                InvoicingEnabled = payments.InvoicingEnabled,
                MercadoPagoPublicKey = online ? payments.MercadoPagoPublicKey : null
            }
        };
    }
}

// ---------------------------------------------------------------- Store settings (admin)

public sealed record GetStoreSettingsQuery : IQuery<StoreSettingsDto>;

internal sealed class GetStoreSettingsQueryHandler(IApplicationDbContext db, ICurrentTenant currentTenant)
    : IQueryHandler<GetStoreSettingsQuery, StoreSettingsDto>
{
    public async Task<StoreSettingsDto> Handle(GetStoreSettingsQuery query, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == currentTenant.RequiredId(), cancellationToken)
            ?? throw new NotFoundException("Empresa", currentTenant.RequiredId());

        return new StoreSettingsDto
        {
            Name = tenant.Name,
            Phone = tenant.Store.Phone,
            WhatsApp = tenant.Store.WhatsApp,
            Email = tenant.Store.Email,
            Document = tenant.Store.Document,
            OpeningHours = tenant.Store.OpeningHours,
            TimeZoneId = tenant.Store.TimeZoneId,
            Address = tenant.Address.ToDto(),
            DeliveryFee = tenant.Store.DeliveryFee,
            FreeDeliveryThreshold = tenant.Store.FreeDeliveryThreshold,
            MinimumOrderAmount = tenant.Store.MinimumOrderAmount
        };
    }
}

public sealed record UpdateStoreSettingsCommand(StoreSettingsDto Request) : ICommand;

public sealed class UpdateStoreSettingsCommandValidator : AbstractValidator<UpdateStoreSettingsCommand>
{
    public UpdateStoreSettingsCommandValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().WithName("Nome").MaximumLength(150);
        RuleFor(x => x.Request.Phone).Phone();
        RuleFor(x => x.Request.WhatsApp).Phone();
        RuleFor(x => x.Request.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Request.Email));
        RuleFor(x => x.Request.Document).BrazilianDocument();
        RuleFor(x => x.Request.OpeningHours).MaximumLength(300);
        RuleFor(x => x.Request.DeliveryFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.FreeDeliveryThreshold).GreaterThan(0).When(x => x.Request.FreeDeliveryThreshold.HasValue);
        RuleFor(x => x.Request.MinimumOrderAmount).GreaterThan(0).When(x => x.Request.MinimumOrderAmount.HasValue);
        RuleFor(x => x.Request.TimeZoneId)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
            .WithMessage("Fuso horário desconhecido.");
        RuleFor(x => x.Request.Address).SetValidator(new AddressDtoValidator())
            .When(x => !x.Request.Address.IsEmpty);
    }
}

internal sealed class UpdateStoreSettingsCommandHandler(IApplicationDbContext db, ICurrentTenant currentTenant)
    : ICommandHandler<UpdateStoreSettingsCommand>
{
    public async Task<Unit> Handle(UpdateStoreSettingsCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == currentTenant.RequiredId(), cancellationToken)
            ?? throw new NotFoundException("Empresa", currentTenant.RequiredId());

        var request = command.Request;
        tenant.Rename(request.Name);
        tenant.Address = request.Address.IsEmpty ? new Address() : request.Address.ToDomain();
        tenant.Store = new TenantStoreSettings
        {
            Phone = request.Phone.NullIfWhiteSpace(),
            WhatsApp = request.WhatsApp.NullIfWhiteSpace(),
            Email = request.Email.NullIfWhiteSpace(),
            Document = Customer.NormalizeDocument(request.Document),
            OpeningHours = request.OpeningHours.NullIfWhiteSpace(),
            TimeZoneId = request.TimeZoneId,
            DeliveryFee = request.DeliveryFee,
            FreeDeliveryThreshold = request.FreeDeliveryThreshold,
            MinimumOrderAmount = request.MinimumOrderAmount
        };

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------- Payment settings (admin)

public sealed record GetPaymentSettingsQuery : IQuery<PaymentSettingsDto>;

internal sealed class GetPaymentSettingsQueryHandler(IApplicationDbContext db, ICurrentTenant currentTenant, IAppUrls urls)
    : IQueryHandler<GetPaymentSettingsQuery, PaymentSettingsDto>
{
    public async Task<PaymentSettingsDto> Handle(GetPaymentSettingsQuery query, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == currentTenant.RequiredId(), cancellationToken)
            ?? throw new NotFoundException("Empresa", currentTenant.RequiredId());

        var settings = tenant.Payments;
        return new PaymentSettingsDto
        {
            MercadoPagoPublicKey = settings.MercadoPagoPublicKey,
            HasAccessToken = !string.IsNullOrWhiteSpace(settings.MercadoPagoAccessTokenProtected),
            HasWebhookSecret = !string.IsNullOrWhiteSpace(settings.MercadoPagoWebhookSecretProtected),
            MercadoPagoSandbox = settings.MercadoPagoSandbox,
            OnlinePixEnabled = settings.OnlinePixEnabled,
            OnlineCreditCardEnabled = settings.OnlineCreditCardEnabled,
            OnlineDebitCardEnabled = settings.OnlineDebitCardEnabled,
            MaxInstallments = settings.MaxInstallments,
            PixExpirationMinutes = settings.PixExpirationMinutes,
            PayOnDeliveryEnabled = settings.PayOnDeliveryEnabled,
            InvoicingEnabled = settings.InvoicingEnabled,
            DefaultPaymentTermDays = settings.DefaultPaymentTermDays,
            WebhookUrl = urls.MercadoPagoWebhookUrl(tenant.Slug)
        };
    }
}

public sealed record UpdatePaymentSettingsCommand(PaymentSettingsDto Request) : ICommand;

public sealed class UpdatePaymentSettingsCommandValidator : AbstractValidator<UpdatePaymentSettingsCommand>
{
    public UpdatePaymentSettingsCommandValidator()
    {
        RuleFor(x => x.Request.MercadoPagoPublicKey).MaximumLength(200);
        RuleFor(x => x.Request.MercadoPagoAccessToken).MaximumLength(500);
        RuleFor(x => x.Request.MercadoPagoWebhookSecret).MaximumLength(500);
        RuleFor(x => x.Request.MaxInstallments).InclusiveBetween(1, 12);
        RuleFor(x => x.Request.PixExpirationMinutes).InclusiveBetween(5, 24 * 60);
        RuleFor(x => x.Request.DefaultPaymentTermDays).InclusiveBetween(1, 180);
    }
}

internal sealed class UpdatePaymentSettingsCommandHandler(IApplicationDbContext db, ICurrentTenant currentTenant, ISecretProtector protector)
    : ICommandHandler<UpdatePaymentSettingsCommand>
{
    public async Task<Unit> Handle(UpdatePaymentSettingsCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == currentTenant.RequiredId(), cancellationToken)
            ?? throw new NotFoundException("Empresa", currentTenant.RequiredId());

        var request = command.Request;
        var current = tenant.Payments;

        // Secrets are write-only: an empty field keeps what is stored.
        var accessToken = string.IsNullOrWhiteSpace(request.MercadoPagoAccessToken)
            ? current.MercadoPagoAccessTokenProtected
            : protector.Protect(request.MercadoPagoAccessToken.Trim());

        var webhookSecret = string.IsNullOrWhiteSpace(request.MercadoPagoWebhookSecret)
            ? current.MercadoPagoWebhookSecretProtected
            : protector.Protect(request.MercadoPagoWebhookSecret.Trim());

        tenant.Payments = new TenantPaymentSettings
        {
            MercadoPagoPublicKey = request.MercadoPagoPublicKey.NullIfWhiteSpace(),
            MercadoPagoAccessTokenProtected = accessToken,
            MercadoPagoWebhookSecretProtected = webhookSecret,
            MercadoPagoSandbox = request.MercadoPagoSandbox,
            OnlinePixEnabled = request.OnlinePixEnabled,
            OnlineCreditCardEnabled = request.OnlineCreditCardEnabled,
            OnlineDebitCardEnabled = request.OnlineDebitCardEnabled,
            MaxInstallments = request.MaxInstallments,
            PixExpirationMinutes = request.PixExpirationMinutes,
            PayOnDeliveryEnabled = request.PayOnDeliveryEnabled,
            InvoicingEnabled = request.InvoicingEnabled,
            DefaultPaymentTermDays = request.DefaultPaymentTermDays
        };

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
