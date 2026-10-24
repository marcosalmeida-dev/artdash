namespace ArtDash.Shared.Contracts.Auth;

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterCustomerRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Document { get; set; }
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public sealed record CurrentUserDto(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    IReadOnlyList<string> Roles,
    Guid TenantId,
    string TenantSlug);

/// <summary>
/// Returned by login, register and refresh. The refresh token never appears here: it travels only
/// in an HttpOnly cookie scoped to the auth endpoints.
/// </summary>
public sealed record AuthTokenResponse(string AccessToken, DateTime ExpiresAt, CurrentUserDto User);
