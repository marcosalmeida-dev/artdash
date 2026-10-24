namespace ArtDash.Shared.Contracts.Users;

public sealed record UserListItemDto(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string Role,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

public sealed record UserDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string Role { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string? LicenseNumber { get; init; }
    public string? LicenseCategory { get; init; }
    public DateTime? LicenseExpiresOn { get; init; }
}

/// <summary>
/// Creates or updates a user. One role per user. When the role is Driver the license fields are used
/// to keep the driver record; when it is Customer a customer record is linked to the account.
/// </summary>
public sealed class SaveUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;

    /// <summary>Required on create, ignored on update (use the reset-password action).</summary>
    public string? Password { get; set; }

    public bool IsActive { get; set; } = true;
    public string? LicenseNumber { get; set; }
    public string? LicenseCategory { get; set; }
    public DateTime? LicenseExpiresOn { get; set; }

    public static SaveUserRequest From(UserDto dto) => new()
    {
        FullName = dto.FullName,
        Email = dto.Email,
        Phone = dto.Phone,
        Role = dto.Role,
        IsActive = dto.IsActive,
        LicenseNumber = dto.LicenseNumber,
        LicenseCategory = dto.LicenseCategory,
        LicenseExpiresOn = dto.LicenseExpiresOn
    };
}

public sealed class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class UserQuery
{
    public string? Search { get; set; }
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
