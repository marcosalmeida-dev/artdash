using ArtDash.Application.Common.Exceptions;
using ArtDash.Application.Common.Interfaces;
using ArtDash.Application.Features.Reports;
using ArtDash.Domain.Catalog;
using ArtDash.Domain.Common;
using ArtDash.Domain.Customers;
using ArtDash.Domain.Deliveries;
using ArtDash.Domain.Orders;
using ArtDash.Domain.Tenants;
using ArtDash.Infrastructure.MultiTenancy;
using ArtDash.Infrastructure.Persistence;
using ArtDash.Infrastructure.Persistence.Interceptors;
using ArtDash.Shared.Contracts.Reports;
using ArtDash.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ArtDash.Infrastructure.Tests;

public sealed class ReportTests
{
    private static readonly DateOnly From = new(2026, 10, 20);
    private static readonly DateOnly To = new(2026, 10, 22);

    [Fact]
    public async Task Sales_follow_inclusive_local_dates_and_fill_empty_days_without_cancelled_revenue()
    {
        await using var fixture = new Fixture();
        var customer = fixture.Customer();
        fixture.Order(customer, Utc(20, 2, 59, 59), 900m);
        fixture.Order(customer, Utc(20, 3), 10m);
        fixture.Order(customer, Utc(21, 2, 59, 59), 20m);
        fixture.Order(customer, Utc(23, 2, 59, 59), 30m);
        fixture.Order(customer, Utc(23, 3), 800m);
        fixture.Order(customer, Utc(21, 12), 700m).Cancel("Test cancellation", Utc(21, 13), "test");
        await fixture.Save();

        var report = await fixture.Report();

        report.Summary.Orders.ShouldBe(4);
        report.Summary.ValidOrders.ShouldBe(3);
        report.Summary.CancelledOrders.ShouldBe(1);
        report.Summary.Revenue.ShouldBe(60m);
        report.Summary.AverageTicket.ShouldBe(20m);
        report.DailySales.ShouldBe(new[]
        {
            new ReportDailySalesDto(From, 2, 30m),
            new ReportDailySalesDto(From.AddDays(1), 0, 0m),
            new ReportDailySalesDto(To, 1, 30m)
        });
    }

    [Fact]
    public async Task Financial_balances_belong_to_the_order_cohort_and_overdue_uses_the_local_today()
    {
        await using var fixture = new Fixture();
        var customer = fixture.Customer();
        var partial = fixture.Order(customer, Utc(20, 12), 120m, timing: PaymentTiming.Invoiced);
        partial.SetDeliveryFee(10m);
        partial.ApplyDiscount(30m);
        partial.SetDueDate(To.AddDays(-1));
        partial.RegisterPayment(PaymentMethod.Cash, 30m, Utc(23, 12), null, "test");
        var dueToday = fixture.Order(customer, Utc(22, 12), 50m, timing: PaymentTiming.Invoiced);
        dueToday.SetDueDate(To);
        fixture.Order(customer, Utc(21, 12), 20m);
        var overpaid = fixture.Order(customer, Utc(21, 13), 20m);
        overpaid.RegisterPayment(PaymentMethod.Cash, 25m, Utc(21, 14), null, "test");
        var old = fixture.Order(customer, Utc(19, 12), 400m, timing: PaymentTiming.Invoiced);
        old.SetDueDate(To.AddDays(-2));
        old.RegisterPayment(PaymentMethod.Cash, 10m, Utc(21, 12), null, "test");
        var cancelled = fixture.Order(customer, Utc(20, 13), 1000m, timing: PaymentTiming.Invoiced);
        cancelled.SetDueDate(To.AddDays(-1));
        cancelled.RegisterPayment(PaymentMethod.Cash, 50m, Utc(20, 14), null, "test");
        cancelled.Cancel("Test cancellation", Utc(20, 15), "test");
        await fixture.Save();

        var report = await fixture.Report();

        report.Summary.Revenue.ShouldBe(190m);
        report.Summary.AverageTicket.ShouldBe(47.5m);
        report.Summary.AmountPaid.ShouldBe(55m);
        report.Summary.OutstandingAmount.ShouldBe(140m);
        report.Summary.OverdueAmount.ShouldBe(70m);
        report.Summary.OverdueCount.ShouldBe(1);
        var invoiced = report.PaymentsByTiming.Single(p => p.Timing == PaymentTiming.Invoiced);
        invoiced.Orders.ShouldBe(2);
        invoiced.Revenue.ShouldBe(150m);
        invoiced.AmountPaid.ShouldBe(30m);
        invoiced.OutstandingAmount.ShouldBe(120m);
    }

