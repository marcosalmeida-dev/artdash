using ArtDash.Shared.Contracts.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace ArtDash.UI.Controllers;

/// <summary>
/// Sign-in, sign-up, token refresh and sign-out. The access token travels in the response body; the
/// refresh token only ever in an HttpOnly cookie scoped to this controller's path, so no script can
/// read it. The Blazor client calls these endpoints from the browser (fetch), never from the circuit,
/// so the cookie is stored by — and sent from — the browser.
/// </summary>
[Route(ApiRoutes.Auth)]
public sealed class AuthController(IAuthService auth, ICurrentUser currentUser) : ApiControllerBase
{
    public const string RefreshCookieName = "artdash_rt";
    private const string CookiePath = "/" + ApiRoutes.Auth;

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthTokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, ClientIp, cancellationToken);
        return Issue(result);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthTokenResponse>> Register(RegisterCustomerRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.RegisterCustomerAsync(request, ClientIp, cancellationToken);
        return Issue(result);
    }

    /// <summary>Trades the refresh cookie for a new access token (and rotates the cookie).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<ActionResult<AuthTokenResponse>> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Não autenticado", detail: "Nenhuma sessão ativa.");
        }

        try
        {
            var result = await auth.RefreshAsync(refreshToken, ClientIp, cancellationToken);
            return Issue(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            // The session is over for good: drop the dead cookie so the browser stops sending it.
            // Answered here rather than rethrown — the exception handler clears the response
            // headers, which would take the cookie deletion with them.
            DeleteRefreshCookie();
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Não autenticado", detail: ex.Message);
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookieName], cancellationToken);
        DeleteRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public Task<CurrentUserDto> Me(CancellationToken cancellationToken) =>
        auth.GetCurrentUserAsync(currentUser.RequiredUserId(), cancellationToken);

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.ChangePasswordAsync(currentUser.RequiredUserId(), request, cancellationToken);
        return NoContent();
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private ActionResult<AuthTokenResponse> Issue(AuthResult result)
    {
        Response.Cookies.Append(RefreshCookieName, result.RefreshToken, BuildCookieOptions(result.RefreshTokenExpiresAt));
        Response.Headers.CacheControl = "no-store";
        return Ok(result.Response);
    }

    private void DeleteRefreshCookie() => Response.Cookies.Delete(RefreshCookieName, BuildCookieOptions(expires: null));

    private CookieOptions BuildCookieOptions(DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        IsEssential = true,
        Expires = expires is null ? null : new DateTimeOffset(DateTime.SpecifyKind(expires.Value, DateTimeKind.Utc))
    };
}
