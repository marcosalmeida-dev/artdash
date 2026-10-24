using ArtDash.UI.Client.Auth;
using ArtDash.UI.Client.Components.Common;
using ArtDash.UI.Client.Services.Api;

namespace ArtDash.UI.Client.Services;

/// <summary>
/// Runs a page action and turns API failures into a snackbar: validation messages, business rule
/// violations (422), "not found", "no connection". A 401 sends the user to sign in.
/// Pages stay free of try/catch boilerplate.
/// </summary>
public sealed class UiActions(ISnackbar snackbar, NavigationManager navigation, IStringLocalizer<Strings> l)
{
    /// <summary>Runs <paramref name="action"/>; true when it succeeded.</summary>
    public async Task<bool> RunAsync(Func<Task> action, string? successMessage = null)
    {
        try
        {
            await action();
            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                snackbar.Add(successMessage, Severity.Success);
            }

            return true;
        }
        catch (ApiException ex)
        {
            Report(ex);
            return false;
        }
    }

    /// <summary>Loads something; default (and a snackbar) when it failed.</summary>
    public async Task<T?> LoadAsync<T>(Func<Task<T>> load)
    {
        try
        {
            return await load();
        }
        catch (ApiException ex)
        {
            Report(ex);
            return default;
        }
    }

    public void Success(string message) => snackbar.Add(message, Severity.Success);

    public void Info(string message) => snackbar.Add(message, Severity.Info);

    public void Warning(string message) => snackbar.Add(message, Severity.Warning);

    public void Report(ApiException ex)
    {
        if (ex.IsUnauthorized)
        {
            var returnUrl = "/" + navigation.ToBaseRelativePath(navigation.Uri);
            navigation.NavigateTo(LandingRoutes.LoginFor(returnUrl));
            return;
        }

        snackbar.Add(MessageFor(ex), ex.IsUnreachable || ex.Status >= 500 ? Severity.Error : Severity.Warning);
    }

    public string MessageFor(ApiException ex)
    {
        if (ex.Errors.Count > 0)
        {
            return string.Join(" ", ex.Errors.SelectMany(e => e.Value).Distinct().Take(3));
        }

        return ex.Status switch
        {
            0 => l["Errors.Network"],
            403 => ex.ServerMessage ?? l["Errors.Forbidden"],
            404 => ex.ServerMessage ?? l["Errors.NotFound"],
            >= 500 => l["Errors.Server"],
            _ => ex.ServerMessage ?? l["Errors.Generic"]
        };
    }
}

/// <summary>Brazilian formatting for money, quantities, dates and documents.</summary>
public static class Fmt
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static string Money(decimal value) => value.ToString("C2", Culture);

    public static string Money(decimal? value) => value is { } v ? Money(v) : "—";

    public static string Number(decimal value, int decimals = 2) => value.ToString($"N{decimals}", Culture);

    /// <summary>"3", "1,5" — trailing zeros dropped.</summary>
    public static string Quantity(decimal value) => value.ToString("0.###", Culture);

    public static string Weight(decimal? kg) => kg is { } v ? $"{v.ToString("0.##", Culture)} kg" : "—";

    public static string Date(DateTime value) => value.ToString("dd/MM/yyyy", Culture);

    public static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", Culture);

    public static string Date(DateOnly? value) => value is { } v ? Date(v) : "—";

    public static string DateAndTime(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", Culture);

    public static string Time(DateTime value) => value.ToString("HH:mm", Culture);

    /// <summary>"agora", "8 min", "2 h 5 min", "3 d".</summary>
    public static string Elapsed(TimeSpan span) =>
        span.TotalMinutes < 1 ? "agora"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min"
        : span.TotalDays < 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
        : $"{(int)span.TotalDays} d";

    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>(48) 99999-0000 / (48) 3333-0000.</summary>
    public static string Phone(string? value)
    {
        var digits = Digits(value);
        return digits.Length switch
        {
            11 => $"({digits[..2]}) {digits[2..7]}-{digits[7..]}",
            10 => $"({digits[..2]}) {digits[2..6]}-{digits[6..]}",
            _ => value ?? string.Empty
        };
    }

    /// <summary>CPF 000.000.000-00 or CNPJ 00.000.000/0000-00.</summary>
    public static string Document(string? value)
    {
        var digits = Digits(value);
        return digits.Length switch
        {
            11 => $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}",
            14 => $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}",
            _ => value ?? string.Empty
        };
    }

    public static string ZipCode(string? value)
    {
        var digits = Digits(value);
        return digits.Length == 8 ? $"{digits[..5]}-{digits[5..]}" : value ?? string.Empty;
    }

    /// <summary>A tel: link for a Brazilian number.</summary>
    public static string TelLink(string? phone) => $"tel:+55{Digits(phone)}";

    public static string WhatsAppLink(string? phone) => $"https://wa.me/55{Digits(phone)}";

    /// <summary>Opens navigation to an address in Google Maps (coordinates when known).</summary>
    public static string DirectionsLink(AddressDto address) =>
        address.HasCoordinates
            ? $"https://www.google.com/maps/dir/?api=1&destination={address.Latitude!.Value.ToString(CultureInfo.InvariantCulture)},{address.Longitude!.Value.ToString(CultureInfo.InvariantCulture)}"
            : $"https://www.google.com/maps/dir/?api=1&destination={Uri.EscapeDataString(address.ToSingleLine())}";

    public static string WazeLink(AddressDto address) =>
        address.HasCoordinates
            ? $"https://waze.com/ul?ll={address.Latitude!.Value.ToString(CultureInfo.InvariantCulture)},{address.Longitude!.Value.ToString(CultureInfo.InvariantCulture)}&navigate=yes"
            : $"https://waze.com/ul?q={Uri.EscapeDataString(address.ToSingleLine())}&navigate=yes";
}

