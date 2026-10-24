using System.Globalization;
using System.Net;
using ArtDash.Infrastructure.MultiTenancy;
using ArtDash.Infrastructure.Options;
using ArtDash.UI.Components;
using ArtDash.UI.Endpoints;
using ArtDash.UI.Hubs;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;

namespace ArtDash.UI.Configuration;

/// <summary>The HTTP pipeline and the endpoint map. Order matters; each step says why it sits where it does.</summary>
public static class WebApplicationExtensions
{
    public static WebApplication UseWebHost(this WebApplication app)
    {
        // First: everything after this (rate limits, HTTPS, tenant by host) must see the real client.
        var forwarded = app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;
        if (forwarded.Enabled)
        {
            app.UseForwardedHeaders(forwarded.ToOptions());
        }

        app.UseExceptionHandler();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        // The development certificate covers localhost only; tenant subdomains use HTTP locally.
        // In production, circuit API calls and health probes still need plain HTTP over loopback.
        if (!app.Environment.IsDevelopment())
        {
            app.UseWhen(
                context => !IsCircuitCall(context) && !context.Request.Path.StartsWithSegments("/healthz"),
                branch => branch.UseHttpsRedirection());
        }

        app.UseSerilogRequestLogging(options =>
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
                : context.Request.Path.StartsWithSegments("/_blazor") || context.Request.Path.StartsWithSegments("/healthz")
                    ? Serilog.Events.LogEventLevel.Verbose
                    : Serilog.Events.LogEventLevel.Information);

        app.UseResponseCompression();

        var culture = new CultureInfo("pt-BR");
        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(culture),
            SupportedCultures = [culture],
            SupportedUICultures = [culture]
        });

        UseUploads(app);

        app.UseRouting();

        // After routing, so [EnableRateLimiting] on endpoints is visible.
        app.UseRateLimiter();

        // Authentication before the tenant middleware (it compares the token's tenant with the host's),
        // which in turn must run before authorization and the endpoints.
        app.UseAuthentication();
        app.UseMultiTenancy();
        app.UseAuthorization();

        app.UseAntiforgery();

        return app;
    }

    public static WebApplication MapWebHostEndpoints(this WebApplication app)
    {
        app.MapStaticAssets();
        app.MapHealthChecks("/healthz").AllowAnonymous();

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Features:ApiReference"))
        {
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference(options =>
            {
                options.Title = "ArtDash API";
                options.Theme = ScalarTheme.Mars;
                options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
                options.Authentication = new ScalarAuthenticationOptions { PreferredSecuritySchemes = ["Bearer"] };
            }).AllowAnonymous();
        }

        app.MapControllers();
        app.MapHub<RealtimeHub>(RealtimeEvents.HubPath);
        app.MapManifest();

        // AllowAnonymous on the page endpoints: the server never sees who is signed in (the access token
        // lives in the circuit, not in a cookie), so an [Authorize] page must not be challenged on a full
        // page load. Pages are still protected — AuthorizeRouteView enforces [Authorize] in the circuit,
        // and every API call is authorized on its own.
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .AddAdditionalAssemblies(typeof(ArtDash.UI.Client.Routes).Assembly)
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Serves uploaded files (logos, product pictures). <c>MapStaticAssets</c> only knows files that existed
    /// at build time, so runtime uploads need their own, narrowly scoped static-file middleware.
    /// </summary>
    private static void UseUploads(WebApplication app)
    {
        var storage = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
        var root = storage.ResolveRootPath(app.Environment.ContentRootPath);
        Directory.CreateDirectory(root);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            RequestPath = storage.RequestPath,
            OnPrepareResponse = context =>
            {
                var headers = context.Context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";

                // Names are unique (GUID v7) and files are never rewritten in place.
                headers.CacheControl = "public,max-age=2592000,immutable";

                // An SVG opened directly must not be able to run script on this origin.
                if (context.File.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
                }
            }
        });
    }

    private static bool IsCircuitCall(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address &&
        IPAddress.IsLoopback(address) &&
        context.Request.Headers.ContainsKey(TenantHeaders.TenantHost);
}
