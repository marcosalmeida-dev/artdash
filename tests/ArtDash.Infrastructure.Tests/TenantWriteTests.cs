using ArtDash.Application.Common.Interfaces;
using ArtDash.Domain.Catalog;
using ArtDash.Domain.Customers;
using ArtDash.Infrastructure.MultiTenancy;
using ArtDash.Infrastructure.Persistence;
using ArtDash.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace ArtDash.Infrastructure.Tests;

public sealed class TenantWriteTests
{
    [Fact]
    public async Task An_explicit_tenant_cannot_insert_into_another_company()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new CurrentTenant();
        tenant.Set(Guid.NewGuid(), "current");
        await using var db = CreateContext(tenant);
        db.Categories.Add(new Category { Name = "Foreign", TenantId = Guid.NewGuid() });

        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task Changing_a_rows_tenant_cannot_bypass_write_isolation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new CurrentTenant();
        tenant.Set(Guid.NewGuid(), "original");
        await using var db = CreateContext(tenant);
        var category = new Category { Name = "Original" };
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        var anotherTenant = Guid.NewGuid();
        tenant.Set(anotherTenant, "other");
        category.TenantId = anotherTenant;
        category.Name = "Changed";

        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task Updates_require_a_resolved_tenant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resolved = new CurrentTenant();
        resolved.Set(Guid.NewGuid(), "original");
        var database = Guid.NewGuid().ToString();
        await using var original = CreateContext(resolved, database);
        var category = new Category { Name = "Original" };
        original.Categories.Add(category);
        await original.SaveChangesAsync(cancellationToken);

        await using var unresolved = CreateContext(new CurrentTenant(), database);
        unresolved.Attach(category);
        category.Name = "Changed";

        await Should.ThrowAsync<InvalidOperationException>(() => unresolved.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task Owned_address_changes_cannot_update_another_company()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tenant = new CurrentTenant();
        tenant.Set(Guid.NewGuid(), "original");
        await using var db = CreateContext(tenant);
        var customer = new Customer { Name = "Original" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);

        tenant.Set(Guid.NewGuid(), "other");
        customer.Address.Street = "Changed street";

        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(cancellationToken));
    }

    private static ApplicationDbContext CreateContext(CurrentTenant tenant, string? database = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(database ?? Guid.NewGuid().ToString())
            .AddInterceptors(new AuditableEntityInterceptor(new TestUser(), tenant, TimeProvider.System))
            .Options;
        return new ApplicationDbContext(options, tenant);
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
