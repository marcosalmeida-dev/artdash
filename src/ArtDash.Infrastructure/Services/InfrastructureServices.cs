using ArtDash.Infrastructure.Options;
using ArtDash.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace ArtDash.Infrastructure.Services;

/// <summary>
/// Per-tenant counters with optimistic concurrency: a conflicting increment is retried, so two
/// requests never receive the same number. Uses the request's own context (and saves it).
/// </summary>
internal sealed class SequenceGenerator(ApplicationDbContext db) : ISequenceGenerator
{
    private const int MaxAttempts = 8;

    public async Task<long> NextAsync(string sequenceName, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var sequence = await db.TenantSequences.FirstOrDefaultAsync(s => s.Name == sequenceName, cancellationToken);
            if (sequence is null)
            {
                sequence = new TenantSequence(sequenceName);
                db.TenantSequences.Add(sequence);
            }

            var value = sequence.Next();

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return value;
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Someone else took this number (or created the row first): reload and try again.
                db.Entry(sequence).State = EntityState.Detached;
                await Task.Delay(Random.Shared.Next(10, 60), cancellationToken);
            }
        }
    }
}

/// <summary>Encrypts tenant secrets with ASP.NET Core Data Protection (keys persisted in the database).</summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("ArtDash.TenantSecrets.v1");

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Keys rotated away or the value was pasted in clear text: treat as "not configured".
            return null;
        }
    }
}

/// <summary>Builds absolute URLs from configuration or from the current request.</summary>
internal sealed class AppUrls(IHttpContextAccessor accessor, IOptions<AppOptions> options) : IAppUrls
{
    public string? MercadoPagoWebhookUrl(string tenantSlug)
    {
        var baseUrl = PublicBaseUrl();
        if (baseUrl is null || !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || IsLocal(baseUrl))
        {
            return null;
        }

        return $"{baseUrl}/api/webhooks/mercadopago/{Uri.EscapeDataString(tenantSlug)}";
    }

    public string TrackingUrl(string trackingCode) =>
        $"{TenantBaseUrl() ?? string.Empty}/rastreio/{Uri.EscapeDataString(trackingCode)}";

    private string? PublicBaseUrl() =>
        string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl)
            ? TenantBaseUrl()
            : options.Value.PublicBaseUrl.TrimEnd('/');

    /// <summary>The address the end user is on (the circuit forwards it in X-Tenant-Host).</summary>
    private string? TenantBaseUrl()
    {
        var request = accessor.HttpContext?.Request;
        if (request is null)
        {
            return null;
        }

        var forwardedHost = request.Headers[TenantHeaders.TenantHost].ToString();
        var host = string.IsNullOrWhiteSpace(forwardedHost) ? request.Host.Value : forwardedHost;
        return $"{request.Scheme}://{host}";
    }

    private static bool IsLocal(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Saves uploads on disk under <see cref="StorageOptions.UploadsPath"/>/{tenant}/{folder}.</summary>
internal sealed class LocalFileStorage(IOptions<StorageOptions> options, IWebHostEnvironment environment, ICurrentTenant currentTenant)
    : IFileStorage
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/svg+xml"] = ".svg"
    };

    public string RootPath => options.Value.ResolveRootPath(environment.ContentRootPath);

    public async Task<string> SaveAsync(Stream content, string fileName, string contentType, string folder, CancellationToken cancellationToken)
    {
        var tenant = currentTenant.Slug ?? "shared";
        var extension = Extensions.TryGetValue(contentType, out var ext) ? ext : Path.GetExtension(fileName).ToLowerInvariant();
        var safeFolder = new string(folder.Where(char.IsAsciiLetterOrDigit).ToArray());
        var relative = Path.Combine(tenant, safeFolder, $"{Guid.CreateVersion7():N}{extension}");
        var fullPath = Path.Combine(RootPath, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using (var file = File.Create(fullPath))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return $"{options.Value.RequestPath.TrimEnd('/')}/{relative.Replace('\\', '/')}";
    }

    public Task DeleteAsync(string? url, CancellationToken cancellationToken)
    {
        var prefix = options.Value.RequestPath.TrimEnd('/') + "/";
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var relative = url[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(RootPath, relative));

        // Only ever delete inside the uploads root, and only the current tenant's files.
        var tenantRoot = Path.GetFullPath(Path.Combine(RootPath, currentTenant.Slug ?? "shared"));
        if (fullPath.StartsWith(tenantRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }
}