/// <summary>One color and icon per status, used by every chip, card and timeline.</summary>
public static class StatusStyles
{
    public static Color For(OrderStatus status) => status switch
    {
        OrderStatus.Pending => Color.Warning,
        OrderStatus.Confirmed => Color.Info,
        OrderStatus.Preparing => Color.Secondary,
        OrderStatus.ReadyForDelivery => Color.Tertiary,
        OrderStatus.OutForDelivery => Color.Primary,
        OrderStatus.Delivered => Color.Success,
        _ => Color.Error
    };

    public static string Icon(OrderStatus status) => status switch
    {
        OrderStatus.Pending => Icons.Material.Rounded.HourglassTop,
        OrderStatus.Confirmed => Icons.Material.Rounded.ThumbUp,
        OrderStatus.Preparing => Icons.Material.Rounded.Inventory2,
        OrderStatus.ReadyForDelivery => Icons.Material.Rounded.Done,
        OrderStatus.OutForDelivery => Icons.Material.Rounded.LocalShipping,
        OrderStatus.Delivered => Icons.Material.Rounded.TaskAlt,
        _ => Icons.Material.Rounded.Cancel
    };

    public static Color For(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => Color.Success,
        PaymentStatus.PartiallyPaid => Color.Info,
        PaymentStatus.Refunded => Color.Dark,
        _ => Color.Warning
    };

    public static Color For(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Planned => Color.Info,
        DeliveryStatus.InProgress => Color.Primary,
        DeliveryStatus.Completed => Color.Success,
        _ => Color.Error
    };

    public static Color For(DeliveryStopStatus status) => status switch
    {
        DeliveryStopStatus.Delivered => Color.Success,
        DeliveryStopStatus.Failed => Color.Error,
        _ => Color.Default
    };

    public static Color For(TransactionStatus status) => status switch
    {
        TransactionStatus.Approved => Color.Success,
        TransactionStatus.Pending or TransactionStatus.InProcess => Color.Warning,
        TransactionStatus.Refunded or TransactionStatus.ChargedBack => Color.Dark,
        _ => Color.Error
    };

    public static string Icon(VehicleType type) => type switch
    {
        VehicleType.Motorcycle => Icons.Material.Rounded.TwoWheeler,
        VehicleType.Van => Icons.Material.Rounded.AirportShuttle,
        VehicleType.Truck => Icons.Material.Rounded.LocalShipping,
        _ => Icons.Material.Rounded.DirectionsCar
    };

    public static string Icon(PaymentMethod method) => method switch
    {
        PaymentMethod.Pix => Icons.Material.Rounded.QrCode2,
        PaymentMethod.CreditCard or PaymentMethod.DebitCard => Icons.Material.Rounded.CreditCard,
        PaymentMethod.Cash => Icons.Material.Rounded.Payments,
        PaymentMethod.BankSlip => Icons.Material.Rounded.Receipt,
        _ => Icons.Material.Rounded.AccountBalance
    };

