using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ArtDash.Infrastructure.Identity;

/// <summary>Reads the signed-in user from the JWT of the current HTTP request.</summary>
internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirst(ArtDashClaimTypes.Subject)?.Value, out var id) ? id : null;

    public string? Name => Principal?.FindFirst(ArtDashClaimTypes.Name)?.Value;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ArtDashClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}
