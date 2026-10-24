using ArtDash.Shared.Contracts.Users;

namespace ArtDash.Application.Features.Users;

// ---------------------------------------------------------------- Queries

public sealed record SearchUsersQuery(UserQuery Filter) : IQuery<PagedResult<UserListItemDto>>;

internal sealed class SearchUsersQueryHandler(IApplicationDbContext db) : IQueryHandler<SearchUsersQuery, PagedResult<UserListItemDto>>
{
    public async Task<PagedResult<UserListItemDto>> Handle(SearchUsersQuery query, CancellationToken cancellationToken)
    {
        var filter = query.Filter;
        var term = filter.Search.ToSearchTerm();
        var role = string.IsNullOrWhiteSpace(filter.Role) ? null : filter.Role;

        return await db.Users.AsNoTracking()
            .WhereIf(term is not null, u => u.FullName.ToLower().Contains(term!) || (u.Email != null && u.Email.ToLower().Contains(term!)))
            .WhereIf(filter.IsActive is not null, u => u.IsActive == filter.IsActive)
            .Select(u => new
            {
                User = u,
                Role = db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .FirstOrDefault()
            })
            .WhereIf(role is not null, x => x.Role == role)
            .OrderBy(x => x.User.FullName)
            .Select(x => new UserListItemDto(
                x.User.Id,
                x.User.FullName,
                x.User.Email ?? string.Empty,
                x.User.PhoneNumber,
                x.Role ?? string.Empty,
                x.User.IsActive,
                x.User.CreatedAt,
                x.User.LastLoginAt))
            .ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }
}

public sealed record GetUserQuery(Guid Id) : IQuery<UserDto>;

internal sealed class GetUserQueryHandler(IApplicationDbContext db) : IQueryHandler<GetUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == query.Id, cancellationToken)
            ?? throw new NotFoundException("Usuário", query.Id);

        var role = await UserRoleRules.GetRoleAsync(db, user.Id, cancellationToken);
        var driver = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == user.Id, cancellationToken);

        return new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Phone = user.PhoneNumber,
            Role = role ?? string.Empty,
            IsActive = user.IsActive,
            LicenseNumber = driver?.LicenseNumber,
            LicenseCategory = driver?.LicenseCategory,
            LicenseExpiresOn = driver?.LicenseExpiresOn?.ToDateTime(TimeOnly.MinValue)
        };
    }
}

// ---------------------------------------------------------------- Commands

public sealed class SaveUserRequestValidator : AbstractValidator<SaveUserRequest>
{
    public SaveUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithName("Nome").MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(150).WithName("E-mail");
        RuleFor(x => x.Phone).Phone();
        RuleFor(x => x.Role).Must(r => AppRoles.All.Contains(r)).WithMessage("Perfil inválido.");
        RuleFor(x => x.LicenseNumber).MaximumLength(20);
        RuleFor(x => x.LicenseCategory).MaximumLength(5);
    }
}

public sealed record CreateUserCommand(SaveUserRequest Request) : ICommand<Guid>;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Request).SetValidator(new SaveUserRequestValidator());
        RuleFor(x => x.Request.Password).NotEmpty().MinimumLength(8).WithName("Senha");
    }
}

internal sealed class CreateUserCommandHandler(IApplicationDbContext db, IIdentityService identity, ICurrentUser currentUser)
    : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Guid> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        UserRoleRules.EnsureCanAssign(currentUser, request.Role);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var userId = await identity.CreateUserAsync(
            new NewIdentityUser(request.FullName.Trim(), request.Email.Trim(), request.Phone.NullIfWhiteSpace(), request.Password!, request.Role, request.IsActive),
            cancellationToken);

        await UserProfiles.SyncAsync(db, userId, request, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return userId;
    }
}

public sealed record UpdateUserCommand(Guid Id, SaveUserRequest Request) : ICommand;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator() => RuleFor(x => x.Request).SetValidator(new SaveUserRequestValidator());
}

