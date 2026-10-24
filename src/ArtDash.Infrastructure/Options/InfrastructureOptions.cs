namespace ArtDash.Infrastructure.Options;

/// <summary>"Jwt" section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "ArtDash";
    public string Audience { get; set; } = "ArtDash";

    /// <summary>HMAC key, at least 32 characters. Keep it out of source control (user-secrets / env).</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}

/// <summary>"MultiTenancy" section.</summary>
public sealed class MultiTenancyOptions
{
    public const string SectionName = "MultiTenancy";

    /// <summary>Slug used when the host matches no tenant (handy for localhost). Leave empty in production.</summary>
    public string? DefaultTenant { get; set; }

    /// <summary>Platform domains whose sub-domains map to tenant slugs (acme.artdash.com.br → acme).</summary>
    public List<string> RootDomains { get; set; } = [];
}

/// <summary>"Storage" section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Folder for uploads; relative paths are resolved against the content root.</summary>
    public string UploadsPath { get; set; } = "App_Data/uploads";

    /// <summary>URL prefix the uploads are served from.</summary>
    public string RequestPath { get; set; } = "/uploads";

    /// <summary>Absolute uploads folder (shared by the storage service and the static-file middleware).</summary>
    public string ResolveRootPath(string contentRootPath) =>
        Path.IsPathRooted(UploadsPath) ? UploadsPath : Path.Combine(contentRootPath, UploadsPath);
}

/// <summary>"App" section.</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// Public HTTPS address of the platform, used for gateway webhooks
    /// (e.g. https://app.artdash.com.br). Empty means "use the address of the current request".
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>"Seed" section: tenants and admin users created on startup when missing.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; } = true;
    public List<SeedTenant> Tenants { get; set; } = [];
}

public sealed class SeedTenant
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Hosts { get; set; } = [];
    public string AdminName { get; set; } = "Administrador";
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? AccentColor { get; set; }
    public string? ConsoleColor { get; set; }
    public string? Tagline { get; set; }
    public bool SampleData { get; set; }
}