    [Fact]
    public async Task Channel_filters_sales_but_customer_retention_uses_history_across_channels()
    {
        await using var fixture = new Fixture();
        var returning = fixture.Customer("Returning");
        var firstPurchase = fixture.Customer("First purchase");
        var storefrontOnly = fixture.Customer("Storefront only");
        fixture.Order(returning, Utc(19, 12), 500m, OrderChannel.Storefront);
        fixture.Order(returning, Utc(20, 12), 50m);
        fixture.Order(firstPurchase, Utc(18, 12), 700m, OrderChannel.Storefront)
            .Cancel("Test cancellation", Utc(18, 13), "test");
        fixture.Order(firstPurchase, Utc(21, 12), 100m);
        fixture.Order(storefrontOnly, Utc(22, 12), 60m, OrderChannel.Storefront);
        await fixture.Save();

        var report = await fixture.Report(new ReportQuery { From = From, To = To, Channel = OrderChannel.Backoffice });

        report.Summary.Orders.ShouldBe(2);
        report.Summary.Revenue.ShouldBe(150m);
        report.Summary.ActiveCustomers.ShouldBe(2);
        report.Summary.NewCustomers.ShouldBe(1);
        report.Summary.ReturningCustomers.ShouldBe(1);
        report.TopCustomers.Select(c => c.CustomerId).ShouldBe(new[] { firstPurchase.Id, returning.Id });
        report.OrdersByChannel.Where(c => c.Channel != OrderChannel.Backoffice).Sum(c => c.Orders).ShouldBe(0);
    }

    [Fact]
    public async Task Rankings_use_historical_item_prices_and_order_totals_instead_of_the_current_catalog()
    {
        await using var fixture = new Fixture();
        var customer = fixture.Customer("Customer");
        var category = new Category { Name = "Category" };
        var product = new Product { Name = "Current catalog name", Price = 999m, CategoryId = category.Id };
        fixture.Db.Categories.Add(category);
        fixture.Db.Products.Add(product);
        var first = fixture.Order(customer, Utc(20, 12), 10m, productId: product.Id, quantity: 2m, name: "Old snapshot");
        first.SetDeliveryFee(20m);
        first.ApplyDiscount(5m);
        var second = fixture.Order(customer, Utc(22, 12), 15m, productId: product.Id, quantity: 3m, name: "Recent snapshot");
        second.ApplyDiscount(5m);
        product.MarkDeleted(Utc(22, 13), "test");
        customer.MarkDeleted(Utc(22, 13), "test");
        await fixture.Save();

        var report = await fixture.Report();

        var rankedProduct = report.TopProducts.ShouldHaveSingleItem();
        rankedProduct.ProductId.ShouldBe(product.Id);
        rankedProduct.Name.ShouldBe("Recent snapshot");
        rankedProduct.Quantity.ShouldBe(5m);
        rankedProduct.Orders.ShouldBe(2);
        rankedProduct.Revenue.ShouldBe(65m);
        var rankedCustomer = report.TopCustomers.ShouldHaveSingleItem();
        rankedCustomer.CustomerId.ShouldBe(customer.Id);
        rankedCustomer.Revenue.ShouldBe(75m);
        rankedCustomer.AverageTicket.ShouldBe(37.5m);
        rankedCustomer.LastOrderAt.ShouldBe(Utc(22, 12));
        rankedCustomer.Type.ShouldBeNull();
    }

