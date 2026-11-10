using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Infrastructure.Authentication;
using ChronoLith.Brocal.Infrastructure.Authorization;
using ChronoLith.Brocal.Infrastructure.Documents;
using ChronoLith.Brocal.Infrastructure.DomainEvents;
using ChronoLith.Brocal.Infrastructure.Files;
using ChronoLith.Brocal.Infrastructure.Fiscal;
using ChronoLith.Brocal.Infrastructure.Identity;
using ChronoLith.Brocal.Infrastructure.Jobs;
using ChronoLith.Brocal.Infrastructure.Licensing;
using ChronoLith.Brocal.Infrastructure.Notifications;
using ChronoLith.Brocal.Infrastructure.Persistence;
using ChronoLith.Brocal.Infrastructure.Persistence.Interceptors;
using ChronoLith.Brocal.Infrastructure.Persistence.Seeding;
using ChronoLith.Brocal.Infrastructure.Platform;
using ChronoLith.Brocal.Infrastructure.Tenancy;
using ChronoLith.Licensing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PdfSharp.Fonts;

namespace ChronoLith.Brocal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        services.AddOptions<TenancyOptions>().Bind(configuration.GetSection(TenancyOptions.SectionName));
        services.AddOptions<ShellOptions>().Bind(configuration.GetSection(ShellOptions.SectionName));
        services.AddOptions<ModuleRegistrationOptions>().Bind(configuration.GetSection(ModuleRegistrationOptions.SectionName));
        services.AddOptions<IdentityOptions>().Bind(configuration.GetSection(IdentityOptions.SectionName));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.AddOptions<WhatsAppOptions>().Bind(configuration.GetSection(WhatsAppOptions.SectionName));
        services.AddOptions<JobsOptions>().Bind(configuration.GetSection(JobsOptions.SectionName));
        services.AddOptions<IndicatorOptions>().Bind(configuration.GetSection(IndicatorOptions.SectionName));

        AddPersistence(services, configuration);
        AddSecurity(services, configuration);
        AddFilesAndDocuments(services, configuration);
        AddIntegrations(services, configuration);
        AddLicensing(services, configuration);

        return services;
    }

    /// <summary><c>Tenancy:Mode = Standalone</c>: own Keycloak realm, platform administrators and ChronoLith license.</summary>
    public static bool IsStandaloneMode(this IConfiguration configuration) =>
        string.Equals(configuration[$"{TenancyOptions.SectionName}:Mode"], "Standalone", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ChronoLith license of the installation, standalone mode only (as a module of ChronoLith the Shell validates
    /// the license and nothing is registered). Program initializes it after migrating the database.
    /// </summary>
    private static void AddLicensing(IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.IsStandaloneMode())
        {
            return;
        }

        // Public Keycloak URL: development licenses are accepted only when it is localhost (or in Development).
        services.AddChronoLithLicensing(configuration, LicenseProduct, sp => sp.GetRequiredService<IOptions<KeycloakOptions>>().Value.AuthServerUrl);
        services.AddScoped<ILicenseStore, LicenseStore>();
        services.AddScoped<ILicenseUsageProvider, LicenseUsageProvider>();
    }

    /// <summary>Product code of Brocal in the ChronoLith licenses.</summary>
    public const string LicenseProduct = "brocal";

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BrocalDb")
            ?? throw new InvalidOperationException("Falta la cadena de conexión 'BrocalDb'.");

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddScoped<TenantInterceptor>();
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<AuditTrailInterceptor>();
        services.AddScoped<IndicatorChangeInterceptor>();

        services.AddDbContext<BrocalDbContext>((serviceProvider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history", BrocalDbContext.Schema)
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                .EnableRetryOnFailure(maxRetryCount: 5))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                serviceProvider.GetRequiredService<TenantInterceptor>(),
                serviceProvider.GetRequiredService<AuditableEntityInterceptor>(),
                serviceProvider.GetRequiredService<AuditTrailInterceptor>(),
                serviceProvider.GetRequiredService<IndicatorChangeInterceptor>()));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrocalDbContext>());
        services.AddScoped<IFolioService, FolioService>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<DemoTenantSeeder>();

        services.AddHealthChecks().AddDbContextCheck<BrocalDbContext>("database", tags: ["ready"]);
    }

    private static void AddSecurity(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection(KeycloakOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IUserContext, UserContext>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<IIsolatedWriter, IsolatedWriter>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.ConfigureOptions<JwtBearerOptionsSetup>();

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, BrocalPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, TenantPermissionHandler>();
        services.AddScoped<IAuthorizationHandler, PlatformAdminHandler>();

        if (configuration.IsStandaloneMode())
        {
            services.AddScoped<IPlatformAccessService, StandalonePlatformAccessService>();
        }
        else
        {
            services.AddHttpClient<IPlatformAccessService, ShellPlatformAccessService>((sp, http) =>
            {
                http.BaseAddress = new Uri($"{sp.GetRequiredService<IOptions<ShellOptions>>().Value.ApiUrl?.TrimEnd('/')}/");
                http.Timeout = TimeSpan.FromSeconds(10);
            });
        }

        var keysPath = configuration["DataProtection:KeysPath"];
        var dataProtection = services.AddDataProtection().SetApplicationName("chronolith-brocal");
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        services.AddSingleton<ISecretProtector, SecretProtector>();
    }

    private static void AddFilesAndDocuments(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileStorageOptions>().Bind(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<FileUrlSigner>();
        services.AddScoped<IFileService, FileService>();

        // PDFsharp needs the TrueType files on Linux: one resolver for the whole process.
        if (GlobalFontSettings.FontResolver is null)
        {
            GlobalFontSettings.FontResolver = new BrocalFontResolver(configuration["Pdf:FontsPath"]);
        }

        services.AddSingleton<IPdfRenderer, PdfRenderer>();
        services.AddSingleton<IExcelWriter, ExcelWriter>();

        services.AddSingleton<IPacProvider, SimulatedPacProvider>();
        services.AddSingleton<IPacGateway, PacGateway>();
    }

    private static void AddIntegrations(IServiceCollection services, IConfiguration configuration)
    {
        var standalone = configuration.IsStandaloneMode();

        services.AddSingleton<ServiceAccountTokenProvider>();
        services.AddTransient<ServiceAccountTokenHandler>();
        services.AddHttpClient(ServiceAccountTokenProvider.HttpClientName).AddStandardResilienceHandler();
        services.AddHttpClient<IIdentityProvisioning, KeycloakProvisioning>((sp, http) =>
                http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<KeycloakOptions>>().Value.AdminApiUrl))
            .AddHttpMessageHandler<ServiceAccountTokenHandler>();

        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddHttpClient<IWhatsAppSender, WhatsAppCloudSender>(http => http.Timeout = TimeSpan.FromSeconds(20));

        void ConfigureShell(IServiceProvider sp, HttpClient http)
        {
            var url = sp.GetRequiredService<IOptions<ShellOptions>>().Value.ApiUrl;
            http.BaseAddress = new Uri($"{(string.IsNullOrWhiteSpace(url) ? "http://localhost" : url.TrimEnd('/'))}/");
            http.Timeout = TimeSpan.FromSeconds(10);
        }

        services.AddHttpClient<IChronoLithAppearance, ChronoLithAppearance>(ConfigureShell);
        services.AddHttpClient(ModuleRegistrationService.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(15));

        // Shared catalogs of ChronoLith: local replica fed by the change feed with the service account of the Shell
        // (the module's own one, or Shell:ClientId standalone) and writes to the master; disabled without them.
        services.AddSingleton<ShellServiceAccountTokenProvider>();
        services.AddScoped<ISharedCatalogs, SharedCatalogReplica>();
        services.AddScoped<ICatalogReplica, CatalogReplicaStore>();
        services.AddHttpClient<ICatalogFeedClient, ShellCatalogFeedClient>((sp, http) =>
        {
            ConfigureShell(sp, http);
            http.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient<IMasterCatalogWriter, ShellMasterCatalogWriter>(ConfigureShell);
        services.AddSingleton<ICatalogSyncRunner, CatalogSyncRunner>();

        // Indicators published to the Shell (same connection and service account): outbox written by
        // IndicatorChangeInterceptor in the business transaction, processed by IndicatorPublishingService.
        services.AddSingleton(sp => IndicatorAvailability.Settings(
            sp.GetRequiredService<IOptions<IndicatorOptions>>().Value,
            sp.GetRequiredService<IOptions<ModuleRegistrationOptions>>().Value,
            sp.GetRequiredService<IOptions<TenancyOptions>>().Value));
        services.AddScoped<IIndicatorOutbox, IndicatorOutboxStore>();
        services.AddHttpClient<IIndicatorClient, ShellIndicatorClient>((sp, http) =>
        {
            ConfigureShell(sp, http);
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IndicatorPublishingRunner>();

        if (!standalone && configuration.GetValue($"{ModuleRegistrationOptions.SectionName}:Enabled", true))
        {
            services.AddHostedService<ModuleRegistrationService>();
        }

        // The runner also serves "Ejecutar ahora" of the platform administrator when background jobs are off.
        services.AddSingleton<IScheduledJobsRunner, ScheduledJobsRunner>();
        if (configuration.GetValue($"{JobsOptions.SectionName}:Enabled", true))
        {
            services.AddHostedService<OutboxDispatcher>();
            services.AddHostedService<ScheduledJobsService>();
            services.AddHostedService<CatalogSyncService>();
            services.AddHostedService<IndicatorPublishingService>();
        }
    }
}
