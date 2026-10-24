using System.Globalization;
using ArtDash.Application;
using ArtDash.Infrastructure;
using ArtDash.Infrastructure.Persistence.Seeding;
using ArtDash.UI.Client;
using ArtDash.UI.Configuration;
using Serilog;

// Captures startup failures before the configured logger exists.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // Brazilian formatting (R$, dd/MM/yyyy) for the API, the validators and the Blazor circuits.
    var culture = CultureInfo.GetCultureInfo("pt-BR");
    CultureInfo.DefaultThreadCurrentCulture = culture;
    CultureInfo.DefaultThreadCurrentUICulture = culture;

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(builder.Environment.ContentRootPath, "App_Data", "logs", "artdash-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14));

    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddWebHost(builder.Configuration, builder.Environment)
        .AddUiClient(builder.Configuration);

    var app = builder.Build();

    app.UseWebHost();
    app.MapWebHostEndpoints();

    // Migrations (or EnsureCreated on a fresh clone), roles, seed tenants and demo data.
    await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException is how the EF Core tools stop the host after reading its services.
    Log.Fatal(ex, "ArtDash terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
