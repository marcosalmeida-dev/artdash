using ArtDash.Shared.Contracts.Auth;
using ArtDash.Shared.Contracts.Deliveries;
using ArtDash.Shared.Contracts.Realtime;

namespace ArtDash.Application.Common.Interfaces;

/// <summary>Hands out per-tenant sequential numbers.</summary>
/// <remarks>
/// Saves immediately (it must, to be safe under concurrency) — call it before adding anything else to
/// the unit of work, or those changes are saved with it.
/// </remarks>
public interface ISequenceGenerator
{
    Task<long> NextAsync(string sequenceName, CancellationToken cancellationToken);
}

/// <summary>Stores uploaded files (logos, product images) and returns their public URL.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string fileName, string contentType, string folder, CancellationToken cancellationToken);

    Task DeleteAsync(string? url, CancellationToken cancellationToken);
}

/// <summary>Encrypts secrets at rest (payment gateway credentials).</summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    string? Unprotect(string? protectedText);
}

/// <summary>Absolute URLs that leave the application (webhooks, tracking links).</summary>
public interface IAppUrls
{
    /// <summary>Null when the app has no public HTTPS address (e.g. localhost) — gateways reject those.</summary>
    string? MercadoPagoWebhookUrl(string tenantSlug);

    string TrackingUrl(string trackingCode);
}

/// <summary>Pushes live updates to connected browsers.</summary>
public interface IRealtimeNotifier
{
    Task OrderChangedAsync(Guid tenantId, OrderChangedMessage message, CancellationToken cancellationToken = default);

    Task PositionUpdatedAsync(Guid tenantId, VehiclePositionDto position, IReadOnlyCollection<Guid> trackedOrderIds, CancellationToken cancellationToken = default);

    Task DeliveryChangedAsync(Guid tenantId, DeliveryChangedMessage message, CancellationToken cancellationToken = default);
}

/// <summary>User administration on top of ASP.NET Core Identity.</summary>
public interface IIdentityService
{
    Task<Guid> CreateUserAsync(NewIdentityUser user, CancellationToken cancellationToken);

    Task UpdateUserAsync(Guid userId, IdentityUserChanges changes, CancellationToken cancellationToken);

    Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken);
}

public sealed record NewIdentityUser(string FullName, string Email, string? Phone, string Password, string Role, bool IsActive);

public sealed record IdentityUserChanges(string FullName, string Email, string? Phone, string Role, bool IsActive);

/// <summary>Sign-in, registration and token rotation.</summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<AuthResult> RegisterCustomerAsync(RegisterCustomerRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<AuthResult> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);

    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);

    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken);

    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Tokens issued by a successful sign-in or refresh. The refresh token goes into a cookie.</summary>
public sealed record AuthResult(AuthTokenResponse Response, string RefreshToken, DateTime RefreshTokenExpiresAt);
