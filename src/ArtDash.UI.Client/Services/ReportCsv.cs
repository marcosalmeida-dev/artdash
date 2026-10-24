using System.Text;
using ArtDash.Shared.Contracts.Reports;

namespace ArtDash.UI.Client.Services;

/// <summary>Exports the report snapshot as Brazilian CSV, keeping all report sections together.</summary>
public static class ReportCsv
{
    public static string Build(ReportDto report, string channelLabel, IStringLocalizer<Strings> l, TimeZoneInfo zone)
    {
        var csv = new StringBuilder();
        var summary = report.Summary;
        var deliveries = report.Deliveries;

        string Label(string key) => l[key];
        DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        void Row(params object?[] cells) => csv.AppendJoin(';', cells.Select(Cell)).Append("\r\n");

        Row(Label("Reports.Title"));
        Row(Label("Reports.Period"), report.From, report.To);
        Row(Label("Orders.Channel"), channelLabel);
        Row(Label("Reports.GeneratedAt"), Local(report.GeneratedAt), zone.Id);
        Row(Label("Reports.Semantics"));
        Row();

        Row(Label("Reports.Summary"));
        Row(Label("Reports.Orders"), summary.Orders);
        Row(Label("Reports.ValidOrders"), summary.ValidOrders);
        Row(Label("Reports.CancelledOrders"), summary.CancelledOrders);
        Row(Label("Reports.Revenue"), summary.Revenue);
        Row(Label("Reports.AverageTicket"), summary.AverageTicket);
        Row(Label("Reports.AmountPaid"), summary.AmountPaid);
        Row(Label("Reports.OutstandingAmount"), summary.OutstandingAmount);
        Row(Label("Reports.OverdueAmount"), summary.OverdueAmount);
        Row(Label("Reports.OverdueCount"), summary.OverdueCount);
        Row(Label("Reports.ActiveCustomers"), summary.ActiveCustomers);
        Row(Label("Reports.NewCustomers"), summary.NewCustomers);
        Row(Label("Reports.ReturningCustomers"), summary.ReturningCustomers);
        Row(Label("Reports.CustomerHint"));
        Row();

        Row(Label("Reports.DailySales"));
        Row(Label("Orders.PlacedAt"), Label("Reports.Orders"), Label("Reports.Revenue"));
        foreach (var day in report.DailySales)
        {
            Row(day.Date, day.Orders, day.Revenue);
        }
        Row();

        Row(Label("Reports.OrdersByStatus"));
        Row(Label("Orders.Status"), Label("Reports.Orders"), Label("Reports.Revenue"));
        foreach (var status in report.OrdersByStatus)
        {
            Row(l.Enum(status.Status), status.Orders, status.Revenue);
        }
        Row();

        Row(Label("Reports.OrdersByChannel"));
        Row(Label("Orders.Channel"), Label("Reports.Orders"), Label("Reports.Revenue"));
        foreach (var channel in report.OrdersByChannel)
        {
            Row(l.Enum(channel.Channel), channel.Orders, channel.Revenue);
        }
        Row();

        Row(Label("Reports.PaymentsByTiming"));
        Row(Label("Orders.PaymentTiming"), Label("Reports.Orders"), Label("Reports.Revenue"),
            Label("Reports.AmountPaid"), Label("Reports.OutstandingAmount"));
        foreach (var timing in report.PaymentsByTiming)
        {
            Row(l.Enum(timing.Timing), timing.Orders, timing.Revenue, timing.AmountPaid, timing.OutstandingAmount);
        }
        Row();

        Row(Label("Reports.PaymentsByStatus"));
        Row(Label("Orders.Status"), Label("Reports.Orders"), Label("Reports.Revenue"),
            Label("Reports.AmountPaid"), Label("Reports.OutstandingAmount"));
        foreach (var status in report.PaymentsByStatus)
        {
            Row(l.Enum(status.Status), status.Orders, status.Revenue, status.AmountPaid, status.OutstandingAmount);
        }
        Row();

        Row(Label("Reports.TopCustomers"));
        Row(Label("Reports.TopCustomersHint"));
        Row(Label("Orders.Customer"), Label("Reports.CustomerType"), Label("Reports.Orders"),
            Label("Reports.Revenue"), Label("Reports.AverageTicket"), Label("Reports.LastOrder"));
        foreach (var customer in report.TopCustomers)
        {
            Row(customer.Name, customer.Type is { } type ? l.Enum(type) : "—", customer.Orders, customer.Revenue,
                customer.AverageTicket, Local(customer.LastOrderAt));
        }
        Row();

        Row(Label("Reports.TopProducts"));
        Row(Label("Reports.TopProductsHint"));
        Row(Label("Products.Product"), Label("Products.Unit"), Label("Reports.Quantity"),
            Label("Reports.Orders"), Label("Reports.GrossRevenue"));
        foreach (var product in report.TopProducts)
        {
            Row(product.Name, l.Enum(product.Unit), new Quantity(product.Quantity), product.Orders, product.Revenue);
        }
        Row();

        Row(Label("Reports.Deliveries"));
        Row(Label("Reports.DeliveriesHint"));
        Row(Label("Reports.Runs"), deliveries.Runs);
        Row(l.Enum(DeliveryStatus.Planned), deliveries.Planned);
        Row(l.Enum(DeliveryStatus.InProgress), deliveries.InProgress);
        Row(l.Enum(DeliveryStatus.Completed), deliveries.Completed);
        Row(l.Enum(DeliveryStatus.Cancelled), deliveries.Cancelled);
        Row(Label("Deliveries.Stops"), deliveries.Stops);
        Row(Label("Reports.DeliveredStops"), deliveries.DeliveredStops);
        Row(Label("Reports.FailedStops"), deliveries.FailedStops);
        Row(Label("Reports.PendingStops"), deliveries.PendingStops);
        Row();

        Row(Label("Reports.Drivers"));
        Row(Label("Reports.DeliveriesHint"));
        Row(Label("Deliveries.Driver"), Label("Reports.Runs"), l.Enum(DeliveryStatus.Planned),
            l.Enum(DeliveryStatus.InProgress), l.Enum(DeliveryStatus.Completed), l.Enum(DeliveryStatus.Cancelled),
            Label("Deliveries.Stops"), Label("Reports.DeliveredStops"), Label("Reports.FailedStops"), Label("Reports.PendingStops"));
        foreach (var driver in report.Drivers)
        {
            Row(driver.DriverName, driver.Runs, driver.Planned, driver.InProgress, driver.Completed,
                driver.Cancelled, driver.Stops, driver.DeliveredStops, driver.FailedStops, driver.PendingStops);
        }

        return csv.ToString();
    }

    private static string Cell(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            decimal amount => amount.ToString("0.00", Fmt.Culture),
            Quantity quantity => quantity.Value.ToString("0.###", Fmt.Culture),
            int count => count.ToString(Fmt.Culture),
            DateOnly date => Fmt.Date(date),
            DateTime date => Fmt.DateAndTime(date),
            _ => SafeText(value.ToString() ?? string.Empty)
        };

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string SafeText(string value)
    {
        // Quoting CSV fields does not stop Excel from interpreting formulas. Keep user-controlled
        // text literal while leaving typed numeric cells numeric, including negative amounts.
        var prefix = value.AsSpan().TrimStart(' ');
        return !prefix.IsEmpty && prefix[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'
            ? "'" + value
            : value;
    }

    private readonly record struct Quantity(decimal Value);
}