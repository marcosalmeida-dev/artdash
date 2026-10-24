namespace ArtDash.Shared.Constants;

/// <summary>HTTP headers the API reads to decide which tenant a request belongs to.</summary>
public static class TenantHeaders
{
    /// <summary>Tenant slug, e.g. <c>acme</c>. Useful for tools and integrations.</summary>
    public const string Tenant = "X-Tenant";

    /// <summary>
    /// The host the browser is on. The Blazor circuit calls the API from the server, so it forwards the
    /// browser's host here and the API resolves the tenant exactly as it would for the browser itself.
    /// </summary>
    public const string TenantHost = "X-Tenant-Host";
}
