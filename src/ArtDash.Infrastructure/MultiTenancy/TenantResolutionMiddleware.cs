using System.Security.Claims;
using ArtDash.Infrastructure.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ArtDash.Infrastructure.MultiTenancy;

/// <summary>
/// Decides which tenant a request belongs to and stores it in the scoped <see cref="CurrentTenant"/>.
/// <list type="number">
/// <item>Signed-in users: the tenant in their token. A token used on another tenant's host is rejected.</item>
/// <item>Route value <c>tenantSlug</c> (gateway webhooks).</item>
/// <item><c>X-Tenant</c> header (slug) — integrations and tools.</item>
/// <item><c>X-Tenant-Host</c> header, else the request host: a registered custom domain or <c>slug.root-domain</c>.</item>
/// <item>The configured default tenant (development).</item>
/// </list>
/// API requests without a tenant are answered with 404; pages render with the neutral brand.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, IOptions<MultiTenancyOptions> options)
{
    public const string TenantItemKey = "artdash:tenant";

    public async Task InvokeAsync(HttpContext context, ITenantStore store, ICurrentTenantSetter setter)
    {
        var cancellationToken = context.RequestAborted;
        var requested = await ResolveFromRequestAsync(context, store, cancellationToken);

        TenantInfo? fromToken = null;
        if (context.User.Identity?.IsAuthenticated == true &&
            Guid.TryParse(context.User.FindFirstValue(ArtDashClaimTypes.TenantId), out var tokenTenantId))
        {
            fromToken = await store.FindByIdAsync(tokenTenantId, cancellationToken);

            if (fromToken is null || (requested is not null && requested.Id != fromToken.Id))
            {
                await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Acesso negado",
                    "Esta sessão pertence a outra empresa. Entre novamente neste endereço.");
                return;
            }
        }

        var tenant = fromToken ?? requested;

        if (tenant is { IsActive: true })
        {
            setter.Set(tenant.Id, tenant.Slug);
            context.Items[TenantItemKey] = tenant;
        }
        else if (IsApiRequest(context))
        {
            await WriteProblemAsync(context, StatusCodes.Status404NotFound, "Empresa não encontrada",
                "Não foi possível identificar a empresa (tenant) desta requisição.");
            return;
        }

        await next(context);
    }

    private async ValueTask<TenantInfo?> ResolveFromRequestAsync(HttpContext context, ITenantStore store, CancellationToken cancellationToken)
    {
        if (context.GetRouteValue("tenantSlug") is string routeSlug && !string.IsNullOrWhiteSpace(routeSlug))
        {
            return await store.FindBySlugAsync(routeSlug, cancellationToken);
        }

        var slugHeader = context.Request.Headers[TenantHeaders.Tenant].ToString();
        if (!string.IsNullOrWhiteSpace(slugHeader))
        {
            return await store.FindBySlugAsync(slugHeader, cancellationToken);
        }

        var hostHeader = context.Request.Headers[TenantHeaders.TenantHost].ToString();
        var host = string.IsNullOrWhiteSpace(hostHeader) ? context.Request.Host.Host : hostHeader;

        var byHost = await ResolveHostAsync(host, store, cancellationToken);
        if (byHost is not null)
        {
            return byHost;
        }

        var fallback = options.Value.DefaultTenant;
        return string.IsNullOrWhiteSpace(fallback) ? null : await store.FindBySlugAsync(fallback, cancellationToken);
    }

    private async ValueTask<TenantInfo?> ResolveHostAsync(string host, ITenantStore store, CancellationToken cancellationToken)
    {
        var normalized = TenantDomain.NormalizeHost(host);
        if (normalized.Length == 0)
        {
            return null;
        }

        var exact = await store.FindByHostAsync(normalized, cancellationToken);
        if (exact is not null)
        {
            return exact;
        }

        foreach (var root in options.Value.RootDomains)
        {
            var suffix = "." + root.Trim().ToLowerInvariant();
            if (!normalized.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var label = normalized[..^suffix.Length];
            if (label.Length > 0 && !label.Contains('.'))
            {
                return await store.FindBySlugAsync(label, cancellationToken);
            }
        }

        return null;
    }

    private static bool IsApiRequest(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs");

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Detail = detail },
            options: null,
            contentType: "application/problem+json");
    }
}

public static class MultiTenancyApplicationBuilderExtensions
{
    /// <summary>Must run after <c>UseRouting</c> and <c>UseAuthentication</c>, before <c>UseAuthorization</c>.</summary>
    public static IApplicationBuilder UseMultiTenancy(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
