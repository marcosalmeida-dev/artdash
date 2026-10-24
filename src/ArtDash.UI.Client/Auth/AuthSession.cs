using ArtDash.Shared.Contracts.Auth;
using ArtDash.UI.Client.Services.Api;
using Microsoft.Extensions.Logging;

namespace ArtDash.UI.Client.Auth;

/// <summary>
/// The circuit's session: the access token (kept in memory only — never in localStorage), the signed-in
/// user, and the refresh logic behind them. The long-lived credential is the HttpOnly refresh cookie in
/// the browser; on every page load the session is restored by trading that cookie for a fresh token.
/// <para>
/// Refreshes are single-flighted (ported from artstore-fs' TokenRefreshCoordinator): a page easily
/// fires several API calls at once, and letting each rotate the refresh token would make all but one
/// present a token the server already spent — which the server reads as theft.
/// </para>
/// </summary>
public sealed class AuthSession(BrowserAuthClient browser, ILogger<AuthSession> logger) : IAsyncDisposable
{
    private const string LoginPath = "/" + ApiRoutes.Auth + "/login";
    private const string RegisterPath = "/" + ApiRoutes.Auth + "/register";
    private const string RefreshPath = "/" + ApiRoutes.Auth + "/refresh";
    private const string LogoutPath = "/" + ApiRoutes.Auth + "/logout";

    /// <summary>Renew this far ahead of expiry so requests never race the deadline.</summary>
    private static readonly TimeSpan RefreshAhead = TimeSpan.FromMinutes(2);

    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _restoreLock = new();
    private Task? _restoreTask;
    private string? _accessToken;
    private DateTime _expiresAtUtc;
    private CancellationTokenSource? _keepAlive;

    /// <summary>Raised when the signed-in user changes (sign-in, sign-out, session lost). Not on plain token renewals.</summary>
    public event Action? UserChanged;

    public ClaimsPrincipal Principal { get; private set; } = Anonymous;

    public CurrentUserDto? User { get; private set; }

    public bool IsAuthenticated => User is not null;

    /// <summary>Restores the session from the refresh cookie, once per circuit.</summary>
    public Task EnsureRestoredAsync()
    {
        lock (_restoreLock)
        {
            return _restoreTask ??= RestoreAsync();
        }
    }

    /// <summary>
    /// A token good for at least the next couple of minutes, renewed first when needed.
    /// Null means "call the API anonymously".
    /// </summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        await EnsureRestoredAsync();

        if (_accessToken is null || !IsExpiring())
        {
            return _accessToken;
        }

