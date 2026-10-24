namespace ArtDash.Shared.Contracts.Branding;

/// <summary>
/// Everything the UI needs to dress itself in a tenant's brand: names, logos, palette, typography.
/// Served anonymously — the storefront and the login page need it before anyone signs in.
/// </summary>
public sealed record BrandingDto
{
    public string DisplayName { get; init; } = "ArtDash";
    public string? Tagline { get; init; }

    /// <summary>Wordmark for light backgrounds (storefront bar, login card).</summary>
    public string? LogoUrl { get; init; }

    /// <summary>Wordmark for dark backgrounds (back-office console bar). Falls back to <see cref="LogoUrl"/>.</summary>
    public string? LogoDarkUrl { get; init; }

    /// <summary>Square mark used for the favicon, app icon and drawer header.</summary>
    public string? IconUrl { get; init; }

    public string PrimaryColor { get; init; } = BrandingDefaults.PrimaryColor;
    public string SecondaryColor { get; init; } = BrandingDefaults.SecondaryColor;
    public string AccentColor { get; init; } = BrandingDefaults.AccentColor;

    /// <summary>Background of the back-office app bar.</summary>
    public string ConsoleColor { get; init; } = BrandingDefaults.ConsoleColor;

    public string FontFamily { get; init; } = BrandingDefaults.FontFamily;
    public int BorderRadius { get; init; } = BrandingDefaults.BorderRadius;
    public bool AllowDarkMode { get; init; } = true;

    public static BrandingDto Default { get; } = new();
}

public static class BrandingDefaults
{
    public const string PrimaryColor = "#2F6FED";
    public const string SecondaryColor = "#0F172A";
    public const string AccentColor = "#F59E0B";
    public const string ConsoleColor = "#111827";
    public const string FontFamily = "Inter";
    public const int BorderRadius = 10;

    /// <summary>Google Fonts families offered in the branding editor.</summary>
    public static readonly IReadOnlyList<string> Fonts =
        ["Inter", "Roboto", "Poppins", "Montserrat", "Nunito", "Quicksand", "Open Sans", "Lato", "Rubik", "Manrope"];
}

public sealed class UpdateBrandingRequest
{
    public string? DisplayName { get; set; }
    public string? Tagline { get; set; }
    public string? LogoUrl { get; set; }
    public string? LogoDarkUrl { get; set; }
    public string? IconUrl { get; set; }
    public string PrimaryColor { get; set; } = BrandingDefaults.PrimaryColor;
    public string SecondaryColor { get; set; } = BrandingDefaults.SecondaryColor;
    public string AccentColor { get; set; } = BrandingDefaults.AccentColor;
    public string ConsoleColor { get; set; } = BrandingDefaults.ConsoleColor;
    public string FontFamily { get; set; } = BrandingDefaults.FontFamily;
    public int BorderRadius { get; set; } = BrandingDefaults.BorderRadius;
    public bool AllowDarkMode { get; set; } = true;

    public static UpdateBrandingRequest From(BrandingDto dto) => new()
    {
        DisplayName = dto.DisplayName,
        Tagline = dto.Tagline,
        LogoUrl = dto.LogoUrl,
        LogoDarkUrl = dto.LogoDarkUrl,
        IconUrl = dto.IconUrl,
        PrimaryColor = dto.PrimaryColor,
        SecondaryColor = dto.SecondaryColor,
        AccentColor = dto.AccentColor,
        ConsoleColor = dto.ConsoleColor,
        FontFamily = dto.FontFamily,
        BorderRadius = dto.BorderRadius,
        AllowDarkMode = dto.AllowDarkMode
    };

    public BrandingDto ToPreview(string fallbackName) => new()
    {
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? fallbackName : DisplayName,
        Tagline = Tagline,
        LogoUrl = LogoUrl,
        LogoDarkUrl = LogoDarkUrl,
        IconUrl = IconUrl,
        PrimaryColor = PrimaryColor,
        SecondaryColor = SecondaryColor,
        AccentColor = AccentColor,
        ConsoleColor = ConsoleColor,
        FontFamily = FontFamily,
        BorderRadius = BorderRadius,
        AllowDarkMode = AllowDarkMode
    };
}

/// <summary>Kinds of brand image that can be uploaded.</summary>
public static class BrandAssetKind
{
    public const string Logo = "logo";
    public const string LogoDark = "logo-dark";
    public const string Icon = "icon";

    public static bool IsValid(string? kind) => kind is Logo or LogoDark or Icon;
}
