using ArtDash.Infrastructure.Options;
using ArtDash.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Identity;

namespace ArtDash.Infrastructure.Identity;

/// <summary>
/// Password sign-in, customer self-registration and refresh-token rotation. Every lookup goes through the
/// tenant-filtered DbContext, so an e-mail registered at one tenant does not exist for another.
/// </summary>
internal sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IApplicationDbContext db,
    ICurrentTenant currentTenant,
    TokenService tokenService,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider clock) : IAuthService
{
    private const string InvalidCredentials = "E-mail ou senha inválidos.";

    // A refresh token reused within this window right after being rotated is treated as a race between
    // two tabs (answered 409, client retries with the new cookie), not as theft.
    private static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(60);

    public async Task<AuthResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        currentTenant.RequiredId();

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedAccessException(InvalidCredentials);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            throw new UnauthorizedAccessException("Conta bloqueada temporariamente por excesso de tentativas. Tente novamente em alguns minutos.");
        }

        if (!result.Succeeded)
        {
            throw new UnauthorizedAccessException(InvalidCredentials);
        }

        user.LastLoginAt = clock.GetUtcNow().UtcDateTime;
        await userManager.UpdateAsync(user);

        return await IssueAsync(user, ipAddress, cancellationToken);
    }

    public async Task<AuthResult> RegisterCustomerAsync(RegisterCustomerRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.RequiredId();
        ValidateRegistration(request);

        var email = request.Email.Trim().ToLowerInvariant();
        var now = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var user = new ApplicationUser
        {
            TenantId = tenantId,
            UserName = email,
            Email = email,
            FullName = request.FullName.Trim(),
            PhoneNumber = request.Phone.NullIfEmpty(),
            CreatedAt = now,
            IsActive = true
        };

        IdentityErrors.ThrowIfFailed(await userManager.CreateAsync(user, request.Password));
        IdentityErrors.ThrowIfFailed(await userManager.AddToRoleAsync(user, AppRoles.Customer));

        // Link to a customer record staff may already have created with this e-mail.
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.UserId == null && c.Email == email, cancellationToken);
        if (customer is null)
        {
            customer = new Customer { Type = CustomerType.Individual, Name = user.FullName, Email = email };
            db.Customers.Add(customer);
        }

        customer.UserId = user.Id;
        customer.Phone ??= user.PhoneNumber;
        customer.Document ??= Customer.NormalizeDocument(request.Document);

        await db.SaveChangesAsync(cancellationToken);
        var result = await IssueAsync(user, ipAddress, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken)
    {
        currentTenant.RequiredId();

        var hash = TokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            ?? throw new UnauthorizedAccessException("Sessão expirada. Entre novamente.");

        var now = clock.GetUtcNow().UtcDateTime;

        if (stored.RevokedAt is { } revokedAt)
        {
            if (stored.ReplacedByHash is not null && now - revokedAt < RotationGracePeriod)
            {
                throw new ConflictException("A sessão acabou de ser renovada. Tente novamente.");
            }

            // An old token came back: assume it leaked and end every session of this user.
            await RevokeAllAsync(stored.UserId, now, cancellationToken);
            throw new UnauthorizedAccessException("Sessão expirada. Entre novamente.");
        }

        if (stored.ExpiresAt <= now)
        {
            throw new UnauthorizedAccessException("Sessão expirada. Entre novamente.");
        }

        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("Sessão expirada. Entre novamente.");
        }

        var result = await IssueAsync(user, ipAddress, cancellationToken, rotated: stored);
        return result;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || !currentTenant.IsResolved)
        {
            return;
        }

        var hash = TokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is not null)
        {
            stored.Revoke(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (request.NewPassword != request.ConfirmNewPassword)
        {
            throw new RequestValidationException(nameof(ChangePasswordRequest.ConfirmNewPassword), "As senhas não conferem.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("Usuário", userId);

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded && result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            throw new RequestValidationException(nameof(ChangePasswordRequest.CurrentPassword), "A senha atual está incorreta.");
        }

        IdentityErrors.ThrowIfFailed(result);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("Usuário", userId);

        var roles = await userManager.GetRolesAsync(user);
        return ToDto(user, roles);
    }

    private async Task<AuthResult> IssueAsync(ApplicationUser user, string? ipAddress, CancellationToken cancellationToken, RefreshToken? rotated = null)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (accessToken, expiresAt) = tokenService.CreateAccessToken(user, roles, currentTenant.Slug ?? string.Empty);

        var now = clock.GetUtcNow().UtcDateTime;
        var refreshToken = TokenService.GenerateRefreshToken();
        var refreshHash = TokenService.Hash(refreshToken);
        var refreshExpiresAt = now.AddDays(Math.Max(1, jwtOptions.Value.RefreshTokenDays));

        rotated?.Revoke(now, refreshHash);
        db.RefreshTokens.Add(new RefreshToken(user.Id, refreshHash, now, refreshExpiresAt, ipAddress));
        await db.SaveChangesAsync(cancellationToken);

        return new AuthResult(new AuthTokenResponse(accessToken, expiresAt, ToDto(user, roles)), refreshToken, refreshExpiresAt);
    }

    private async Task RevokeAllAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(cancellationToken);
        active.ForEach(t => t.Revoke(now));
        await db.SaveChangesAsync(cancellationToken);
    }

    private CurrentUserDto ToDto(ApplicationUser user, IEnumerable<string> roles) =>
        new(user.Id, user.FullName, user.Email ?? string.Empty, user.PhoneNumber, roles.ToList(), user.TenantId, currentTenant.Slug ?? string.Empty);

    private static void ValidateRegistration(RegisterCustomerRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 3)
        {
            errors[nameof(request.FullName)] = ["Informe seu nome completo."];
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            errors[nameof(request.Email)] = ["Informe um e-mail válido."];
        }

        if (request.Password != request.ConfirmPassword)
        {
            errors[nameof(request.ConfirmPassword)] = ["As senhas não conferem."];
        }

        if (!string.IsNullOrWhiteSpace(request.Document) &&
            !ArtDash.Application.Common.Validation.ValidationRules.IsValidCpfOrCnpj(request.Document))
        {
            errors[nameof(request.Document)] = ["CPF/CNPJ inválido."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }
}

internal static class InfrastructureStringExtensions
{
    public static string? NullIfEmpty(this string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