        await _gate.WaitAsync();
        try
        {
            // Re-check inside the lock: whoever held it before may already have renewed.
            return _accessToken is null || !IsExpiring() ? _accessToken : await RefreshLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Called after the API answered 401 to <paramref name="staleToken"/> (a revoked session, or a token
    /// that expired in flight). Returns a replacement to retry with, or null when the session is over.
    /// </summary>
    public async Task<string?> RefreshAfterUnauthorizedAsync(string staleToken)
    {
        await _gate.WaitAsync();
        try
        {
            // Another request already replaced the rejected token: use that one instead of rotating again.
            if (_accessToken is not null && !string.Equals(_accessToken, staleToken, StringComparison.Ordinal) && !IsExpired())
            {
                return _accessToken;
            }

            return await RefreshLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<AuthOutcome> LoginAsync(LoginRequest request) => SignInAsync(LoginPath, request);

    /// <summary>Creates a customer account and signs it in (the endpoint already returns a session).</summary>
    public Task<AuthOutcome> RegisterAsync(RegisterCustomerRequest request) => SignInAsync(RegisterPath, request);

    /// <summary>Revokes the session server-side (best effort) and always signs out locally.</summary>
    public async Task LogoutAsync()
    {
        try
        {
            await browser.SendAsync(LogoutPath, "POST");
        }
        finally
        {
            await _gate.WaitAsync();
            try
            {
                ClearSession();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>
    /// Keeps a signed-in tab alive: renews the token ahead of expiry even while the user is idle, and
    /// notices a session revoked elsewhere. Started once by the layout.
    /// </summary>
    public void StartKeepAlive()
    {
        if (_keepAlive is not null)
        {
            return;
        }

        _keepAlive = new CancellationTokenSource();
        _ = KeepAliveLoopAsync(_keepAlive.Token);
    }

    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_accessToken is not null)
                {
                    await GetAccessTokenAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Session keep-alive stopped");
        }
    }

    private async Task RestoreAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await RefreshLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AuthOutcome> SignInAsync(string path, object request)
    {
        var response = await browser.SendAsync(path, "POST", request);

        if (response.IsSuccess && BrowserAuthClient.Read<AuthTokenResponse>(response) is { } result)
        {
            await _gate.WaitAsync();
            try
            {
                SetSession(result);
            }
            finally
            {
                _gate.Release();
            }

            return AuthOutcome.Succeeded(result.User);
        }

        if (response.IsTransportFailure)
        {
            return AuthOutcome.Failed("Auth.NetworkError");
        }

        if (response.Status == StatusCodes429)
        {
            return AuthOutcome.Failed("Auth.TooManyAttempts");
        }

        var problem = BrowserAuthClient.Read<ApiProblem>(response);
        return AuthOutcome.Failed(problem?.Detail ?? problem?.Title ?? "Auth.Failed", problem?.Errors);
    }

    /// <summary>Trades the refresh cookie for a new access token. The caller holds <see cref="_gate"/>.</summary>
    private async Task<string?> RefreshLockedAsync()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await browser.SendAsync(RefreshPath, "POST");

            if (response.IsSuccess)
            {
                var result = BrowserAuthClient.Read<AuthTokenResponse>(response);
                if (result is null)
                {
                    return _accessToken;
                }

                SetSession(result);
                return result.AccessToken;
            }

            // 409: this tab lost a rotation race with another one. The browser already holds the
            // winner's cookie, so one retry succeeds.
            if (response.Status == 409)
            {
                await Task.Delay(200);
                continue;
            }

            // 401 is the only definitive answer: the session is over.
            if (response.Status == 401)
            {
                ClearSession();
                return null;
            }

            // Offline, throttled or a server error: the session may still be fine. Keep what we have.
            return _accessToken;
        }

        return _accessToken;
    }

    private void SetSession(AuthTokenResponse result)
    {
        var previous = User;

        _accessToken = result.AccessToken;
        _expiresAtUtc = result.ExpiresAt.Kind == DateTimeKind.Utc ? result.ExpiresAt : result.ExpiresAt.ToUniversalTime();
        User = result.User;
        Principal = SessionPrincipal.From(result.User);

        if (!SessionPrincipal.SameUser(previous, result.User))
        {
            UserChanged?.Invoke();
        }
    }

    private void ClearSession()
    {
        var wasAuthenticated = User is not null;

        _accessToken = null;
        _expiresAtUtc = default;
        User = null;
        Principal = Anonymous;

        if (wasAuthenticated)
        {
            UserChanged?.Invoke();
        }
    }

    private bool IsExpiring() => _expiresAtUtc - DateTime.UtcNow <= RefreshAhead;

    private bool IsExpired() => _expiresAtUtc <= DateTime.UtcNow.AddSeconds(15);

    private const int StatusCodes429 = 429;

    public ValueTask DisposeAsync()
    {
        _keepAlive?.Cancel();
        _keepAlive?.Dispose();
        _keepAlive = null;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Result of a sign-in or sign-up attempt. <see cref="Error"/> is a resource key or a server message.</summary>
public sealed record AuthOutcome(bool Success, CurrentUserDto? User, string? Error, IReadOnlyDictionary<string, string[]>? Errors)
{
    public static AuthOutcome Succeeded(CurrentUserDto user) => new(true, user, null, null);

    public static AuthOutcome Failed(string error, IReadOnlyDictionary<string, string[]>? errors = null) => new(false, null, error, errors);
}

/// <summary>Builds the <see cref="ClaimsPrincipal"/> the components authorize against.</summary>
public static class SessionPrincipal
{
    public static ClaimsPrincipal From(CurrentUserDto user)
    {
        var claims = new List<Claim>
        {
            new(ArtDashClaimTypes.Subject, user.Id.ToString()),
            new(ArtDashClaimTypes.Name, user.FullName),
            new(ArtDashClaimTypes.Email, user.Email),
            new(ArtDashClaimTypes.TenantId, user.TenantId.ToString()),
            new(ArtDashClaimTypes.TenantSlug, user.TenantSlug)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ArtDashClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, authenticationType: "artdash", ArtDashClaimTypes.Name, ArtDashClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    public static bool SameUser(CurrentUserDto? a, CurrentUserDto? b) =>
        a?.Id == b?.Id &&
        (a is null || b is null || a.Roles.OrderBy(r => r).SequenceEqual(b.Roles.OrderBy(r => r)));
}
