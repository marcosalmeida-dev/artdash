using ArtDash.Shared.Contracts.Branding;
using ArtDash.UI.Client.Services;
using ArtDash.UI.Client.Services.Api;
using Microsoft.Extensions.Logging;

namespace ArtDash.UI.Client.Theming;

/// <summary>
/// The tenant's brand for this circuit: loaded once, turned into a MudTheme, and swappable for a live
/// preview while an admin edits it (the whole app re-dresses itself as the colors change).
/// </summary>
public sealed class BrandingState(BrandingApi api, ILogger<BrandingState> logger)
{
    private readonly object _lock = new();
    private Task? _load;
    private BrandingDto _saved = BrandingDto.Default;
    private BrandingDto? _preview;

    public BrandingDto Current => _preview ?? _saved;

    public MudTheme Theme { get; private set; } = ThemeBuilder.Build(BrandingDto.Default);

    public bool IsPreviewing => _preview is not null;

    public event Action? Changed;

    public Task EnsureLoadedAsync()
    {
        lock (_lock)
        {
            return _load ??= LoadAsync();
        }
    }

    public void Preview(BrandingDto branding)
    {
        _preview = branding;
        Rebuild();
    }

    public void CancelPreview()
    {
        if (_preview is null)
        {
            return;
        }

        _preview = null;
        Rebuild();
    }

    /// <summary>Adopts a saved branding (after the editor saved it).</summary>
    public void Apply(BrandingDto saved)
    {
        _saved = saved;
        _preview = null;
        Rebuild();
    }

    private async Task LoadAsync()
    {
        try
        {
            _saved = await api.GetAsync();
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Could not load the branding; using the neutral brand");
        }

        Rebuild();
    }

    private void Rebuild()
    {
        Theme = ThemeBuilder.Build(Current);
        Changed?.Invoke();
    }
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>The visitor's light/dark preference (kept in localStorage), following the OS when "System".</summary>
public sealed class ThemePreference(BrowserStorage storage)
{
    private const string StorageKey = "ad.theme";
    private bool _initialized;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public bool SystemIsDark { get; private set; }

    public event Action? Changed;

    public bool IsDark(bool allowDark) =>
        allowDark && (Mode == ThemeMode.Dark || (Mode == ThemeMode.System && SystemIsDark));

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (Enum.TryParse<ThemeMode>(await storage.GetAsync(StorageKey), out var mode))
        {
            Mode = mode;
        }
    }

    public void SetSystemPreference(bool isDark)
    {
        if (SystemIsDark == isDark)
        {
            return;
        }

        SystemIsDark = isDark;
        Changed?.Invoke();
    }

    public async Task CycleAsync()
    {
        Mode = Mode switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            _ => ThemeMode.System
        };

        await storage.SetAsync(StorageKey, Mode.ToString());
        Changed?.Invoke();
    }
}
