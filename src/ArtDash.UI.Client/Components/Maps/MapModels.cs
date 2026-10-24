namespace ArtDash.UI.Client.Components.Maps;

/// <summary>Marker kinds understood by js/map.js (strings, so they serialize as-is).</summary>
public static class MapMarkerKinds
{
    public const string Vehicle = "vehicle";
    public const string Destination = "destination";
    public const string Store = "store";
    public const string Stop = "stop";
    public const string StopDone = "stop-done";
    public const string StopFailed = "stop-failed";
    public const string Me = "me";
}

/// <param name="Key">Stable identity: a marker with the same key is moved, not duplicated.</param>
/// <param name="Label">Short text drawn on the marker (stop sequence, plate).</param>
/// <param name="Popup">Plain text shown when the marker is tapped.</param>
public sealed record MapMarker(
    string Key,
    double Latitude,
    double Longitude,
    string Kind,
    string? Label = null,
    string? Popup = null,
    double? Heading = null);

public sealed record MapPoint(double Latitude, double Longitude);
