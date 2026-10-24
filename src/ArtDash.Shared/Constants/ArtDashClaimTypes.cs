namespace ArtDash.Shared.Constants;

/// <summary>
/// Claim names written into the access token. Short JWT-style names are used end to end
/// (inbound claim mapping is disabled on the API), so server and client read the same keys.
/// </summary>
public static class ArtDashClaimTypes
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";
    public const string TenantId = "tenant_id";
    public const string TenantSlug = "tenant";
}
