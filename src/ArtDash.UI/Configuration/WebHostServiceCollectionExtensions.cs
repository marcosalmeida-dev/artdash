using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using ArtDash.Shared.Json;
using ArtDash.UI.Controllers;
using ArtDash.UI.Hubs;
using ArtDash.UI.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;

namespace ArtDash.UI.Configuration;

/// <summary>Web-layer services: API, problem details, SignalR, rate limits, Blazor Server.</summary>
public static class WebHostServiceCollectionExtensions
{
    public static IServiceCollection AddWebHost(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // One JSON contract everywhere (camelCase, enums as strings): MVC, problem details, minimal endpoints, SignalR.
        services.AddControllers()
            .AddJsonOptions(options => JsonDefaults.Apply(options.JsonSerializerOptions));
        services.ConfigureHttpJsonOptions(options => JsonDefaults.Apply(options.SerializerOptions));

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

        services.AddSignalR(options => options.AddFilter<TenantHubFilter>())
            .AddJsonProtocol(options => JsonDefaults.Apply(options.PayloadSerializerOptions));
        services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

        AddRateLimits(services);

        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json", "application/manifest+json"]);
        });

        services.AddHealthChecks();

        services.Configure<ForwardedHeadersSettings>(configuration.GetSection(ForwardedHeadersSettings.SectionName));

        services.AddRazorComponents()
            .AddInteractiveServerComponents(options => options.DetailedErrors = environment.IsDevelopment());

        return services;
    }

    private static void AddRateLimits(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Muitas tentativas",
                    Detail = "Aguarde um instante e tente novamente."
                };

                await context.HttpContext.Response.WriteAsJsonAsync(
                    problem, (JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
            };

            options.AddPolicy(RateLimitPolicies.Auth, context => PerClient(context, permitLimit: 10, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.Refresh, context => PerClient(context, permitLimit: 30, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.Public, context => PerClient(context, permitLimit: 60, TimeSpan.FromMinutes(1)));
        });
    }

    /// <summary>
    /// A fixed window per client IP. Loopback is exempt: the Blazor circuit calls the API from the server
    /// on behalf of every user, and one shared bucket would throttle all of them together. Behind a reverse
    /// proxy, enable "ForwardedHeaders" so the real client address is what gets partitioned.
    /// </summary>
    private static RateLimitPartition<string> PerClient(HttpContext context, int permitLimit, TimeSpan window)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null || IPAddress.IsLoopback(address))
        {
            return RateLimitPartition.GetNoLimiter("loopback");
        }

        return RateLimitPartition.GetFixedWindowLimiter(address.ToString(), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}

/// <summary>"ForwardedHeaders" section: opt-in trust of X-Forwarded-* from a reverse proxy.</summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; set; }

    /// <summary>Accept the headers from any source. Only when the app is reachable exclusively through the proxy.</summary>
    public bool TrustAnyProxy { get; set; }

    public List<string> KnownProxies { get; set; } = [];

    /// <summary>CIDR ranges, e.g. <c>10.0.0.0/8</c>.</summary>
    public List<string> KnownNetworks { get; set; } = [];

    public int ForwardLimit { get; set; } = 1;

    public ForwardedHeadersOptions ToOptions()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            ForwardLimit = Math.Max(1, ForwardLimit)
        };

        if (TrustAnyProxy)
        {
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            return options;
        }

        foreach (var proxy in KnownProxies)
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }

        foreach (var network in KnownNetworks)
        {
            if (System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                options.KnownIPNetworks.Add(parsed);
            }
        }

        return options;
    }
}
