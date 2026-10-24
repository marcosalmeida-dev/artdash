namespace ArtDash.Shared.Enums;

/// <summary>State of a delivery run (one driver, one vehicle, one or more stops).</summary>
public enum DeliveryStatus
{
    Planned = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}