internal sealed class UpdateUserCommandHandler(IApplicationDbContext db, IIdentityService identity, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<UpdateUserCommand>
{
    public async Task<Unit> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var currentRole = await UserRoleRules.GetRoleAsync(db, command.Id, cancellationToken);

        if (!await db.Users.AnyAsync(u => u.Id == command.Id, cancellationToken))
        {
            throw new NotFoundException("Usuário", command.Id);
        }

        UserRoleRules.EnsureCanManage(currentUser, currentRole);
        UserRoleRules.EnsureCanAssign(currentUser, request.Role);

        if (command.Id == currentUser.UserId && (!request.IsActive || request.Role != currentRole))
        {
            throw new DomainException("Você não pode desativar nem mudar o perfil do seu próprio usuário.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await identity.UpdateUserAsync(
            command.Id,
            new IdentityUserChanges(request.FullName.Trim(), request.Email.Trim(), request.Phone.NullIfWhiteSpace(), request.Role, request.IsActive),
            cancellationToken);

        await UserProfiles.SyncAsync(db, command.Id, request, cancellationToken);

        if (!request.IsActive)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var tokens = await db.RefreshTokens.Where(t => t.UserId == command.Id && t.RevokedAt == null).ToListAsync(cancellationToken);
            tokens.ForEach(t => t.Revoke(now));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record ResetUserPasswordCommand(Guid Id, string NewPassword) : ICommand;

public sealed class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator() => RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).WithName("Nova senha");
}

internal sealed class ResetUserPasswordCommandHandler(IApplicationDbContext db, IIdentityService identity, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<ResetUserPasswordCommand>
{
    public async Task<Unit> Handle(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        var role = await UserRoleRules.GetRoleAsync(db, command.Id, cancellationToken);
        UserRoleRules.EnsureCanManage(currentUser, role);

        await identity.ResetPasswordAsync(command.Id, command.NewPassword, cancellationToken);

        // Existing sessions end with the old password.
        var now = clock.GetUtcNow().UtcDateTime;
        var tokens = await db.RefreshTokens.Where(t => t.UserId == command.Id && t.RevokedAt == null).ToListAsync(cancellationToken);
        tokens.ForEach(t => t.Revoke(now));
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

// ---------------------------------------------------------------- Helpers

internal static class UserRoleRules
{
    public static async Task<string?> GetRoleAsync(IApplicationDbContext db, Guid userId, CancellationToken cancellationToken) =>
        await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Only administrators hand out back-office roles.</summary>
    public static void EnsureCanAssign(ICurrentUser currentUser, string role)
    {
        if (!AppRoles.All.Contains(role))
        {
            throw new RequestValidationException(nameof(SaveUserRequest.Role), "Perfil inválido.");
        }

        if (AppRoles.IsStaff(role) && !currentUser.IsAdmin())
        {
            throw new ForbiddenAccessException("Somente administradores podem criar ou promover administradores e gerentes.");
        }
    }

    /// <summary>Managers cannot change administrators or other managers.</summary>
    public static void EnsureCanManage(ICurrentUser currentUser, string? targetRole)
    {
        if (AppRoles.IsStaff(targetRole) && !currentUser.IsAdmin())
        {
            throw new ForbiddenAccessException("Somente administradores podem alterar administradores e gerentes.");
        }
    }
}

/// <summary>Keeps the role-specific records (driver profile, customer record) in step with the account.</summary>
internal static class UserProfiles
{
    public static async Task SyncAsync(IApplicationDbContext db, Guid userId, SaveUserRequest request, CancellationToken cancellationToken)
    {
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.UserId == userId, cancellationToken);

        if (request.Role == AppRoles.Driver)
        {
            if (driver is null)
            {
                driver = new Driver { UserId = userId };
                db.Drivers.Add(driver);
            }

            driver.Name = request.FullName.Trim();
            driver.Phone = request.Phone.NullIfWhiteSpace();
            driver.LicenseNumber = request.LicenseNumber.NullIfWhiteSpace();
            driver.LicenseCategory = request.LicenseCategory.NullIfWhiteSpace()?.ToUpperInvariant();
            driver.LicenseExpiresOn = request.LicenseExpiresOn is { } expires ? DateOnly.FromDateTime(expires) : null;
            driver.IsActive = request.IsActive;
        }
        else if (driver is not null)
        {
            driver.IsActive = false;
        }

        if (request.Role == AppRoles.Customer)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken)
                ?? await db.Customers.FirstOrDefaultAsync(c => c.UserId == null && c.Email == email, cancellationToken);

            if (customer is null)
            {
                customer = new Customer { Type = CustomerType.Individual };
                db.Customers.Add(customer);
            }

            customer.UserId = userId;
            customer.Name = request.FullName.Trim();
            customer.Email = email;
            customer.Phone = request.Phone.NullIfWhiteSpace() ?? customer.Phone;
            customer.IsActive = request.IsActive;
        }
    }
}