    public static string Icon(PaymentTiming timing) => timing switch
    {
        PaymentTiming.Prepaid => Icons.Material.Rounded.Bolt,
        PaymentTiming.OnDelivery => Icons.Material.Rounded.DeliveryDining,
        _ => Icons.Material.Rounded.RequestQuote
    };

    /// <summary>The order statuses in their natural sequence (for steppers and board columns).</summary>
    public static readonly IReadOnlyList<OrderStatus> Flow =
    [
        OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Preparing,
        OrderStatus.ReadyForDelivery, OrderStatus.OutForDelivery, OrderStatus.Delivered
    ];

    /// <summary>Status changes staff can make by hand (driver-recorded ones excluded) — mirrors Order.MoveTo.</summary>
    public static IReadOnlyList<OrderStatus> ManualTargets(OrderStatus current, PaymentTiming timing, PaymentStatus payment, bool inDelivery) =>
        current switch
        {
            OrderStatus.Pending when timing != PaymentTiming.Prepaid || payment == PaymentStatus.Paid => [OrderStatus.Confirmed],
            OrderStatus.Confirmed => [OrderStatus.Preparing, OrderStatus.ReadyForDelivery],
            OrderStatus.Preparing => [OrderStatus.ReadyForDelivery],
            _ => []
        };

    public static bool CanCancel(OrderStatus current, bool inDelivery) =>
        current is not (OrderStatus.OutForDelivery or OrderStatus.Delivered or OrderStatus.Cancelled) && !inDelivery;
}

/// <summary>Dialog shortcuts.</summary>
public static class DialogServiceExtensions
{
    private static readonly DialogOptions SmallDialog = new()
    {
        MaxWidth = MaxWidth.ExtraSmall,
        FullWidth = true,
        CloseOnEscapeKey = true,
        CloseButton = true
    };

    /// <summary>A yes/no question. True when confirmed.</summary>
    public static async Task<bool> ConfirmAsync(
        this IDialogService dialogs, string title, string message, string confirmText, string cancelText,
        Color color = Color.Primary, string? icon = null)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Message, message },
            { x => x.ConfirmText, confirmText },
            { x => x.CancelText, cancelText },
            { x => x.Color, color },
            { x => x.Icon, icon ?? Icons.Material.Rounded.HelpOutline }
        };

        var dialog = await dialogs.ShowAsync<ConfirmDialog>(title, parameters, SmallDialog);
        var result = await dialog.Result;
        return result is { Canceled: false };
    }

    /// <summary>Asks for a short text (a reason, a note). Null when cancelled.</summary>
    public static async Task<string?> PromptAsync(
        this IDialogService dialogs, string title, string label, string confirmText, string cancelText,
        bool required = true, Color color = Color.Primary, string? helperText = null)
    {
        var parameters = new DialogParameters<TextPromptDialog>
        {
            { x => x.Label, label },
            { x => x.ConfirmText, confirmText },
            { x => x.CancelText, cancelText },
            { x => x.Required, required },
            { x => x.Color, color },
            { x => x.HelperText, helperText }
        };

        var dialog = await dialogs.ShowAsync<TextPromptDialog>(title, parameters, SmallDialog);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: string text } ? text : null;
    }

    public static DialogOptions Form(MaxWidth width = MaxWidth.Small) => new()
    {
        MaxWidth = width,
        FullWidth = true,
        CloseOnEscapeKey = true,
        CloseButton = true,
        BackdropClick = false
    };
}

/// <summary>Random passwords for accounts created or reset by an admin.</summary>
public static class PasswordGenerator
{
    // No look-alikes (0/O, 1/l/I): the password is often read out or typed from a phone.
    private const string Lower = "abcdefghjkmnpqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const string Digits = "23456789";

    /// <summary>12 characters with lower case, upper case and digits (meets the Identity password policy).</summary>
    public static string New()
    {
        string[] pools = [Lower, Upper, Lower, Digits];
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++)
        {
            var pool = pools[i % pools.Length];
            chars[i] = pool[System.Security.Cryptography.RandomNumberGenerator.GetInt32(pool.Length)];
        }

        return new string(chars);
    }
}
