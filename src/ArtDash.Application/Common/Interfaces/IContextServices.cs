namespace ArtDash.Application.Common.Interfaces;

/// <summary>The tenant the current request belongs to (resolved by host, header or token).</summary>
public interface ICurrentTenant
{
    Guid? Id { get; }
    string? Slug { get; }
    bool IsResolved { get; }
}

/// <summary>Sets the current tenant. Used by the resolution middleware, hubs, webhooks and seeding.</summary>
public interface ICurrentTenantSetter
{
    void Set(Guid tenantId, string slug);
}

/// <summary>The signed-in user of the current request.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Name { get; }
    bool IsAuthenticated { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool IsInRole(string role);
}

public static class ContextServiceExtensions
{
    public static Guid RequiredId(this ICurrentTenant tenant) =>
        tenant.Id ?? throw new InvalidOperationException("Nenhuma empresa (tenant) foi identificada para esta requisição.");

    public static Guid RequiredUserId(this ICurrentUser user) =>
        user.UserId ?? throw new ForbiddenAccessException("É preciso estar autenticado.");

    public static bool IsStaff(this ICurrentUser user) =>
        user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Manager);

    public static bool IsAdmin(this ICurrentUser user) => user.IsInRole(AppRoles.Admin);

    /// <summary>Name written to audit fields and status history.</summary>
    public static string AuditName(this ICurrentUser user) =>
        string.IsNullOrWhiteSpace(user.Name) ? "sistema" : user.Name;
}
