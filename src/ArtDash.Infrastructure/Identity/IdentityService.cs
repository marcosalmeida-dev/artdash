using Microsoft.AspNetCore.Identity;

namespace ArtDash.Infrastructure.Identity;

/// <summary>User administration (back office) on top of <see cref="UserManager{TUser}"/>.</summary>
internal sealed class IdentityService(UserManager<ApplicationUser> userManager, ICurrentTenant currentTenant, TimeProvider clock)
    : IIdentityService
{
    public async Task<Guid> CreateUserAsync(NewIdentityUser newUser, CancellationToken cancellationToken)
    {
        var email = newUser.Email.Trim().ToLowerInvariant();
        var user = new ApplicationUser
        {
            TenantId = currentTenant.RequiredId(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = newUser.FullName,
            PhoneNumber = newUser.Phone,
            IsActive = newUser.IsActive,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };

        IdentityErrors.ThrowIfFailed(await userManager.CreateAsync(user, newUser.Password));
        IdentityErrors.ThrowIfFailed(await userManager.AddToRoleAsync(user, newUser.Role));
        return user.Id;
    }

    public async Task UpdateUserAsync(Guid userId, IdentityUserChanges changes, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("Usuário", userId);

        var email = changes.Email.Trim().ToLowerInvariant();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            IdentityErrors.ThrowIfFailed(await userManager.SetEmailAsync(user, email));
            IdentityErrors.ThrowIfFailed(await userManager.SetUserNameAsync(user, email));
            user.EmailConfirmed = true;
        }

        user.FullName = changes.FullName;
        user.PhoneNumber = changes.Phone;
        user.IsActive = changes.IsActive;
        IdentityErrors.ThrowIfFailed(await userManager.UpdateAsync(user));

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Count != 1 || roles[0] != changes.Role)
        {
            if (roles.Count > 0)
            {
                IdentityErrors.ThrowIfFailed(await userManager.RemoveFromRolesAsync(user, roles));
            }

            IdentityErrors.ThrowIfFailed(await userManager.AddToRoleAsync(user, changes.Role));
        }

        // Role or activation changes must take effect on the next token refresh.
        await userManager.UpdateSecurityStampAsync(user);
    }

    public async Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("Usuário", userId);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        IdentityErrors.ThrowIfFailed(await userManager.ResetPasswordAsync(user, token, newPassword));
        await userManager.SetLockoutEndDateAsync(user, null);
    }
}
