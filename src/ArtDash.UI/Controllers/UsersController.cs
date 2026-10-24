using ArtDash.Application.Features.Dashboard;
using ArtDash.Application.Features.Users;
using ArtDash.Shared.Contracts.Dashboard;
using ArtDash.Shared.Contracts.Users;

namespace ArtDash.UI.Controllers;

/// <summary>User administration: staff, drivers and customer logins of the current tenant.</summary>
[Route(ApiRoutes.Users)]
[Authorize(Roles = AppRoles.Admin)]
public sealed class UsersController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserListItemDto>> Search([FromQuery] UserQuery filter, CancellationToken cancellationToken) =>
        sender.Send(new SearchUsersQuery(filter), cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<UserDto> Get(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetUserQuery(id), cancellationToken);

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(SaveUserRequest request, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new CreateUserCommand(request), cancellationToken);
        return CreatedWithId(nameof(Get), id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveUserRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new UpdateUserCommand(id, request), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new ResetUserPasswordCommand(id, request.NewPassword), cancellationToken);
        return NoContent();
    }
}

[Route(ApiRoutes.Dashboard)]
[Authorize(Roles = AppRoles.Staff)]
public sealed class DashboardController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<DashboardDto> Get(CancellationToken cancellationToken) =>
        sender.Send(new GetDashboardQuery(), cancellationToken);
}
