namespace ArtDash.UI.Client.Theming;

/// <summary>Small color helpers for building a full palette out of a tenant's three brand colors.</summary>
public static class ColorMath
{
    public static bool IsValidHex(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    /// <summary>A valid "#RRGGBB" or the fallback.</summary>
    public static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var hex = value.Trim();
        if (hex.Length == 4 && hex[0] == '#')
        {
            hex = $"#{hex[1]}{hex[1]}{hex[2]}{hex[2]}{hex[3]}{hex[3]}";
        }

        return IsValidHex(hex) ? hex.ToUpperInvariant() : fallback;
    }

    public static (int R, int G, int B) ToRgb(string hex)
    {
        var value = Normalize(hex, "#000000");
        return (Convert.ToInt32(value[1..3], 16), Convert.ToInt32(value[3..5], 16), Convert.ToInt32(value[5..7], 16));
    }

    public static string ToHex(int r, int g, int b) =>
        $"#{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";

    /// <summary>Mixes <paramref name="weight"/> (0..1) of <paramref name="other"/> into <paramref name="color"/>.</summary>
    public static string Mix(string color, string other, double weight)
    {
        var (r1, g1, b1) = ToRgb(color);
        var (r2, g2, b2) = ToRgb(other);
        weight = Math.Clamp(weight, 0, 1);

        return ToHex(
            (int)Math.Round(r1 + (r2 - r1) * weight),
            (int)Math.Round(g1 + (g2 - g1) * weight),
            (int)Math.Round(b1 + (b2 - b1) * weight));
    }

    public static string Darken(string color, double amount) => Mix(color, "#000000", amount);

    public static string Lighten(string color, double amount) => Mix(color, "#FFFFFF", amount);

    public static string Rgba(string color, double alpha)
    {
        var (r, g, b) = ToRgb(color);
        return $"rgba({r},{g},{b},{alpha.ToString("0.###", CultureInfo.InvariantCulture)})";
    }

    /// <summary>WCAG relative luminance (0 = black, 1 = white).</summary>
    public static double Luminance(string color)
    {
        var (r, g, b) = ToRgb(color);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);

        static double Channel(int value)
        {
            var c = value / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }

    public static double Contrast(string a, string b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Black or white — whichever reads better on <paramref name="background"/>.</summary>
    public static string ContrastText(string background) =>
        Contrast(background, "#FFFFFF") >= Contrast(background, "#111111") ? "#FFFFFF" : "#111111";

    public static bool IsDark(string color) => Luminance(color) < 0.35;

    /// <summary>Lightens a color until it reads on a dark surface (for the dark palette).</summary>
    public static string ForDarkSurface(string color, string surface = "#1E2126")
    {
        var result = color;
        for (var step = 0; step < 8 && Contrast(result, surface) < 3.5; step++)
        {
            result = Lighten(result, 0.15);
        }

        return result;
    }
}
