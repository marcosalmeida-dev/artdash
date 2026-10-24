using Microsoft.AspNetCore.Identity;

namespace ArtDash.Domain.Identity;

/// <summary>
/// A login. Belongs to exactly one tenant: the tenant query filter applies to users too, which makes
/// every Identity lookup (by e-mail, by user name) tenant-scoped without a custom store.
/// The user name is the e-mail.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, ITenantEntity
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public Guid TenantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
        Id = Guid.CreateVersion7();
    }

    public ApplicationRole(string roleName) : base(roleName)
    {
        Id = Guid.CreateVersion7();
    }

    public string? Description { get; set; }
}

/// <summary>
/// A refresh token, stored hashed. Rotated on every use; the previous token keeps a pointer to its
/// replacement so a concurrent refresh from another tab can be told apart from a stolen token.
/// </summary>
public sealed class RefreshToken : BaseEntity, ITenantEntity
{
    private RefreshToken()
    {
    }

    public RefreshToken(Guid userId, string tokenHash, DateTime createdAt, DateTime expiresAt, string? createdByIp)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedByIp = createdByIp;
    }

    public Guid TenantId { get; set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public string? CreatedByIp { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? ReplacedByHash { get; private set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTime now, string? replacedByHash = null)
    {
        RevokedAt ??= now;
        ReplacedByHash ??= replacedByHash;
    }
}
