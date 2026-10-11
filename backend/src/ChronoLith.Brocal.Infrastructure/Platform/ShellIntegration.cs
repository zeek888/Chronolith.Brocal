using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Platform;

public sealed class ModuleRegistrationOptions
{
    public const string SectionName = "ModuleRegistration";

    /// <summary>Publishes the manifest in the Shell at startup (module mode).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Public URL of <c>remoteEntry.json</c> of the frontend.</summary>
    public string RemoteEntryUrl { get; init; } = "http://localhost:4303/remoteEntry.json";

    /// <summary>Public URL of this API as seen by the browser (the Shell adds the token to calls to it).</summary>
    public string ApiUrl { get; init; } = "http://localhost:4303/api";

    public string RoutePath { get; init; } = "brocal";

    public int SortOrder { get; init; } = 20;
}

/// <summary>Manifest published to the Shell (same contract as modules/activities).</summary>
internal static class BrocalManifest
{
    public static object Create(ModuleRegistrationOptions registration) => new
    {
        code = PlatformPermissions.ModuleCode,
        name = "Brocal",
        description = "Documentación operativa de servicios petroleros: boletas, órdenes de trabajo, flota, personal, HSE, facturación y cobranza (multiempresa).",
        translations = new Dictionary<string, object>
        {
            ["en"] = new { name = "Brocal", description = "Operational records for oilfield services: tickets, work orders, fleet, personnel, HSE, invoicing and collections (multi-company)." }
        },
        icon = "receipt_long",
        color = "#0F766E",
        routePath = registration.RoutePath,
        remoteEntryUrl = registration.RemoteEntryUrl,
        exposedModule = "./routes",
        apiBaseUrl = registration.ApiUrl.TrimEnd('/'),
        appVersion = typeof(BrocalManifest).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0",
        sortOrder = registration.SortOrder,
        permissions = new object[]
        {
            new
            {
                code = PlatformPermissions.Access,
                name = "Acceso a Brocal",
                description = "Abrir Brocal; lo que cada usuario ve depende de su empresa y sus roles dentro de ella.",
                translations = new Dictionary<string, object> { ["en"] = new { name = "Brocal access", description = "Open Brocal; what each user sees depends on their company and roles in it." } }
            },
            new
            {
                code = PlatformPermissions.ManagePlatform,
                name = "Administrar la plataforma Brocal",
                description = "Alta de empresas, cuotas, módulos contratados, marca y suspensión.",
                translations = new Dictionary<string, object> { ["en"] = new { name = "Manage the Brocal platform", description = "Companies, quotas, contracted modules, branding and suspension." } }
            }
        },
        navigation = new object[]
        {
            new { label = "Inicio", path = "inicio", icon = "space_dashboard", permissionCode = PlatformPermissions.Access, sortOrder = 10, translations = new Dictionary<string, object> { ["en"] = new { label = "Home" } } },
            new { label = "Boletas de servicio", path = "boletas", icon = "receipt_long", permissionCode = PlatformPermissions.Access, sortOrder = 20, translations = new Dictionary<string, object> { ["en"] = new { label = "Service tickets" } } },
            new { label = "Órdenes de trabajo", path = "ordenes", icon = "event_note", permissionCode = PlatformPermissions.Access, sortOrder = 30, translations = new Dictionary<string, object> { ["en"] = new { label = "Work orders" } } },
            new { label = "Dashboard gerencial", path = "dashboard", icon = "monitoring", permissionCode = PlatformPermissions.Access, sortOrder = 40, translations = new Dictionary<string, object> { ["en"] = new { label = "Management dashboard" } } },
            new { label = "Administración de la plataforma", path = "plataforma/empresas", icon = "domain_add", permissionCode = PlatformPermissions.ManagePlatform, sortOrder = 90, translations = new Dictionary<string, object> { ["en"] = new { label = "Platform administration" } } }
        },
        widgets = new object[]
        {
            new
            {
                key = "resumen-mes", name = "Brocal · servicios del mes", description = "Boletas firmadas, importe del mes y boletas pendientes de facturar.",
                component = "./widgets/resumen-mes", defaultCols = 6, defaultRows = 2, permissionCode = PlatformPermissions.Access,
                translations = new Dictionary<string, object> { ["en"] = new { name = "Brocal · services this month", description = "Signed tickets, amount of the month and tickets pending invoicing." } }
            },
            new
            {
                key = "vencimientos", name = "Brocal · vencimientos", description = "Permisos de unidades y documentos del personal vencidos o por vencer.",
                component = "./widgets/vencimientos", defaultCols = 4, defaultRows = 3, permissionCode = PlatformPermissions.Access,
                translations = new Dictionary<string, object> { ["en"] = new { name = "Brocal · expiries", description = "Unit permits and personnel documents expired or about to expire." } }
            },
            new
            {
                key = "flota", name = "Brocal · estado de la flota", description = "Unidades operando, disponibles y en taller.",
                component = "./widgets/flota", defaultCols = 4, defaultRows = 2, permissionCode = PlatformPermissions.Access,
                translations = new Dictionary<string, object> { ["en"] = new { name = "Brocal · fleet status", description = "Units operating, available and in the workshop." } }
            }
        }
    };
}

