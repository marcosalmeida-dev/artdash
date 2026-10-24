using Microsoft.AspNetCore.Components.Authorization;

namespace ArtDash.UI.Client.Auth;

/// <summary>
/// Authentication state for the components, backed by <see cref="AuthSession"/>. The first call waits for
/// the session to be restored from the refresh cookie, so AuthorizeRouteView never flashes "denied" at a
/// user who is in fact signed in.
/// </summary>
public sealed class AppAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly AuthSession _session;

    public AppAuthenticationStateProvider(AuthSession session)
    {
        _session = session;
        _session.UserChanged += OnUserChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await _session.EnsureRestoredAsync();
        return new AuthenticationState(_session.Principal);
    }

    private void OnUserChanged() =>
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_session.Principal)));

    public void Dispose() => _session.UserChanged -= OnUserChanged;
}

/// <summary>Role and identity helpers for the signed-in principal.</summary>
public static class UserExtensions
{
    public static bool IsSignedIn(this ClaimsPrincipal? user) => user?.Identity?.IsAuthenticated == true;

    public static bool IsAdmin(this ClaimsPrincipal? user) => user?.IsInRole(AppRoles.Admin) == true;

    public static bool IsStaff(this ClaimsPrincipal? user) =>
        user?.IsInRole(AppRoles.Admin) == true || user?.IsInRole(AppRoles.Manager) == true;

    public static bool IsDriver(this ClaimsPrincipal? user) => user?.IsInRole(AppRoles.Driver) == true;

    public static bool IsCustomer(this ClaimsPrincipal? user) => user?.IsInRole(AppRoles.Customer) == true;

    public static string DisplayName(this ClaimsPrincipal? user) =>
        user?.FindFirst(ArtDashClaimTypes.Name)?.Value ?? string.Empty;

    /// <summary>The most specific role (an Admin is shown as Admin, not as anything else).</summary>
    public static string? MainRole(this ClaimsPrincipal? user) =>
        user.IsAdmin() ? AppRoles.Admin
        : user?.IsInRole(AppRoles.Manager) == true ? AppRoles.Manager
        : user.IsDriver() ? AppRoles.Driver
        : user.IsCustomer() ? AppRoles.Customer
        : null;

    /// <summary>Up to two initials for the avatar — "Maria Silva" becomes MS.</summary>
    public static string Initials(this ClaimsPrincipal? user)
    {
        var parts = user.DisplayName().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => char.ToUpperInvariant(parts[0][0]).ToString(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}"
        };
    }
}

/// <summary>Where each kind of user starts, and safe return URLs.</summary>
public static class LandingRoutes
{
    public const string Storefront = "/";
    public const string Admin = "/admin";
    public const string Driver = "/motorista";
    public const string Login = "/entrar";

    public static string For(ClaimsPrincipal? user) =>
        user.IsStaff() ? Admin : user.IsDriver() ? Driver : Storefront;

    public static string LoginFor(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl) || returnUrl == "/"
            ? Login
            : $"{Login}?returnUrl={Uri.EscapeDataString(returnUrl)}";

    /// <summary>Only same-site relative paths: never redirect a user to another origin after sign-in.</summary>
    public static string? SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) &&
        returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
        !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : null;
}
