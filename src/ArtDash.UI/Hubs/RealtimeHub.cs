using System.Security.Claims;
using ArtDash.Application.Features.Orders;
using ArtDash.Infrastructure.MultiTenancy;
using ArtDash.Shared.Contracts.Deliveries;
using ArtDash.Shared.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace ArtDash.UI.Hubs;

/// <summary>
/// Live updates. Staff connections join their tenant's staff group on connect (order board, live map);
/// anyone holding a tracking code can follow that one order (tracking page).
/// The Blazor circuit is the client: it connects server-side, with the user's token.
/// </summary>
public sealed class RealtimeHub(ISender sender, ICurrentTenant currentTenant) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (currentTenant.Id is { } tenantId && IsStaff(Context.User))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Staff(tenantId));
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Follows one order by its public tracking code. Returns false for an unknown code.</summary>
    [HubMethodName(RealtimeEvents.TrackOrder)]
    public async Task<bool> TrackOrder(string trackingCode)
    {
        var orderId = await ResolveAsync(trackingCode);
        if (orderId is null)
        {
            return false;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Order(orderId.Value));
        return true;
    }

    [HubMethodName(RealtimeEvents.StopTrackingOrder)]
    public async Task StopTrackingOrder(string trackingCode)
    {
        var orderId = await ResolveAsync(trackingCode);
        if (orderId is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Order(orderId.Value));
        }
    }

    private async Task<Guid?> ResolveAsync(string trackingCode) =>
        string.IsNullOrWhiteSpace(trackingCode) || !currentTenant.IsResolved
            ? null
            : await sender.Send(new ResolveTrackingCodeQuery(trackingCode.Trim()), Context.ConnectionAborted);

    private static bool IsStaff(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && (user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Manager));
}

/// <summary>SignalR group names.</summary>
public static class RealtimeGroups
{
    public static string Staff(Guid tenantId) => $"staff:{tenantId:N}";

    public static string Order(Guid orderId) => $"order:{orderId:N}";
}

/// <summary>
/// Every hub call runs in its own DI scope, which the tenant middleware never saw. This filter copies
/// the tenant resolved for the connection's HTTP request into each of those scopes, so the
/// tenant-filtered DbContext works inside hub methods exactly as it does in controllers.
/// </summary>
public sealed class TenantHubFilter : IHubFilter
{
    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ApplyTenant(invocationContext.Context, invocationContext.ServiceProvider);
        return next(invocationContext);
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        ApplyTenant(context.Context, context.ServiceProvider);
        return next(context);
    }

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        ApplyTenant(context.Context, context.ServiceProvider);
        return next(context, exception);
    }

    private static void ApplyTenant(HubCallerContext context, IServiceProvider services)
    {
        var setter = services.GetRequiredService<ICurrentTenantSetter>();

        if (context.GetHttpContext()?.Items[TenantResolutionMiddleware.TenantItemKey] is TenantInfo tenant)
        {
            setter.Set(tenant.Id, tenant.Slug);
            return;
        }

        // Fallback for transports that do not keep the original request around: the token's tenant.
        var user = context.User;
        if (Guid.TryParse(user?.FindFirst(ArtDashClaimTypes.TenantId)?.Value, out var tenantId))
        {
            setter.Set(tenantId, user!.FindFirst(ArtDashClaimTypes.TenantSlug)?.Value ?? string.Empty);
        }
    }
}

/// <summary>
/// <see cref="IRealtimeNotifier"/> over SignalR. Live updates are best effort: a failure is logged and
/// never fails the request (or the domain event) that caused it.
/// </summary>
internal sealed class SignalRRealtimeNotifier(IHubContext<RealtimeHub> hub, ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public Task OrderChangedAsync(Guid tenantId, OrderChangedMessage message, CancellationToken cancellationToken = default) =>
        SendAsync(
            [RealtimeGroups.Staff(tenantId), RealtimeGroups.Order(message.OrderId)],
            RealtimeEvents.OrderChanged,
            message,
            cancellationToken);

    public Task PositionUpdatedAsync(
        Guid tenantId,
        VehiclePositionDto position,
        IReadOnlyCollection<Guid> trackedOrderIds,
        CancellationToken cancellationToken = default)
    {
        var groups = new List<string>(trackedOrderIds.Count + 1) { RealtimeGroups.Staff(tenantId) };
        groups.AddRange(trackedOrderIds.Select(RealtimeGroups.Order));
        return SendAsync(groups, RealtimeEvents.PositionUpdated, position, cancellationToken);
    }

    public Task DeliveryChangedAsync(Guid tenantId, DeliveryChangedMessage message, CancellationToken cancellationToken = default) =>
        SendAsync([RealtimeGroups.Staff(tenantId)], RealtimeEvents.DeliveryChanged, message, cancellationToken);

    private async Task SendAsync(List<string> groups, string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Groups(groups).SendAsync(method, payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Realtime notification {Method} failed", method);
        }
    }
}
