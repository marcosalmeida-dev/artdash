using ArtDash.Shared.Contracts.Branding;

namespace ArtDash.UI.Client.Theming;

/// <summary>CSS produced from a tenant's branding: custom properties and the web font.</summary>
public static class BrandCss
{
    public static string SafeFont(string? font) =>
        font is not null && BrandingDefaults.Fonts.Contains(font) ? font : BrandingDefaults.FontFamily;

    /// <summary>Google Fonts stylesheet for the brand font (400–800 weights).</summary>
    public static string FontStylesheetUrl(string? font) =>
        $"https://fonts.googleapis.com/css2?family={SafeFont(font).Replace(' ', '+')}:wght@400;500;600;700;800&display=swap";

    public static string FontStack(string? font) =>
        $"'{SafeFont(font)}', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";

    /// <summary>
    /// The <c>--ad-*</c> variables app.css is written against. Values are validated (hex colors, a font
    /// from the allow-list, a clamped radius), so they are safe to write into a style element.
    /// </summary>
    public static string Variables(BrandingDto branding)
    {
        var primary = ColorMath.Normalize(branding.PrimaryColor, BrandingDefaults.PrimaryColor);
        var secondary = ColorMath.Normalize(branding.SecondaryColor, BrandingDefaults.SecondaryColor);
        var accent = ColorMath.Normalize(branding.AccentColor, BrandingDefaults.AccentColor);
        var console = ColorMath.Normalize(branding.ConsoleColor, BrandingDefaults.ConsoleColor);
        var radius = Math.Clamp(branding.BorderRadius, 0, 24);

        return string.Join(';',
            $"--ad-primary:{primary}",
            $"--ad-primary-dk:{ColorMath.Darken(primary, 0.18)}",
            $"--ad-primary-lt:{ColorMath.Lighten(primary, 0.35)}",
            $"--ad-primary-text:{ColorMath.ContrastText(primary)}",
            $"--ad-primary-rgb:{Rgb(primary)}",
            $"--ad-secondary:{secondary}",
            $"--ad-accent:{accent}",
            $"--ad-accent-text:{ColorMath.ContrastText(accent)}",
            $"--ad-console:{console}",
            $"--ad-console-2:{ColorMath.Lighten(console, 0.08)}",
            $"--ad-console-text:{ColorMath.ContrastText(console)}",
            $"--ad-on-console-accent:{(ColorMath.IsDark(console) ? ColorMath.ForDarkSurface(primary, console) : ColorMath.Darken(primary, 0.2))}",
            $"--ad-font:{FontStack(branding.FontFamily)}",
            $"--ad-radius:{radius}px",
            $"--ad-radius-lg:{Math.Clamp(radius + 6, 6, 28)}px") + ";";
    }

    public static string RootRule(BrandingDto branding) => $":root{{{Variables(branding)}}}";

    private static string Rgb(string hex)
    {
        var (r, g, b) = ColorMath.ToRgb(hex);
        return $"{r},{g},{b}";
    }
}

