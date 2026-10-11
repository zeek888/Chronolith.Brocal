using ChronoLith.Brocal.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Convenient for local/compose environments; production can run a migration bundle instead.</summary>
    public bool ApplyMigrationsOnStartup { get; init; }

    /// <summary>Creates the "demo" company with sample data the first time (catalogs, units, tickets…).</summary>
    public bool SeedDemoTenant { get; init; }

    /// <summary>E-mail invited as administrator of the demo company (binds on first sign-in).</summary>
    public string? DemoAdminEmail { get; init; } = "admin@chronolith.local";
}

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (options.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("Aplicando migraciones de la base de datos de Brocal");
            await provider.GetRequiredService<BrocalDbContext>().Database.MigrateAsync(cancellationToken);
        }

        if (options.SeedDemoTenant)
        {
            await provider.GetRequiredService<DemoTenantSeeder>().SeedAsync(options.DemoAdminEmail, cancellationToken);
        }
    }
}
