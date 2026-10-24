using System.Text.Json;
using ArtDash.Shared.Json;

namespace ArtDash.UI.Client.Auth;

/// <summary>
/// Runs the authentication calls in the user's browser rather than on the circuit (ported from
/// artstore-fs). Everything else talks to the API through the circuit's server-side HttpClient, but
/// authentication cannot: the refresh token is an HttpOnly cookie only the browser can hold, and the
/// sign-in rate limits partition on the caller's IP — for a server-issued call, the server's own.
/// </summary>
public sealed class BrowserAuthClient(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "./_content/ArtDash.UI.Client/js/auth.js";

    private IJSObjectReference? _module;

    /// <summary>
    /// Issues a same-origin request and returns the raw status and body. Never throws for a transport
    /// failure — those come back as status 0, which callers must read as "unknown", not "rejected".
    /// </summary>
    public async Task<BrowserResponse> SendAsync(string path, string method, object? body = null)
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            var json = body is null ? null : JsonSerializer.Serialize(body, JsonDefaults.Options);
            return await _module.InvokeAsync<BrowserResponse>("send", path, method, json);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException or InvalidOperationException)
        {
            // InvalidOperationException: JS interop is not available (circuit not ready or torn down).
            return BrowserResponse.Unreachable;
        }
    }

    /// <summary>Reads a JSON response body, or default when it is empty or unexpected.</summary>
    public static T? Read<T>(BrowserResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(response.Body, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException)
        {
            // The circuit is already gone; nothing to release.
        }

        _module = null;
    }
}

/// <summary>Raw result of a browser-side request.</summary>
public sealed record BrowserResponse
{
    /// <summary>HTTP status, or 0 when the request never reached the server.</summary>
    public int Status { get; init; }

    public string Body { get; init; } = string.Empty;

    public bool IsSuccess => Status is >= 200 and < 300;

    public bool IsTransportFailure => Status == 0;

    public static BrowserResponse Unreachable { get; } = new() { Status = 0 };
}
