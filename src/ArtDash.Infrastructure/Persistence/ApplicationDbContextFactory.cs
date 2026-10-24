using ArtDash.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ArtDash.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the context at design time (migrations, database update) without starting
/// the web host. Connection string, first match wins:
/// <list type="number">
///   <item>environment variable <c>ARTDASH_CONNECTION</c>;</item>
///   <item><c>ConnectionStrings:DefaultConnection</c> of the web host (<c>src/ArtDash.UI/appsettings*.json</c>,
///   environment from <c>ASPNETCORE_ENVIRONMENT</c>, Development by default);</item>
///   <item>LocalDB.</item>
/// </list>
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string LocalDb =
        "Server=(localdb)\\mssqllocaldb;Database=ArtDash;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ResolveConnectionString(), sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .Options;

        return new ApplicationDbContext(options, new CurrentTenant());
    }

    private static string ResolveConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("ARTDASH_CONNECTION");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var hostFolder = HostFolderCandidates().FirstOrDefault(folder => File.Exists(Path.Combine(folder, "appsettings.json")));
        if (hostFolder is null)
        {
            return LocalDb;
        }

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(hostFolder)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        return string.IsNullOrWhiteSpace(connectionString) ? LocalDb : connectionString;
    }

    /// <summary>
    /// <c>dotnet ef</c> runs with the startup project as working directory; the other candidates cover
    /// running it from the solution folder or from this project.
    /// </summary>
    private static IEnumerable<string> HostFolderCandidates()
    {
        var current = Directory.GetCurrentDirectory();
        yield return current;
        yield return Path.Combine(current, "src", "ArtDash.UI");
        yield return Path.GetFullPath(Path.Combine(current, "..", "ArtDash.UI"));
    }
}
