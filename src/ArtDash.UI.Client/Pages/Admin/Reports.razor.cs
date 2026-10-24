using ArtDash.Shared.Contracts.Reports;
using ArtDash.UI.Client.Services;

namespace ArtDash.UI.Client.Pages.Admin;

public partial class Reports
{
    private static readonly string[] Presets = ["Today", "Last7Days", "Last30Days", "ThisMonth"];
    private ReportDto? _data;
    private DateTime? _from;
    private DateTime? _to;
    private OrderChannel? _channel;
    private OrderChannel? _appliedChannel;
    private string? _preset = "Last30Days";
    private bool _loading;
    private bool _loadFailed;
    private bool _exporting;
    private IReadOnlyList<SalesPoint> ChartPoints { get; set; } = [];
    private decimal _chartMax = 1m;
    private string ChartGroupingKey { get; set; } = "Reports.SalesDaily";
    private int ChartLabelStep => Math.Max(1, (int)Math.Ceiling(ChartPoints.Count / 7d));

    private bool HasUnappliedFilters => _data is { } data &&
        (_from is null || _to is null || DateOnly.FromDateTime(_from.Value) != data.From ||
         DateOnly.FromDateTime(_to.Value) != data.To || _channel != _appliedChannel);

    protected override async Task OnInitializedAsync()
    {
        await Store.EnsureLoadedAsync();
        await LoadAsync(new ReportQuery());
    }

    private void UseCustomRange() => _preset = null;

    private Task RetryAsync() => _from is null || _to is null ? LoadAsync(new ReportQuery { Channel = _channel }) : ApplyAsync();

    private async Task SetPresetAsync(string preset)
    {
        if (_loading)
        {
            return;
        }

        _preset = preset;
        var today = Store.ToLocal(Clock.GetUtcNow().UtcDateTime).Date;
        _to = today;
        _from = preset switch
        {
            "Today" => today,
            "Last7Days" => today.AddDays(-6),
            "ThisMonth" => new DateTime(today.Year, today.Month, 1),
            _ => today.AddDays(-29)
        };
        await ApplyAsync();
    }

    private async Task ApplyAsync()
    {
        if (_loading)
        {
            return;
        }

        if (_from is null || _to is null || _from.Value.Date > _to.Value.Date)
        {
            Ui.Warning(L["Reports.InvalidRange"]);
            return;
        }

        if ((_to.Value.Date - _from.Value.Date).Days >= 366)
        {
            Ui.Warning(L["Reports.RangeTooLong"]);
            return;
        }

        await LoadAsync(new ReportQuery
        {
            From = DateOnly.FromDateTime(_from.Value),
            To = DateOnly.FromDateTime(_to.Value),
            Channel = _channel
        });
    }

    private async Task LoadAsync(ReportQuery filter)
    {
        _loading = true;
        try
        {
            var result = await Ui.LoadAsync(() => Api.GetAsync(filter));
            _loadFailed = result is null;
            if (result is not null)
            {
                _data = result;
                _from = result.From.ToDateTime(TimeOnly.MinValue);
                _to = result.To.ToDateTime(TimeOnly.MinValue);
                _appliedChannel = filter.Channel;
                BuildChart(result);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ExportAsync()
    {
        if (_data is not { } data || _exporting || _loading || _loadFailed || HasUnappliedFilters)
        {
            return;
        }

        _exporting = true;
        try
        {
            var csv = ReportCsv.Build(data, ChannelLabel(_appliedChannel), L, Store.Zone);
            if (!await Browser.DownloadTextAsync($"relatorios-{data.From:yyyy-MM-dd}-{data.To:yyyy-MM-dd}.csv", csv))
            {
                Ui.Warning(L["Reports.ExportFailed"]);
            }
        }
        finally
        {
            _exporting = false;
        }
    }

    private void BuildChart(ReportDto data)
    {
        var days = data.To.DayNumber - data.From.DayNumber + 1;
        ChartGroupingKey = days <= 31 ? "Reports.SalesDaily" : days <= 120 ? "Reports.SalesWeekly" : "Reports.SalesMonthly";
        ChartPoints = data.DailySales
            .GroupBy(d => days <= 31 ? d.Date : days <= 120
                ? data.From.AddDays((d.Date.DayNumber - data.From.DayNumber) / 7 * 7)
                : new DateOnly(d.Date.Year, d.Date.Month, 1))
            .Select(g => new SalesPoint(g.Key.ToString(days > 120 ? "MMM/yy" : "dd/MM", Fmt.Culture),
                g.Min(d => d.Date), g.Max(d => d.Date), g.Sum(d => d.Orders), g.Sum(d => d.Revenue)))
            .ToList();
        _chartMax = Math.Max(1m, ChartPoints.Count == 0 ? 0m : ChartPoints.Max(p => p.Revenue));
    }

    private string ChannelLabel(OrderChannel? channel) => channel is { } value ? L.Enum(value) : L["Reports.AllChannels"];

    private string ChartTitle(SalesPoint point) => L["Reports.ChartPoint",
        Fmt.Date(point.From), Fmt.Date(point.To), point.Orders, Fmt.Money(point.Revenue)];

    private string BarStyle(decimal revenue) => $"height:{(revenue / _chartMax * 100m).ToString("0.##", CultureInfo.InvariantCulture)}%";

    private static double Ratio(decimal value, decimal total) => total <= 0 ? 0 : (double)(value / total * 100m);

    private static string Percent(decimal value, decimal total) => total <= 0 ? "—" : (value / total).ToString("P1", Fmt.Culture);

    private sealed record SalesPoint(string Label, DateOnly From, DateOnly To, int Orders, decimal Revenue);
}