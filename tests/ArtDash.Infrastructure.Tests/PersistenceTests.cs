using ArtDash.Domain.Catalog;
using ArtDash.Application.Common.Interfaces;
using ArtDash.Domain.Common;
using ArtDash.Domain.Customers;
using ArtDash.Domain.Orders;
using ArtDash.Infrastructure.MultiTenancy;
using ArtDash.Infrastructure.Persistence;
using ArtDash.Infrastructure.Persistence.Interceptors;
using ArtDash.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ArtDash.Infrastructure.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void The_sql_server_model_generates_a_complete_schema()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelValidation;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var db = new ApplicationDbContext(options, new CurrentTenant());

        var script = db.Database.GenerateCreateScript();

        script.ShouldContain("CREATE TABLE [Orders]");
        script.ShouldContain("CREATE TABLE [OrderStatusHistory]");
        script.ShouldContain("CREATE TABLE [Deliveries]");
    }

    [Fact]
    public async Task Order_items_payments_and_history_survive_reloading()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new CurrentTenant();
        tenant.Set(Guid.NewGuid(), "test");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditableEntityInterceptor(new TestUser(), tenant, TimeProvider.System)).Options;
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var customer = new Customer { Name = "Test customer" };
        var order = Order.Create("000001", "TESTCODE", customer, OrderChannel.Backoffice,
            PaymentTiming.OnDelivery, PaymentMethod.Cash, new Address(), null, now, "test");
        order.AddItem(Guid.NewGuid(), "Product", null, UnitOfMeasure.Unit, 10m, 2);
        order.RegisterPayment(PaymentMethod.Cash, 20m, now, null, "test");
        order.Confirm(now, "test");

        await using (var db = new ApplicationDbContext(options, tenant))
        {
            db.Customers.Add(customer);
            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using var reloaded = new ApplicationDbContext(options, tenant);
        var stored = await reloaded.Orders.Include(o => o.Items).Include(o => o.Payments)
            .Include(o => o.StatusHistory).SingleAsync(cancellationToken);

        stored.Items.Single().Quantity.ShouldBe(2m);
        stored.Payments.Single().Amount.ShouldBe(20m);
        stored.StatusHistory.Select(h => h.ToStatus).ShouldBe(
            new[] { OrderStatus.Pending, OrderStatus.Confirmed });
    }

    [Fact]
    public async Task Cached_query_filters_follow_the_current_tenant_and_hide_deleted_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new CurrentTenant();
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options, tenant);
        var deleted = new Category { Name = "Deleted", TenantId = firstTenant };
        deleted.MarkDeleted(DateTime.UtcNow, "test");
        db.Categories.AddRange(
            new Category { Name = "First", TenantId = firstTenant },
            new Category { Name = "Second", TenantId = secondTenant }, deleted);
        await db.SaveChangesAsync(cancellationToken);

        (await db.Categories.CountAsync(cancellationToken)).ShouldBe(0);
        tenant.Set(firstTenant, "first");
        (await db.Categories.SingleAsync(cancellationToken)).Name.ShouldBe("First");
        tenant.Set(secondTenant, "second");
        (await db.Categories.SingleAsync(cancellationToken)).Name.ShouldBe("Second");
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