    [Fact]
    public async Task Customer_and_product_rankings_are_limited_to_the_ten_highest_revenues()
    {
        await using var fixture = new Fixture();
        for (var value = 1; value <= 12; value++)
        {
            fixture.Order(fixture.Customer($"Customer {value}"), Utc(21, 12), value);
        }
        await fixture.Save();

        var report = await fixture.Report();

        report.Summary.Revenue.ShouldBe(78m);
        report.TopCustomers.Count.ShouldBe(10);
        report.TopProducts.Count.ShouldBe(10);
        var expected = Enumerable.Range(3, 10).Reverse().Select(value => (decimal)value).ToArray();
        report.TopCustomers.Select(c => c.Revenue).ShouldBe(expected);
        report.TopProducts.Select(p => p.Revenue).ShouldBe(expected);
    }

    [Fact]
    public async Task Reports_follow_the_current_tenant_even_when_the_ef_model_and_context_are_reused()
    {
        await using var fixture = new Fixture();
        var firstTenantId = fixture.Db.CurrentTenantId;
        var firstCustomer = fixture.Customer("First tenant");
        fixture.Order(firstCustomer, Utc(21, 12), 40m);
        await fixture.Save();
        fixture.Db.ChangeTracker.Clear();

        var secondTenant = new Tenant("Second tenant", "second");
        fixture.CurrentTenant.Set(secondTenant.Id, secondTenant.Slug);
        fixture.Db.Tenants.Add(secondTenant);
        var secondCustomer = fixture.Customer("Second tenant");
        fixture.Order(secondCustomer, Utc(21, 12), 900m);
        await fixture.Save();
        fixture.Db.ChangeTracker.Clear();

        var second = await fixture.Report();
        second.Summary.Revenue.ShouldBe(900m);
        second.TopCustomers.ShouldHaveSingleItem().CustomerId.ShouldBe(secondCustomer.Id);

        fixture.CurrentTenant.Set(firstTenantId, "reports");
        var first = await fixture.Report();
        first.Summary.Revenue.ShouldBe(40m);
        first.Summary.ValidOrders.ShouldBe(1);
        first.TopCustomers.ShouldHaveSingleItem().CustomerId.ShouldBe(firstCustomer.Id);
        first.TopProducts.ShouldHaveSingleItem().Revenue.ShouldBe(40m);
    }

    [Fact]
    public async Task Deliveries_use_the_scheduled_local_date_independent_of_the_sales_channel()
    {
        await using var fixture = new Fixture();
        var customer = fixture.Customer();
        var orders = Enumerable.Range(0, 4)
            .Select(_ => fixture.Order(customer, Utc(20, 12), 10m, OrderChannel.Storefront))
            .ToList();
        var driver = new Driver { Name = "Driver" };
        var vehicle = new Vehicle { Plate = "ABC1D23" };
        fixture.Db.Drivers.Add(driver);
        fixture.Db.Vehicles.Add(vehicle);
        var completed = Delivery.Plan("D1", driver, vehicle, Utc(20, 3), [orders[0].Id, orders[1].Id], null);
        completed.Start(Utc(21, 10));
        completed.CompleteStop(completed.Stops.First().Id, Utc(21, 11), "Recipient", null);
        completed.FailStop(completed.Stops.Last().Id, Utc(21, 12), "Customer absent");
        completed.Finish(Utc(21, 13));
        var cancelled = Delivery.Plan("D2", driver, vehicle, Utc(21, 12), [orders[2].Id], null);
        cancelled.Cancel("Test cancellation", Utc(21, 13));
        var planned = Delivery.Plan("D3", driver, vehicle, Utc(23, 2, 59, 59), [orders[3].Id], null);
        var outside = Delivery.Plan("D4", driver, vehicle, Utc(23, 3), [orders[3].Id], null);
        fixture.Db.Deliveries.AddRange(completed, cancelled, planned, outside);
        await fixture.Save();

        var report = await fixture.Report(new ReportQuery { From = From, To = To, Channel = OrderChannel.Backoffice });

        report.Summary.Orders.ShouldBe(0);
        report.Deliveries.Runs.ShouldBe(3);
        report.Deliveries.Completed.ShouldBe(1);
        report.Deliveries.Cancelled.ShouldBe(1);
        report.Deliveries.Planned.ShouldBe(1);
        report.Deliveries.Stops.ShouldBe(3);
        report.Deliveries.DeliveredStops.ShouldBe(1);
        report.Deliveries.FailedStops.ShouldBe(1);
        report.Deliveries.PendingStops.ShouldBe(1);
        var rankedDriver = report.Drivers.ShouldHaveSingleItem();
        rankedDriver.DriverId.ShouldBe(driver.Id);
        rankedDriver.DriverName.ShouldBe(driver.Name);
        rankedDriver.Runs.ShouldBe(3);
        rankedDriver.DeliveredStops.ShouldBe(1);
        rankedDriver.FailedStops.ShouldBe(1);
        rankedDriver.PendingStops.ShouldBe(1);
    }

