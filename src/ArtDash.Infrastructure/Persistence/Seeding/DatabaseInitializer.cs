using ArtDash.Infrastructure.MultiTenancy;
using ArtDash.Infrastructure.Options;
using Microsoft.AspNetCore.Identity;

namespace ArtDash.Infrastructure.Persistence.Seeding;

/// <summary>
/// Creates/updates the database schema and seeds roles, tenants, their admin users and (optionally)
/// demo data. Each tenant is seeded in its own scope with the tenant set explicitly, because the
/// query filters and Identity lookups depend on it.
/// </summary>
public sealed class DatabaseInitializer(IServiceScopeFactory scopeFactory, IOptions<SeedOptions> seedOptions, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await EnsureSchemaAsync(db, cancellationToken);
            await EnsureRolesAsync(scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>());
        }

        if (!seedOptions.Value.Enabled)
        {
            return;
        }

        foreach (var seed in seedOptions.Value.Tenants.Where(t => !string.IsNullOrWhiteSpace(t.Slug)))
        {
            await SeedTenantAsync(seed, cancellationToken);
        }

        using var invalidateScope = scopeFactory.CreateScope();
        invalidateScope.ServiceProvider.GetRequiredService<ITenantStore>().Invalidate();
    }

    private async Task EnsureSchemaAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync(cancellationToken);
            return;
        }

        // No migration yet (fresh clone): create the schema straight from the model so the app runs.
        // Generate the first migration before going to production — see README.
        logger.LogWarning("No EF Core migrations found; creating the database with EnsureCreated. Run 'dotnet ef migrations add Initial' before deploying.");
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    private static async Task EnsureRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        var descriptions = new Dictionary<string, string>
        {
            [AppRoles.Admin] = "Acesso total à empresa",
            [AppRoles.Manager] = "Pedidos, entregas, cadastros",
            [AppRoles.Customer] = "Loja e acompanhamento dos próprios pedidos",
            [AppRoles.Driver] = "Aplicativo do motorista"
        };

        foreach (var (role, description) in descriptions)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole(role) { Description = description });
            }
        }
    }

    private async Task SeedTenantAsync(SeedTenant seed, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var slug = Tenant.NormalizeSlug(seed.Slug);

        var tenant = await db.Tenants.Include(t => t.Domains).FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);
        if (tenant is null)
        {
            tenant = new Tenant(string.IsNullOrWhiteSpace(seed.Name) ? slug : seed.Name, slug);
            tenant.Branding.Tagline = seed.Tagline;
            tenant.Branding.PrimaryColor = seed.PrimaryColor ?? tenant.Branding.PrimaryColor;
            tenant.Branding.SecondaryColor = seed.SecondaryColor ?? tenant.Branding.SecondaryColor;
            tenant.Branding.AccentColor = seed.AccentColor ?? tenant.Branding.AccentColor;
            tenant.Branding.ConsoleColor = seed.ConsoleColor ?? tenant.Branding.ConsoleColor;
            db.Tenants.Add(tenant);
            logger.LogInformation("Seeding tenant {Slug}", slug);
        }

        foreach (var host in seed.Hosts)
        {
            var normalized = TenantDomain.NormalizeHost(host);
            var takenByOther = await db.TenantDomains.AnyAsync(d => d.Host == normalized && d.TenantId != tenant.Id, cancellationToken);
            if (!takenByOther)
            {
                tenant.AddDomain(normalized);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        services.GetRequiredService<ICurrentTenantSetter>().Set(tenant.Id, tenant.Slug);

        await EnsureAdminAsync(services, tenant, seed);

        if (seed.SampleData && !await db.Products.AnyAsync(cancellationToken))
        {
            await SampleData.SeedAsync(services, db, tenant, cancellationToken);
            logger.LogInformation("Sample data created for tenant {Slug}", slug);
        }
    }

    private async Task EnsureAdminAsync(IServiceProvider services, Tenant tenant, SeedTenant seed)
    {
        if (string.IsNullOrWhiteSpace(seed.AdminEmail) || string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var email = seed.AdminEmail.Trim().ToLowerInvariant();

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            TenantId = tenant.Id,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = seed.AdminName,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        var created = await userManager.CreateAsync(admin, seed.AdminPassword);
        if (!created.Succeeded)
        {
            logger.LogError("Could not create the admin of {Slug}: {Errors}", tenant.Slug, string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, AppRoles.Admin);
    }
}