/// <summary>Registers the manifest in the Shell at startup (idempotent upsert), retrying until the platform answers.</summary>
internal sealed class ModuleRegistrationService(
    IHttpClientFactory httpClientFactory,
    ServiceAccountTokenProvider tokens,
    IOptions<ModuleRegistrationOptions> registration,
    IOptions<ShellOptions> shell,
    ILogger<ModuleRegistrationService> logger) : BackgroundService
{
    public const string HttpClientName = "shell-registration";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(3);
        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                using var client = httpClientFactory.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Put, $"{shell.Value.ApiUrl!.TrimEnd('/')}/api/modules/manifest")
                {
                    Content = JsonContent.Create(BrocalManifest.Create(registration.Value))
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(stoppingToken));

                using var response = await client.SendAsync(request, stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"El Shell respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(stoppingToken)}");
                }

                logger.LogInformation("Módulo Brocal registrado en el Shell ({RemoteEntry}).", registration.Value.RemoteEntryUrl);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("No se pudo registrar Brocal en el Shell (intento {Attempt}): {Error}. Reintento en {Delay}s.", attempt, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(1).Ticks));
            }
        }
    }
}

/// <summary>Shared catalogs of the Shell (wells, fields, companies) read with the user's token.</summary>
internal sealed class ShellCatalogClient(HttpClient http, IHttpContextAccessor httpContext, IOptions<ShellOptions> shell, IMemoryCache cache) : IShellCatalogClient
{
    private static readonly HashSet<string> Catalogs = ["wells", "fields", "facilities", "companies", "assets", "basins", "rigs"];

    public bool IsAvailable => !string.IsNullOrWhiteSpace(shell.Value.ApiUrl);

    public async Task<IReadOnlyList<ShellLookupItem>> LookupAsync(string catalog, CancellationToken cancellationToken)
    {
        if (!IsAvailable || !Catalogs.Contains(catalog))
        {
            return [];
        }

        var header = httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !AuthenticationHeaderValue.TryParse(header, out var token))
        {
            return [];
        }

        return await cache.GetOrCreateAsync($"shell-lookup:{catalog}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/catalogs/{catalog}/lookup?activeOnly=false");
            request.Headers.Authorization = token;
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);
                return (IReadOnlyList<ShellLookupItem>)[];
            }

            return await response.Content.ReadFromJsonAsync<List<ShellLookupItem>>(cancellationToken) ?? [];
        }) ?? [];
    }
}

/// <summary>
/// Default theme and layout of ChronoLith (Administración → Apariencia) for standalone mode, read from the Shell API
/// with the user's token. Null when the Shell is not reachable (the frontend then uses the ChronoLith palette).
/// </summary>
internal sealed class ChronoLithAppearance(HttpClient http, IHttpContextAccessor httpContext, IOptions<ShellOptions> shell, IMemoryCache cache, ILogger<ChronoLithAppearance> logger)
    : IChronoLithAppearance
{
    public async Task<ChronoLithTheme?> GetDefaultThemeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shell.Value.ApiUrl))
        {
            return null;
        }

        var header = httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !AuthenticationHeaderValue.TryParse(header, out var token))
        {
            return null;
        }

        return await cache.GetOrCreateAsync("chronolith-theme", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            try
            {
                using var themesRequest = new HttpRequestMessage(HttpMethod.Get, "api/appearance/themes");
                themesRequest.Headers.Authorization = token;
                using var themesResponse = await http.SendAsync(themesRequest, cancellationToken);
                using var layoutRequest = new HttpRequestMessage(HttpMethod.Get, "api/appearance/layout");
                layoutRequest.Headers.Authorization = token;
                using var layoutResponse = await http.SendAsync(layoutRequest, cancellationToken);
                if (!themesResponse.IsSuccessStatusCode)
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                    return null;
                }

                var themes = await themesResponse.Content.ReadFromJsonAsync<List<ShellTheme>>(cancellationToken) ?? [];
                var layout = layoutResponse.IsSuccessStatusCode ? await layoutResponse.Content.ReadFromJsonAsync<ShellLayout>(cancellationToken) : null;
                var theme = themes.FirstOrDefault(t => t.IsDefault) ?? themes.FirstOrDefault();
                return theme is null
                    ? null
                    : new ChronoLithTheme(theme.Name, theme.Palette, layout?.AppName, layout?.LogoUrl, layout?.Density ?? 0, layout?.DefaultColorScheme ?? "light");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                logger.LogInformation("Tema de ChronoLith no disponible: {Error}", ex.Message);
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                return null;
            }
        });
    }

    private sealed record ShellTheme(Guid Id, string Name, bool IsDefault, ThemePalette Palette);

    private sealed record ShellLayout(string AppName, string? LogoUrl, int Density, string DefaultColorScheme);
}
