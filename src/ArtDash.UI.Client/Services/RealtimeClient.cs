using ArtDash.Shared.Contracts.Deliveries;
using ArtDash.Shared.Contracts.Realtime;
using ArtDash.Shared.Json;
using ArtDash.UI.Client.Auth;
using ArtDash.UI.Client.Services.Api;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArtDash.UI.Client.Services;

/// <summary>
/// One SignalR connection per circuit to the realtime hub, opened server-side with the user's token
/// and the browser's host (so the hub resolves the same tenant). Staff connections receive the
/// tenant's order and fleet events; tracked orders are re-joined after every reconnect.
/// Handlers run off the renderer's thread: components must marshal through <c>InvokeAsync</c>.
/// </summary>
public sealed class RealtimeClient(
    AuthSession session,
    ApiBaseAddress baseAddress,
    NavigationManager navigation,
    ILogger<RealtimeClient> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _trackedCodes = new(StringComparer.OrdinalIgnoreCase);
    private HubConnection? _connection;
    private bool _listeningToUser;
    private bool _disposed;

    public event Func<OrderChangedMessage, Task>? OrderChanged;
    public event Func<VehiclePositionDto, Task>? PositionUpdated;
    public event Func<DeliveryChangedMessage, Task>? DeliveryChanged;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task EnsureStartedAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (!_listeningToUser)
            {
                session.UserChanged += OnUserChanged;
                _listeningToUser = true;
            }

            _connection ??= Build();
            if (_connection.State != HubConnectionState.Disconnected)
            {
                return;
            }

            await _connection.StartAsync();
            await RejoinTrackedAsync(_connection);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime connection could not be started");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Follows one order by its tracking code. False when the code is unknown or offline.</summary>
    public async Task<bool> TrackOrderAsync(string trackingCode)
    {
        _trackedCodes.Add(trackingCode);
        await EnsureStartedAsync();

        if (_connection is not { State: HubConnectionState.Connected } connection)
        {
            return false;
        }

        try
        {
            return await connection.InvokeAsync<bool>(RealtimeEvents.TrackOrder, trackingCode);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "TrackOrder failed");
            return false;
        }
    }

    public async Task StopTrackingAsync(string trackingCode)
    {
        if (!_trackedCodes.Remove(trackingCode) || _connection is not { State: HubConnectionState.Connected } connection)
        {
            return;
        }

        try
        {
            await connection.InvokeAsync(RealtimeEvents.StopTrackingOrder, trackingCode);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "StopTrackingOrder failed");
        }
    }

    private HubConnection Build()
    {
        var browserHost = new Uri(navigation.BaseUri).Authority;
        var hubUrl = new Uri(baseAddress.Value, RealtimeEvents.HubPath.TrimStart('/'));

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.Headers[TenantHeaders.TenantHost] = browserHost;
                options.AccessTokenProvider = () => session.GetAccessTokenAsync();
            })
            .WithAutomaticReconnect()
            .AddJsonProtocol(options => JsonDefaults.Apply(options.PayloadSerializerOptions))
            .Build();

        connection.On<OrderChangedMessage>(RealtimeEvents.OrderChanged, message => RaiseAsync(OrderChanged, message));
        connection.On<VehiclePositionDto>(RealtimeEvents.PositionUpdated, message => RaiseAsync(PositionUpdated, message));
        connection.On<DeliveryChangedMessage>(RealtimeEvents.DeliveryChanged, message => RaiseAsync(DeliveryChanged, message));
        connection.Reconnected += _ => RejoinTrackedAsync(connection);

        return connection;
    }

    private async Task RejoinTrackedAsync(HubConnection connection)
    {
        foreach (var code in _trackedCodes.ToList())
        {
            try
            {
                await connection.InvokeAsync<bool>(RealtimeEvents.TrackOrder, code);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not re-join order {Code}", code);
            }
        }
    }

    private async Task RaiseAsync<T>(Func<T, Task>? handlers, T message)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<T, Task>>())
        {
            try
            {
                await handler(message);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "A realtime handler failed");
            }
        }
    }

    /// <summary>Group membership follows the identity the connection was opened with: reconnect as the new user.</summary>
    private void OnUserChanged() => _ = RestartAsync();

    private async Task RestartAsync()
    {
        HubConnection? previous;
        await _gate.WaitAsync();
        try
        {
            previous = _connection;
            _connection = null;
        }
        finally
        {
            _gate.Release();
        }

        if (previous is null)
        {
            return;
        }

        try
        {
            await previous.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Realtime connection disposal failed");
        }

        await EnsureStartedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_listeningToUser)
        {
            session.UserChanged -= OnUserChanged;
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Realtime connection disposal failed");
            }
        }
    }
}
