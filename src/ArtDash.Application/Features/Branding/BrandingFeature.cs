using ArtDash.Shared.Contracts.Branding;

namespace ArtDash.Application.Features.Branding;

// ---------------------------------------------------------------- Get (anonymous)

public sealed record GetBrandingQuery : IQuery<BrandingDto>;

internal sealed class GetBrandingQueryHandler(IApplicationDbContext db, ICurrentTenant currentTenant)
    : IQueryHandler<GetBrandingQuery, BrandingDto>
{
    public async Task<BrandingDto> Handle(GetBrandingQuery query, CancellationToken cancellationToken)
    {
        if (currentTenant.Id is not { } tenantId)
        {
            return BrandingDto.Default;
        }

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        return tenant is null ? BrandingDto.Default : BrandingMapper.ToDto(tenant);
    }
}

// ---------------------------------------------------------------- Update (admin)

public sealed record UpdateBrandingCommand(UpdateBrandingRequest Request) : ICommand<BrandingDto>;

public sealed class UpdateBrandingCommandValidator : AbstractValidator<UpdateBrandingCommand>
{
    public UpdateBrandingCommandValidator()
    {
        RuleFor(x => x.Request.DisplayName).MaximumLength(100);
        RuleFor(x => x.Request.Tagline).MaximumLength(160);
        RuleFor(x => x.Request.PrimaryColor).HexColor();
        RuleFor(x => x.Request.SecondaryColor).HexColor();
        RuleFor(x => x.Request.AccentColor).HexColor();
        RuleFor(x => x.Request.ConsoleColor).HexColor();
        RuleFor(x => x.Request.FontFamily)
            .Must(font => BrandingDefaults.Fonts.Contains(font))
            .WithMessage("Fonte não suportada.");
        RuleFor(x => x.Request.BorderRadius).InclusiveBetween(0, 24);
        RuleFor(x => x.Request.LogoUrl).MaximumLength(500);
        RuleFor(x => x.Request.LogoDarkUrl).MaximumLength(500);
        RuleFor(x => x.Request.IconUrl).MaximumLength(500);
    }
}

internal sealed class UpdateBrandingCommandHandler(IApplicationDbContext db, ICurrentTenant currentTenant)
    : ICommandHandler<UpdateBrandingCommand, BrandingDto>
{
    public async Task<BrandingDto> Handle(UpdateBrandingCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == currentTenant.RequiredId(), cancellationToken)
            ?? throw new NotFoundException("Empresa", currentTenant.RequiredId());

        var request = command.Request;
        tenant.Branding = new TenantBranding
        {
            DisplayName = request.DisplayName.NullIfWhiteSpace(),
            Tagline = request.Tagline.NullIfWhiteSpace(),
            LogoUrl = request.LogoUrl.NullIfWhiteSpace(),
            LogoDarkUrl = request.LogoDarkUrl.NullIfWhiteSpace(),
            IconUrl = request.IconUrl.NullIfWhiteSpace(),
            PrimaryColor = request.PrimaryColor.ToUpperInvariant(),
            SecondaryColor = request.SecondaryColor.ToUpperInvariant(),
            AccentColor = request.AccentColor.ToUpperInvariant(),
            ConsoleColor = request.ConsoleColor.ToUpperInvariant(),
            FontFamily = request.FontFamily,
            BorderRadius = request.BorderRadius,
            AllowDarkMode = request.AllowDarkMode
        };

        await db.SaveChangesAsync(cancellationToken);
        return BrandingMapper.ToDto(tenant);
    }
}

// ---------------------------------------------------------------- Upload logo / icon (admin)

public sealed record UploadBrandAssetCommand(string Kind, Stream Content, string FileName, string ContentType)
    : ICommand<UploadResultDto>;

public sealed class UploadBrandAssetCommandValidator : AbstractValidator<UploadBrandAssetCommand>
{
    public UploadBrandAssetCommandValidator()
    {
        RuleFor(x => x.Kind).Must(BrandAssetKind.IsValid).WithMessage("Tipo de imagem inválido.");
        RuleFor(x => x.ContentType).Must(UploadRules.IsAllowedImage).WithMessage("Envie uma imagem PNG, JPG, WEBP ou SVG.");
    }
}

internal sealed class UploadBrandAssetCommandHandler(IApplicationDbContext db, ICurrentTenant currentTenant, IFileStorage storage)
    : ICommandHandler<UploadBrandAssetCommand, UploadResultDto>
{
    public async Task<UploadResultDto> Handle(UploadBrandAssetCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == currentTenant.RequiredId(), cancellationToken)
            ?? throw new NotFoundException("Empresa", currentTenant.RequiredId());

        var url = await storage.SaveAsync(command.Content, command.FileName, command.ContentType, "branding", cancellationToken);

        string? previous;
        switch (command.Kind)
        {
            case BrandAssetKind.Logo:
                previous = tenant.Branding.LogoUrl;
                tenant.Branding.LogoUrl = url;
                break;
            case BrandAssetKind.LogoDark:
                previous = tenant.Branding.LogoDarkUrl;
                tenant.Branding.LogoDarkUrl = url;
                break;
            default:
                previous = tenant.Branding.IconUrl;
                tenant.Branding.IconUrl = url;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(previous, cancellationToken);

        return new UploadResultDto(url);
    }
}

internal static class BrandingMapper
{
    public static BrandingDto ToDto(Tenant tenant) => new()
    {
        DisplayName = string.IsNullOrWhiteSpace(tenant.Branding.DisplayName) ? tenant.Name : tenant.Branding.DisplayName,
        Tagline = tenant.Branding.Tagline,
        LogoUrl = tenant.Branding.LogoUrl,
        LogoDarkUrl = tenant.Branding.LogoDarkUrl ?? tenant.Branding.LogoUrl,
        IconUrl = tenant.Branding.IconUrl,
        PrimaryColor = tenant.Branding.PrimaryColor,
        SecondaryColor = tenant.Branding.SecondaryColor,
        AccentColor = tenant.Branding.AccentColor,
        ConsoleColor = tenant.Branding.ConsoleColor,
        FontFamily = tenant.Branding.FontFamily,
        BorderRadius = tenant.Branding.BorderRadius,
        AllowDarkMode = tenant.Branding.AllowDarkMode
    };
}

/// <summary>Upload constraints shared by brand assets and product images.</summary>
public static class UploadRules
{
    public const long MaxImageBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/svg+xml"
    };

    public static bool IsAllowedImage(string? contentType) => contentType is not null && AllowedImageTypes.Contains(contentType);
}
