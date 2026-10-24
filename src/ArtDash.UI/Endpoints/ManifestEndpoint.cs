using ArtDash.Application.Features.Branding;

namespace ArtDash.UI.Endpoints;

/// <summary>
/// The PWA manifest, built per tenant from its branding: an installed app carries the company's own
/// name, colours and icon instead of the platform's.
/// </summary>
public static class ManifestEndpoint
{
    public static IEndpointConventionBuilder MapManifest(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/manifest.webmanifest", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var branding = await sender.Send(new GetBrandingQuery(), cancellationToken);

            var icons = new List<Dictionary<string, string>>();
            if (!string.IsNullOrWhiteSpace(branding.IconUrl))
            {
                icons.Add(Icon(branding.IconUrl, "any", IconType(branding.IconUrl), "any"));
            }

            icons.Add(Icon("/img/icon-192.png", "192x192", "image/png", "any"));
            icons.Add(Icon("/img/icon-512.png", "512x512", "image/png", "any"));
            icons.Add(Icon("/img/icon-maskable-512.png", "512x512", "image/png", "maskable"));

            var manifest = new Dictionary<string, object?>
            {
                ["id"] = "/",
                ["name"] = branding.DisplayName,
                ["short_name"] = branding.DisplayName.Length <= 12 ? branding.DisplayName : branding.DisplayName[..12].TrimEnd(),
                ["description"] = branding.Tagline ?? "Faça seu pedido e acompanhe a entrega em tempo real.",
                ["lang"] = "pt-BR",
                ["start_url"] = "/",
                ["scope"] = "/",
                ["display"] = "standalone",
                ["display_override"] = new[] { "standalone", "minimal-ui" },
                ["background_color"] = "#FFFFFF",
                ["theme_color"] = branding.PrimaryColor,
                ["icons"] = icons,
                ["shortcuts"] = new[]
                {
                    new Dictionary<string, string> { ["name"] = "Fazer um pedido", ["url"] = "/" },
                    new Dictionary<string, string> { ["name"] = "Meus pedidos", ["url"] = "/meus-pedidos" },
                    new Dictionary<string, string> { ["name"] = "Minhas entregas (motorista)", ["url"] = "/motorista" }
                }
            };

            return Results.Json(manifest, contentType: "application/manifest+json");
        }).AllowAnonymous();

    private static Dictionary<string, string> Icon(string src, string sizes, string type, string purpose) => new()
    {
        ["src"] = src,
        ["sizes"] = sizes,
        ["type"] = type,
        ["purpose"] = purpose
    };

    private static string IconType(string url) =>
        url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml"
        : url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
        : url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg"
        : "image/png";
}
