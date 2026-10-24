using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArtDash.Shared.Json;
using ArtDash.UI.Client.Auth;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace ArtDash.UI.Client.Services.Api;

/// <summary>"Api" section: where the circuit reaches the REST API (normally this same process, over loopback).</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>E.g. <c>http://localhost:5180</c>. Empty: the first HTTP address the server listens on.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>Resolves the API base address once.</summary>
public sealed class ApiBaseAddress(IOptions<ApiOptions> options, IServer server)
{
    private Uri? _value;

    public Uri Value => _value ??= Resolve();

    private Uri Resolve()
    {
        var configured = options.Value.BaseUrl;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new Uri(configured.TrimEnd('/') + "/");
        }

        ICollection<string> addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var address = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Configure Api:BaseUrl (endereço em que o circuito acessa a API).");

        // Wildcard bindings ("http://+:8080", "http://[::]:8080", "http://0.0.0.0:80") are reached over loopback.
        address = address
            .Replace("://+", "://localhost", StringComparison.Ordinal)
            .Replace("://*", "://localhost", StringComparison.Ordinal)
            .Replace("://[::]", "://localhost", StringComparison.Ordinal)
            .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal);

        return new UriBuilder(address) { Path = "/" }.Uri;
    }
}

/// <summary>One connection pool for every circuit (a handler per circuit would exhaust sockets).</summary>
internal static class ApiTransport
{
    public static readonly SocketsHttpHandler Handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AutomaticDecompression = DecompressionMethods.All,

        // Cookies belong to the browser; the circuit never keeps any.
        UseCookies = false
    };
}

/// <summary>
/// Attaches the bearer token and the browser's host (the API resolves the tenant from it), renews the
/// token ahead of expiry, and retries once after a 401 (ported from artstore-fs' JwtAuthorizationMessageHandler).
/// </summary>
internal sealed class ApiAuthorizationHandler(AuthSession session, NavigationManager navigation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (BrowserHost() is { } host)
        {
            request.Headers.TryAddWithoutValidation(TenantHeaders.TenantHost, host);
        }

        var token = await session.GetAccessTokenAsync();
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null)
        {
            return response;
        }

        var refreshed = await session.RefreshAfterUnauthorizedAsync(token);
        if (refreshed is null || string.Equals(refreshed, token, StringComparison.Ordinal))
        {
            return response;
        }

        var retry = await CloneAsync(request, cancellationToken);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);
        response.Dispose();
        return await base.SendAsync(retry, cancellationToken);
    }

    private string? BrowserHost()
    {
        try
        {
            return new Uri(navigation.BaseUri).Authority;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A request cannot be sent twice; the body is buffered because its stream was consumed.</summary>
    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        if (request.Content is not null)
        {
            var content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}

/// <summary>
/// The circuit's HTTP client for the REST API. JSON in and out with the shared contract; any non-success
/// answer becomes an <see cref="ApiException"/> carrying the RFC 9457 problem the API returned.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;

    public ApiClient(AuthSession session, NavigationManager navigation, ApiBaseAddress baseAddress)
    {
        var handler = new ApiAuthorizationHandler(session, navigation) { InnerHandler = ApiTransport.Handler };

        // disposeHandler: false — the inner handler is shared by every circuit.
        _http = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = baseAddress.Value,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    /// <summary>Like <see cref="GetAsync{T}"/>, but a 404 comes back as null.</summary>
    public async Task<T?> FindAsync<T>(string url, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            return await GetAsync<T>(url, cancellationToken);
        }
        catch (ApiException ex) when (ex.IsNotFound)
        {
            return null;
        }
    }

    public async Task<T> PostAsync<T>(string url, object? body, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(WithBody(HttpMethod.Post, url, body), cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    public async Task PostAsync(string url, object? body = null, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(WithBody(HttpMethod.Post, url, body), cancellationToken);
    }

    public async Task<T> PutAsync<T>(string url, object body, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(WithBody(HttpMethod.Put, url, body), cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    public async Task PutAsync(string url, object body, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(WithBody(HttpMethod.Put, url, body), cancellationToken);
    }

    public async Task DeleteAsync(string url, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, url), cancellationToken);
    }

    /// <summary>Posts one file as multipart/form-data (field name <c>file</c>).</summary>
    public async Task<T> UploadAsync<T>(string url, Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = form }, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private static HttpRequestMessage WithBody(HttpMethod method, string url, object? body) => new(method, url)
    {
        Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: JsonDefaults.Options)
    };

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw ApiException.Unreachable(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw ApiException.Unreachable(ex);
        }
        finally
        {
            request.Dispose();
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var problem = await ApiProblem.ReadAsync(response, cancellationToken);
            throw new ApiException((int)response.StatusCode, problem);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options, cancellationToken);
        return value ?? throw new ApiException((int)response.StatusCode, new ApiProblem { Title = "Resposta vazia do servidor." });
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>An RFC 9457 problem as returned by the API.</summary>
public sealed class ApiProblem
{
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public int? Status { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    public static async Task<ApiProblem?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<ApiProblem>(text, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A failed API call. <see cref="Status"/> 0 means the server could not be reached.</summary>
public sealed class ApiException(int status, ApiProblem? problem, Exception? inner = null)
    : Exception(problem?.Detail ?? problem?.Title ?? $"HTTP {status}", inner)
{
    public int Status { get; } = status;

    public ApiProblem? Problem { get; } = problem;

    public IReadOnlyDictionary<string, string[]> Errors =>
        Problem?.Errors ?? (IReadOnlyDictionary<string, string[]>)new Dictionary<string, string[]>();

    /// <summary>The server's own explanation, when it gave one.</summary>
    public string? ServerMessage => Problem?.Detail ?? Problem?.Title;

    public bool IsUnreachable => Status == 0;
    public bool IsUnauthorized => Status == 401;
    public bool IsForbidden => Status == 403;
    public bool IsNotFound => Status == 404;
    public bool IsConflict => Status == 409;
    public bool IsValidation => Status == 400 && Errors.Count > 0;

    /// <summary>The first message for a field (keys as the API sends them: "Name", "Address.ZipCode").</summary>
    public string? ErrorFor(string field) =>
        Errors.FirstOrDefault(e => string.Equals(e.Key, field, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();

    public static ApiException Unreachable(Exception inner) => new(0, null, inner);
}

/// <summary>Builds query strings: skips nulls and blanks, dates as yyyy-MM-dd, enums by name.</summary>
public static class ApiQuery
{
    public static string Build(string path, params (string Name, object? Value)[] parameters)
    {
        var parts = parameters
            .Where(p => p.Value is not null && p.Value is not string { Length: 0 } && !(p.Value is string s && string.IsNullOrWhiteSpace(s)))
            .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(Format(p.Value!))}");

        var query = string.Join("&", parts);
        return query.Length == 0 ? path : $"{path}?{query}";
    }

    private static string Format(object value) => value switch
    {
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
