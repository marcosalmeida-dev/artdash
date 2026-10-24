using System.Text.Json;
using ArtDash.Shared.Contracts.Catalog;
using ArtDash.Shared.Contracts.Settings;
using ArtDash.Shared.Json;
using ArtDash.UI.Client.Services.Api;
using Microsoft.Extensions.Logging;

namespace ArtDash.UI.Client.Services;

/// <summary>localStorage access that never throws (private mode, storage disabled, circuit gone).</summary>
public sealed class BrowserStorage(IJSRuntime js)
{
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task SetAsync(string key, string value)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
        }
    }
}

/// <summary>
/// Public store information (contacts, delivery pricing, payment options) and the store's time zone —
/// loaded once per circuit. Every date the UI shows goes through <see cref="ToLocal"/>: the API speaks UTC.
/// </summary>
public sealed class StoreState(StoreApi api, ILogger<StoreState> logger)
{
    private static readonly TimeZoneInfo Fallback = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");

    private readonly object _lock = new();
    private Task? _load;

    public StoreInfoDto Info { get; private set; } = new();

    public TimeZoneInfo Zone { get; private set; } = Fallback;

    public event Action? Changed;

    public Task EnsureLoadedAsync()
    {
        lock (_lock)
        {
            return _load ??= LoadAsync();
        }
    }

    public Task ReloadAsync()
    {
        lock (_lock)
        {
            _load = LoadAsync();
            return _load;
        }
    }

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime? ToLocal(DateTime? utc) => utc is { } value ? ToLocal(value) : null;

    public DateTime Now => ToLocal(DateTime.UtcNow);

    public DateTime Today => Now.Date;

    public decimal DeliveryFeeFor(decimal subtotal) =>
        Info.FreeDeliveryThreshold is > 0 && subtotal >= Info.FreeDeliveryThreshold.Value ? 0m : Math.Max(0m, Info.DeliveryFee);

    private async Task LoadAsync()
    {
        try
        {
            Info = await api.GetInfoAsync();
            Zone = TimeZoneInfo.TryFindSystemTimeZoneById(Info.TimeZoneId, out var zone) ? zone : Fallback;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Could not load the store information");
        }

        Changed?.Invoke();
    }
}

/// <summary>A line of the shopping cart.</summary>
public sealed class CartLine
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public UnitOfMeasure Unit { get; set; }
    public string? ImageUrl { get; set; }
    public decimal Quantity { get; set; }

    public decimal Total => UnitPrice * Quantity;

    /// <summary>Weighed goods move in halves; everything else in whole units.</summary>
    public static decimal StepFor(UnitOfMeasure unit) => unit is UnitOfMeasure.Kilogram or UnitOfMeasure.Liter ? 0.5m : 1m;
}

/// <summary>The storefront cart, kept in localStorage so it survives a reload or a closed tab.</summary>
public sealed class CartService(BrowserStorage storage)
{
    private const string StorageKey = "ad.cart";

    private readonly List<CartLine> _lines = [];
    private bool _loaded;

    public IReadOnlyList<CartLine> Lines => _lines;

    public bool IsEmpty => _lines.Count == 0;

    public decimal ItemCount => _lines.Sum(l => l.Unit is UnitOfMeasure.Kilogram or UnitOfMeasure.Liter ? 1 : l.Quantity);

    public decimal Subtotal => _lines.Sum(l => l.Total);

    public event Action? Changed;

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var json = await storage.GetAsync(StorageKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var lines = JsonSerializer.Deserialize<List<CartLine>>(json, JsonDefaults.Options) ?? [];
            _lines.Clear();
            _lines.AddRange(lines.Where(l => l.Quantity > 0));
            Changed?.Invoke();
        }
        catch (JsonException)
        {
            await storage.RemoveAsync(StorageKey);
        }
    }

    public decimal QuantityOf(Guid productId) => _lines.FirstOrDefault(l => l.ProductId == productId)?.Quantity ?? 0;

    public Task AddAsync(StorefrontProductDto product, decimal? quantity = null)
    {
        var step = quantity ?? CartLine.StepFor(product.Unit);
        var line = _lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (line is null)
        {
            _lines.Add(new CartLine
            {
                ProductId = product.Id,
                Name = product.Name,
                UnitPrice = product.Price,
                Unit = product.Unit,
                ImageUrl = product.ImageUrl,
                Quantity = step
            });
        }
        else
        {
            line.Quantity += step;
        }

        return SaveAsync();
    }

    public Task SetQuantityAsync(Guid productId, decimal quantity)
    {
        var line = _lines.FirstOrDefault(l => l.ProductId == productId);
        if (line is null)
        {
            return Task.CompletedTask;
        }

        if (quantity <= 0)
        {
            _lines.Remove(line);
        }
        else
        {
            line.Quantity = Math.Min(quantity, 9999);
        }

        return SaveAsync();
    }

    public Task IncrementAsync(Guid productId, int direction)
    {
        var line = _lines.FirstOrDefault(l => l.ProductId == productId);
        return line is null ? Task.CompletedTask : SetQuantityAsync(productId, line.Quantity + direction * CartLine.StepFor(line.Unit));
    }

    public Task RemoveAsync(Guid productId) => SetQuantityAsync(productId, 0);

    public async Task ClearAsync()
    {
        _lines.Clear();
        await SaveAsync();
    }

    /// <summary>Refreshes names, prices and pictures from the current catalog and drops what is no longer sold.</summary>
    public async Task ReconcileAsync(StorefrontCatalogDto catalog)
    {
        var products = catalog.AllProducts.ToDictionary(p => p.Id);
        var changed = _lines.RemoveAll(l => !products.ContainsKey(l.ProductId)) > 0;

        foreach (var line in _lines)
        {
            var product = products[line.ProductId];
            if (line.UnitPrice != product.Price || line.Name != product.Name || line.ImageUrl != product.ImageUrl)
            {
                line.UnitPrice = product.Price;
                line.Name = product.Name;
                line.ImageUrl = product.ImageUrl;
                line.Unit = product.Unit;
                changed = true;
            }
        }

        if (changed)
        {
            await SaveAsync();
        }
    }

    private async Task SaveAsync()
    {
        if (_lines.Count == 0)
        {
            await storage.RemoveAsync(StorageKey);
        }
        else
        {
            await storage.SetAsync(StorageKey, JsonSerializer.Serialize(_lines, JsonDefaults.Options));
        }

        Changed?.Invoke();
    }
}