/// <summary>Builds the MudBlazor theme (light and dark palettes) from a tenant's branding.</summary>
public static class ThemeBuilder
{
    public static MudTheme Build(BrandingDto branding)
    {
        var primary = ColorMath.Normalize(branding.PrimaryColor, BrandingDefaults.PrimaryColor);
        var secondary = ColorMath.Normalize(branding.SecondaryColor, BrandingDefaults.SecondaryColor);
        var accent = ColorMath.Normalize(branding.AccentColor, BrandingDefaults.AccentColor);
        var radius = $"{Math.Clamp(branding.BorderRadius, 0, 24)}px";

        const string darkSurface = "#1E2126";
        var darkPrimary = ColorMath.ForDarkSurface(primary, darkSurface);
        var darkSecondary = ColorMath.ForDarkSurface(secondary, darkSurface);
        var darkAccent = ColorMath.ForDarkSurface(accent, darkSurface);

        return new MudTheme
        {
            // Snackbars must clear full-screen overlays (checkout sheets, map panels) — lesson from artstore-fs.
            ZIndex = new ZIndex { Snackbar = 2000 },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = radius,
                AppbarHeight = "64px",
                DrawerWidthLeft = "270px"
            },
            PaletteLight = new PaletteLight
            {
                Primary = primary,
                PrimaryDarken = ColorMath.Darken(primary, 0.15),
                PrimaryLighten = ColorMath.Lighten(primary, 0.2),
                PrimaryContrastText = ColorMath.ContrastText(primary),
                Secondary = secondary,
                SecondaryDarken = ColorMath.Darken(secondary, 0.15),
                SecondaryLighten = ColorMath.Lighten(secondary, 0.2),
                SecondaryContrastText = ColorMath.ContrastText(secondary),
                Tertiary = accent,
                TertiaryDarken = ColorMath.Darken(accent, 0.15),
                TertiaryLighten = ColorMath.Lighten(accent, 0.2),
                TertiaryContrastText = ColorMath.ContrastText(accent),
                Info = "#0288D1",
                InfoContrastText = "#FFFFFF",
                Success = "#2E7D32",
                SuccessContrastText = "#FFFFFF",
                Warning = "#ED6C02",
                WarningContrastText = "#FFFFFF",
                Error = "#D32F2F",
                ErrorContrastText = "#FFFFFF",
                Dark = "#424242",
                DarkContrastText = "#FFFFFF",
                TextPrimary = "rgba(17,24,39,0.92)",
                TextSecondary = "rgba(17,24,39,0.62)",
                TextDisabled = "rgba(17,24,39,0.38)",
                ActionDefault = "rgba(17,24,39,0.56)",
                ActionDisabled = "rgba(17,24,39,0.26)",
                ActionDisabledBackground = "rgba(17,24,39,0.12)",
                Background = "#F5F6F8",
                BackgroundGray = "#ECEEF2",
                Surface = "#FFFFFF",
                DrawerBackground = "#FFFFFF",
                DrawerText = "rgba(17,24,39,0.87)",
                AppbarBackground = primary,
                AppbarText = ColorMath.ContrastText(primary),
                LinesDefault = "rgba(17,24,39,0.12)",
                LinesInputs = "rgba(17,24,39,0.38)",
                TableLines = "rgba(17,24,39,0.10)",
                TableStriped = "rgba(17,24,39,0.02)",
                TableHover = "rgba(17,24,39,0.04)",
                Divider = "rgba(17,24,39,0.12)",
                DividerLight = "rgba(17,24,39,0.06)",
                HoverOpacity = 0.06,
                OverlayDark = "rgba(17,24,39,0.5)",
                OverlayLight = "rgba(255,255,255,0.5)"
            },
            PaletteDark = new PaletteDark
            {
                Primary = darkPrimary,
                PrimaryContrastText = ColorMath.ContrastText(darkPrimary),
                Secondary = darkSecondary,
                SecondaryContrastText = ColorMath.ContrastText(darkSecondary),
                Tertiary = darkAccent,
                TertiaryContrastText = ColorMath.ContrastText(darkAccent),
                Info = "#4FC3F7",
                Success = "#66BB6A",
                Warning = "#FFA726",
                Error = "#EF5350",
                Background = "#121417",
                BackgroundGray = "#1A1D21",
                Surface = darkSurface,
                DrawerBackground = darkSurface,
                DrawerText = "rgba(255,255,255,0.87)",
                AppbarBackground = darkSurface,
                AppbarText = "rgba(255,255,255,0.87)",
                TextPrimary = "rgba(255,255,255,0.87)",
                TextSecondary = "rgba(255,255,255,0.60)",
                TextDisabled = "rgba(255,255,255,0.38)",
                ActionDefault = "rgba(255,255,255,0.60)",
                ActionDisabled = "rgba(255,255,255,0.26)",
                LinesDefault = "rgba(255,255,255,0.12)",
                LinesInputs = "rgba(255,255,255,0.30)",
                TableLines = "rgba(255,255,255,0.12)",
                Divider = "rgba(255,255,255,0.12)",
                DividerLight = "rgba(255,255,255,0.06)"
            }
        };
    }
}