    [Fact]
    public async Task Default_period_ends_on_the_tenant_today_and_returns_a_complete_empty_series()
    {
        await using var fixture = new Fixture();
        await fixture.Save();

        var report = await fixture.Report(new ReportQuery());

        report.To.ShouldBe(To);
        report.From.ShouldBe(To.AddDays(-29));
        report.GeneratedAt.ShouldBe(Fixture.Now.UtcDateTime);
        report.DailySales.Count.ShouldBe(30);
        report.DailySales.ShouldAllBe(day => day.Orders == 0 && day.Revenue == 0m);
        report.Summary.AverageTicket.ShouldBe(0m);
        report.TopCustomers.ShouldBeEmpty();
        report.TopProducts.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_single_date_cannot_expand_past_the_limit_after_defaults_are_resolved()
    {
        await using var fixture = new Fixture();
        await fixture.Save();

        await Should.ThrowAsync<RequestValidationException>(() =>
            fixture.Report(new ReportQuery { From = To.AddDays(-366) }));
    }

    private static DateTime Utc(int day, int hour, int minute = 0, int second = 0) =>
        new(2026, 10, day, hour, minute, second, DateTimeKind.Utc);

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 23, 2, 0, 0, TimeSpan.Zero);
        private readonly TimeProvider _clock = new FixedClock(Now);
        public CurrentTenant CurrentTenant { get; } = new();
        public ApplicationDbContext Db { get; }

        public Fixture()
        {
            var tenant = new Tenant("Reports", "reports");
            tenant.Store.TimeZoneId = "America/Sao_Paulo";
            CurrentTenant.Set(tenant.Id, tenant.Slug);
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(new AuditableEntityInterceptor(new TestUser(), CurrentTenant, _clock))
                .Options;
            Db = new ApplicationDbContext(options, CurrentTenant);
            Db.Tenants.Add(tenant);
        }

        public Customer Customer(string name = "Customer")
        {
            var customer = new Customer { Name = name };
            Db.Customers.Add(customer);
            return customer;
        }

        public Order Order(Customer customer, DateTime placedAt, decimal unitPrice,
            OrderChannel channel = OrderChannel.Backoffice, PaymentTiming timing = PaymentTiming.OnDelivery,
            Guid? productId = null, decimal quantity = 1m, string name = "Product")
        {
            var number = Guid.NewGuid().ToString("N")[..12];
            var order = ArtDash.Domain.Orders.Order.Create(number, number, customer, channel, timing,
                PaymentMethod.Cash, new Address(), null, placedAt, "test");
            order.AddItem(productId ?? Guid.NewGuid(), name, null, UnitOfMeasure.Unit, unitPrice, quantity);
            Db.Orders.Add(order);
            return order;
        }

        public Task Save() => Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        public Task<ReportDto> Report(ReportQuery? filter = null) =>
            new GetReportsQueryHandler(Db, CurrentTenant, _clock).Handle(
                new GetReportsQuery(filter ?? new ReportQuery { From = From, To = To }), TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? Name => "test";
        public bool IsAuthenticated => false;
        public IReadOnlyCollection<string> Roles => [];
        public bool IsInRole(string role) => false;
    }
}