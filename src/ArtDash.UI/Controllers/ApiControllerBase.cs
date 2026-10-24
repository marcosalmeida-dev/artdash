namespace ArtDash.UI.Controllers;

/// <summary>
/// Base for the REST controllers. Controllers stay thin: bind the request, send a command or query,
/// shape the HTTP answer. Errors are exceptions, translated once by <c>GlobalExceptionHandler</c>.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>201 with the new resource id in the body (the client reads it as a <see cref="Guid"/>).</summary>
    protected ActionResult<Guid> CreatedWithId(string actionName, Guid id) =>
        CreatedAtAction(actionName, new { id }, id);
}

/// <summary>Rate-limit policy names (see <c>WebHostServiceCollectionExtensions</c>).</summary>
public static class RateLimitPolicies
{
    /// <summary>Sign-in and sign-up, per client IP.</summary>
    public const string Auth = "auth";

    /// <summary>Token refresh, per client IP (every page load refreshes once).</summary>
    public const string Refresh = "refresh";

    /// <summary>Anonymous public lookups (tracking), per client IP.</summary>
    public const string Public = "public";
}
