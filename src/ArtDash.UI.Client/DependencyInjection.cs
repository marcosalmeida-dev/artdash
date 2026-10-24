using ArtDash.UI.Client.Auth;
using ArtDash.UI.Client.Services;
using ArtDash.UI.Client.Services.Api;
using ArtDash.UI.Client.Theming;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace ArtDash.UI.Client;

public static class DependencyInjection
{
    /// <summary>
    /// Client services. Everything is scoped — in Blazor Server a scope is one circuit, i.e. one browser
    /// tab — except the API base address, which is the same for every circuit.
    /// </summary>
    public static IServiceCollection AddUiClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
            config.SnackbarConfiguration.PreventDuplicates = true;
            config.SnackbarConfiguration.NewestOnTop = true;
            config.SnackbarConfiguration.ShowCloseIcon = true;
            config.SnackbarConfiguration.VisibleStateDuration = 4500;
            config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
        });

        services.AddLocalization();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();

        // Session: the access token lives here, in the circuit; the refresh token in the browser's cookie.
        services.AddScoped<BrowserAuthClient>();
        services.AddScoped<AuthSession>();
        services.AddScoped<AppAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthenticationStateProvider>());

        // REST API
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));
        services.AddSingleton<ApiBaseAddress>();
        services.AddScoped<ApiClient>();
        services.AddScoped<StoreApi>();
        services.AddScoped<BrandingApi>();
        services.AddScoped<SettingsApi>();
        services.AddScoped<AccountApi>();
        services.AddScoped<CatalogApi>();
        services.AddScoped<CustomersApi>();
        services.AddScoped<OrdersApi>();
        services.AddScoped<MeApi>();
        services.AddScoped<PaymentsApi>();
        services.AddScoped<TrackingApi>();
        services.AddScoped<FleetApi>();
        services.AddScoped<DeliveriesApi>();
        services.AddScoped<DriverApi>();
        services.AddScoped<UsersApi>();
        services.AddScoped<DashboardApi>();
        services.AddScoped<ReportsApi>();

        // Circuit state and browser services
        services.AddScoped<BrowserStorage>();
        services.AddScoped<BrowserUtil>();
        services.AddScoped<BrandingState>();
        services.AddScoped<ThemePreference>();
        services.AddScoped<StoreState>();
        services.AddScoped<CartService>();
        services.AddScoped<RealtimeClient>();
        services.AddScoped<GeolocationService>();
        services.AddScoped<AddressLookup>();
        services.AddScoped<UiActions>();

        return services;
    }
}
