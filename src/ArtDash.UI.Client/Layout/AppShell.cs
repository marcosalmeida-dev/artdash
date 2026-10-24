namespace ArtDash.UI.Client.Layout;

/// <summary>
/// The chrome drawn around the pages, picked by role (as in artstore-fs): customers and visitors get the
/// storefront, staff the back-office console, drivers a compact app for the phone.
/// </summary>
public enum AppShell
{
    Store,
    Console,
    Driver
}
