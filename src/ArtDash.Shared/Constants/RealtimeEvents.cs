namespace ArtDash.Shared.Constants;

/// <summary>SignalR hub path, client events and hub methods shared by server and client.</summary>
public static class RealtimeEvents
{
    public const string HubPath = "/hubs/realtime";

    // Server -> client events
    public const string OrderChanged = nameof(OrderChanged);
    public const string PositionUpdated = nameof(PositionUpdated);
    public const string DeliveryChanged = nameof(DeliveryChanged);

    // Client -> server methods
    public const string TrackOrder = nameof(TrackOrder);
    public const string StopTrackingOrder = nameof(StopTrackingOrder);
}
