namespace ArtDash.Shared.Constants;

/// <summary>
/// The four roles the system knows about. Roles are global (shared by every tenant);
/// users are not — each user belongs to exactly one tenant.
/// </summary>
public static class AppRoles
{
    public const string Admin = nameof(Admin);
    public const string Manager = nameof(Manager);
    public const string Customer = nameof(Customer);
    public const string Driver = nameof(Driver);

    /// <summary>Back-office users. Comma-separated so it can be used in <c>[Authorize(Roles = ...)]</c>.</summary>
    public const string Staff = Admin + "," + Manager;

    public static readonly IReadOnlyList<string> All = [Admin, Manager, Customer, Driver];

    public static bool IsStaff(string? role) => role is Admin or Manager;
}
