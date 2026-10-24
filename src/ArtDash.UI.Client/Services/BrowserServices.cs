using System.Text.Json.Serialization;

namespace ArtDash.UI.Client.Services;

/// <summary>A GPS fix from the browser (driver's phone).</summary>
public sealed record GeoFix(double Latitude, double Longitude, double? AccuracyMeters, double? SpeedKmh, double? Heading, DateTime RecordedAtUtc);

/// <summary>
/// Wraps <c>navigator.geolocation.watchPosition</c> plus a screen Wake Lock (js/geo.js): the driver's phone
/// keeps reporting while the run screen is open. Fixes are throttled in the browser (time and distance).
/// </summary>
public sealed class GeolocationService(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "./_content/ArtDash.UI.Client/js/geo.js";

    private IJSObjectReference? _module;
    private DotNetObjectReference<GeolocationService>? _reference;

    public bool IsWatching { get; private set; }

    public GeoFix? LastFix { get; private set; }

    /// <summary>1 = permission denied, 2 = position unavailable, 3 = timeout, 0 = not supported.</summary>
    public int? LastErrorCode { get; private set; }

    public event Func<GeoFix, Task>? PositionChanged;

    public event Action? StateChanged;

    public async Task<bool> StartAsync(int minIntervalSeconds = 10, int minDistanceMeters = 25)
    {
        if (IsWatching)
        {
            return true;
        }

        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _reference ??= DotNetObjectReference.Create(this);
            IsWatching = await _module.InvokeAsync<bool>("start", _reference, minIntervalSeconds * 1000, minDistanceMeters);
            LastErrorCode = IsWatching ? null : 0;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
        {
            IsWatching = false;
        }

        StateChanged?.Invoke();
        return IsWatching;
    }

    public async Task StopAsync()
    {
        IsWatching = false;
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("stop");
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
        {
        }

        StateChanged?.Invoke();
    }

    [JSInvokable]
    public async Task OnPosition(double latitude, double longitude, double? accuracy, double? speedMetersPerSecond, double? heading, double timestamp)
    {
        LastErrorCode = null;
        LastFix = new GeoFix(
            latitude,
            longitude,
            accuracy,
            speedMetersPerSecond is { } speed ? Math.Round(speed * 3.6, 1) : null,
            heading,
            DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp).UtcDateTime);

        if (PositionChanged is not null)
        {
            await PositionChanged(LastFix);
        }

        StateChanged?.Invoke();
    }

    [JSInvokable]
    public void OnError(int code)
    {
        LastErrorCode = code;
        StateChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (IsWatching)
        {
            await StopAsync();
        }

        try
        {
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
        }

        _reference?.Dispose();
    }
}

/// <summary>ViaCEP result (Brazilian postal codes).</summary>
public sealed class CepResult
{
    [JsonPropertyName("logradouro")] public string? Street { get; set; }
    [JsonPropertyName("bairro")] public string? District { get; set; }
    [JsonPropertyName("localidade")] public string? City { get; set; }
    [JsonPropertyName("uf")] public string? State { get; set; }
    [JsonPropertyName("complemento")] public string? Complement { get; set; }
    [JsonPropertyName("erro")] public bool? NotFound { get; set; }
}

public sealed record GeocodeResult(double Latitude, double Longitude, string DisplayName);

/// <summary>
/// Address helpers that run in the browser (js/lookup.js): CEP lookup on ViaCEP and geocoding on
/// OpenStreetMap Nominatim, so maps can place the customer without a paid API key.
/// </summary>
public sealed class AddressLookup(IJSRuntime js)
{
    private const string ModulePath = "./_content/ArtDash.UI.Client/js/lookup.js";

    private IJSObjectReference? _module;

    public async Task<CepResult?> FindCepAsync(string? zipCode)
    {
        var digits = new string((zipCode ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != 8)
        {
            return null;
        }

        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            var result = await _module.InvokeAsync<CepResult?>("cep", digits);
            return result is null || result.NotFound == true ? null : result;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<GeocodeResult?> GeocodeAsync(AddressDto address)
    {
        var query = string.Join(", ", new[]
        {
            string.Join(" ", new[] { address.Street, address.Number }.Where(s => !string.IsNullOrWhiteSpace(s))),
            address.District, address.City, address.State, "Brasil"
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return await _module.InvokeAsync<GeocodeResult?>("geocode", query, address.ZipCode);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
        {
            return null;
        }
    }
}

/// <summary>Small browser helpers (js/util.js): clipboard, downloads, theme-color meta, web font loading.</summary>
public sealed class BrowserUtil(IJSRuntime js)
{
    private const string ModulePath = "./_content/ArtDash.UI.Client/js/util.js";

    private IJSObjectReference? _module;

    public Task<bool> CopyAsync(string text) => InvokeAsync<bool>("copyText", text);

    public Task<bool> DownloadTextAsync(string filename, string content, string contentType = "text/csv;charset=utf-8") =>
        InvokeAsync<bool>("downloadText", filename, content, contentType);

    public Task SetThemeColorAsync(string color) => InvokeAsync<bool>("setThemeColor", color);

    public Task EnsureFontAsync(string stylesheetUrl) => InvokeAsync<bool>("ensureFont", stylesheetUrl);

    public Task ScrollToTopAsync() => InvokeAsync<bool>("scrollToTop");

    private async Task<T?> InvokeAsync<T>(string identifier, params object?[] args)
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return await _module.InvokeAsync<T>(identifier, args);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or InvalidOperationException)
        {
            return default;
        }
    }
}
