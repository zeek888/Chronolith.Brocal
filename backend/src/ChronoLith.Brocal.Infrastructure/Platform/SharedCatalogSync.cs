using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.SharedCatalogs;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Authentication;
using ChronoLith.Brocal.Infrastructure.Identity;
using ChronoLith.Brocal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Platform;

/// <summary>
/// When the shared catalogs of ChronoLith are in use: a Shell API and a service account of the realm
/// <c>chronolith</c> (the module's own in module mode; <c>Shell:ClientId</c> standalone). Without them Brocal works
/// with its own catalogs only.
/// </summary>
internal static class SharedCatalogAvailability
{
    public static bool IsEnabled(ShellOptions shell, KeycloakOptions keycloak, bool standalone)
    {
        if (string.IsNullOrWhiteSpace(shell.ApiUrl))
        {
            return false;
        }

        return standalone ? shell.HasOwnServiceAccount : !string.IsNullOrWhiteSpace(keycloak.ClientSecret);
    }
}

/// <summary>Lookups of the shared catalogs over the local replica, cached briefly per catalog.</summary>
internal sealed class SharedCatalogReplica(BrocalDbContext db, IMemoryCache cache, IOptions<ShellOptions> shell, IOptions<KeycloakOptions> keycloak, IOptions<Application.Features.Platform.TenancyOptions> tenancy) : ISharedCatalogs
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public bool IsAvailable => SharedCatalogAvailability.IsEnabled(shell.Value, keycloak.Value, PlatformModes.IsStandalone(tenancy.Value));

    public async Task<IReadOnlyList<ShellLookupItem>> LookupAsync(string catalog, bool activeOnly, Guid? parentId, CancellationToken cancellationToken)
    {
        if (!IsAvailable || !SharedCatalogNames.IsValid(catalog))
        {
            return [];
        }

        var items = await cache.GetOrCreateAsync(CacheKey(catalog), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await db.SharedCatalogItems.AsNoTracking()
                .Where(i => i.Catalog == catalog)
                .OrderBy(i => i.Name).ThenBy(i => i.Code)
                .Select(i => new ShellLookupItem(i.Id, i.Code, i.Name, i.ParentId, i.IsActive))
                .ToListAsync(cancellationToken);
        }) ?? [];

        return items.Where(i => (!activeOnly || i.IsActive) && (parentId is null || i.ParentId == parentId)).ToList();
    }

    public async Task<SharedCatalogItem?> FindAsync(string catalog, Guid id, CancellationToken cancellationToken) =>
        IsAvailable ? await db.SharedCatalogItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.Catalog == catalog, cancellationToken) : null;

    public void Changed(string catalog) => cache.Remove(CacheKey(catalog));

    private static string CacheKey(string catalog) => $"shared-catalog:{catalog}";
}

/// <summary>Replica storage over the Brocal database (the same unit of work as the reconciler of linked records).</summary>
internal sealed class CatalogReplicaStore(BrocalDbContext db, ISharedCatalogs lookups) : ICatalogReplica
{
    public async Task<CatalogFeedState> GetStateAsync(CancellationToken cancellationToken)
    {
        var state = await db.CatalogFeedStates.FirstOrDefaultAsync(s => s.Id == CatalogFeedState.SingletonId, cancellationToken);
        if (state is null)
        {
            state = CatalogFeedState.Create();
            db.CatalogFeedStates.Add(state);
        }

        return state;
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken) => db.SharedCatalogItems.AnyAsync(cancellationToken);

    public async Task<Dictionary<Guid, SharedCatalogItem>> FindAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? [] : await db.SharedCatalogItems.Where(i => list.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<SharedCatalogItem>> AllAsync(string catalog, CancellationToken cancellationToken) =>
        await db.SharedCatalogItems.Where(i => i.Catalog == catalog).ToListAsync(cancellationToken);

    public void Add(SharedCatalogItem item) => db.SharedCatalogItems.Add(item);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var changed = db.ChangeTracker.Entries<SharedCatalogItem>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity.Catalog)
            .Distinct()
            .ToList();

        await db.SaveChangesAsync(cancellationToken);
        foreach (var catalog in changed)
        {
            lookups.Changed(catalog);
        }
    }
}

/// <summary>Feed, report and full load against the Shell with the service account (<c>chronolith-module-registrar</c>).</summary>
internal sealed class ShellCatalogFeedClient(
    HttpClient http,
    ShellServiceAccountTokenProvider tokens,
    IOptions<ShellOptions> shell,
    IOptions<KeycloakOptions> keycloak,
    IOptions<Application.Features.Platform.TenancyOptions> tenancy,
    ILogger<ShellCatalogFeedClient> logger) : ICatalogFeedClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => SharedCatalogAvailability.IsEnabled(shell.Value, keycloak.Value, PlatformModes.IsStandalone(tenancy.Value));

    public async Task<CatalogFeedPage> PullAsync(long since, int limit, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/catalogs/changes?consumer={CatalogFeedConsumer.ConsumerCode}&since={since}&limit={limit}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;

        var changes = new List<CatalogFeedChange>();
        if (root.TryGetProperty("changes", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in array.EnumerateArray())
            {
                changes.Add(new CatalogFeedChange(
                    c.GetProperty("id").GetInt64(),
                    MasterJson.Str(c, "catalog") ?? string.Empty,
                    MasterJson.Id(c, "entityId") ?? Guid.Empty,
                    MasterJson.Str(c, "code") ?? string.Empty,
                    MasterJson.Str(c, "name") ?? string.Empty,
                    MasterJson.Str(c, "action") ?? "updated",
                    MasterJson.Date(c, "occurredAt") ?? DateTimeOffset.MinValue,
                    MasterJson.Str(c, "actor"),
                    MasterJson.Str(c, "source"),
                    c.TryGetProperty("data", out var data) ? data.Clone() : default,
                    MasterJson.Bool(c, "replay") ?? false));
            }
        }

        return new CatalogFeedPage(
            root.TryGetProperty("cursor", out var cursor) ? cursor.GetInt64() : since,
            root.TryGetProperty("latest", out var latest) ? latest.GetInt64() : 0,
            MasterJson.Bool(root, "hasMore") ?? false,
            MasterJson.Bool(root, "requiresFullSync") ?? false,
            changes);
    }

    public async Task ReportAsync(long cursor, IReadOnlyList<long> applied, IReadOnlyList<CatalogFeedError> errors, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            cursor,
            version = typeof(ShellCatalogFeedClient).Assembly.GetName().Version?.ToString(3),
            applied,
            errors = errors.Select(e => new { changeId = e.ChangeId, message = e.Message })
        }, Json);

        using var response = await SendAsync(HttpMethod.Put, $"api/catalogs/changes/consumers/{CatalogFeedConsumer.ConsumerCode}", body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ShellCatalogRow>?> ReadAllAsync(string catalog, CancellationToken cancellationToken)
    {
        var rows = new List<ShellCatalogRow>();
        var page = 1;
        while (true)
        {
            using var response = await SendAsync(HttpMethod.Get, $"api/catalogs/{catalog}?page={page}&pageSize=200", null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound && page == 1)
            {
                logger.LogInformation("El Shell no tiene el catálogo {Catalog}; se omite.", catalog);
                return null;
            }

            await EnsureSuccessAsync(response, cancellationToken);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var items = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items");
            foreach (var item in items.EnumerateArray())
            {
                if (MasterJson.Id(item, "id") is { } id)
                {
                    rows.Add(new ShellCatalogRow(id, MasterJson.Parse(catalog, null, null, item)));
                }
            }

            if (root.ValueKind == JsonValueKind.Array || !root.TryGetProperty("totalPages", out var totalPages) || page >= totalPages.GetInt32())
            {
                return rows;
            }

            page++;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokens.Invalidate();
        }

        return response;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"El Shell respondió {(int)response.StatusCode} a {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: {Clip(detail)}");
        }
    }

    private static string Clip(string text) => text.Length > 300 ? text[..300] : text;
}

/// <summary>
/// Writes to the master catalogs (<c>POST/PUT /api/catalogs/*</c>) with <c>X-ChronoLith-Source: brocal</c>, either
/// with the token of the current user (needs <c>shell.catalogs.manage</c>) or with the service account (outbox).
/// </summary>
internal sealed class ShellMasterCatalogWriter(
    HttpClient http,
    IHttpContextAccessor httpContext,
    ShellServiceAccountTokenProvider tokens,
    IOptions<ShellOptions> shell,
    IOptions<KeycloakOptions> keycloak,
    IOptions<Application.Features.Platform.TenancyOptions> tenancy,
    ILogger<ShellMasterCatalogWriter> logger) : IMasterCatalogWriter
{
    public const string SourceHeader = "X-ChronoLith-Source";

    public async Task<WriteBackResult> SendAsync(string method, string path, string payloadJson, bool asServiceAccount, CancellationToken cancellationToken)
    {
        if (!SharedCatalogAvailability.IsEnabled(shell.Value, keycloak.Value, PlatformModes.IsStandalone(tenancy.Value)))
        {
            return new WriteBackResult(WriteBackOutcome.Disabled);
        }

        AuthenticationHeaderValue? authorization;
        if (asServiceAccount)
        {
            try
            {
                authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                logger.LogWarning(ex, "No se obtuvo el token de la cuenta de servicio para escribir en el catálogo maestro");
                return new WriteBackResult(WriteBackOutcome.Unavailable, Detail: ex.Message);
            }
        }
        else
        {
            var header = httpContext.HttpContext?.Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(header) || !AuthenticationHeaderValue.TryParse(header, out authorization))
            {
                return new WriteBackResult(WriteBackOutcome.Forbidden, Detail: "sin token de usuario");
            }
        }

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = new StringContent(payloadJson, Encoding.UTF8, "application/json") };
            request.Headers.Authorization = authorization;
            request.Headers.TryAddWithoutValidation(SourceHeader, PlatformPermissions.ModuleCode);
            using var response = await http.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new WriteBackResult(WriteBackOutcome.Sent, method == "POST" ? ShellPayloads.CreatedId(content) : null);
            }

            if (asServiceAccount && response.StatusCode == HttpStatusCode.Unauthorized)
            {
                tokens.Invalidate();
            }

            var detail = $"{(int)response.StatusCode}: {(content.Length > 500 ? content[..500] : content)}";
            var outcome = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => WriteBackOutcome.Forbidden,
                HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity => WriteBackOutcome.Rejected,
                _ => WriteBackOutcome.Unavailable
            };
            logger.LogInformation("El catálogo maestro no aceptó {Method} {Path} ({Account}): {Detail}", method, path, asServiceAccount ? "cuenta de servicio" : "usuario", detail);
            return new WriteBackResult(outcome, Detail: detail);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "No se pudo escribir en el catálogo maestro ({Method} {Path})", method, path);
            return new WriteBackResult(WriteBackOutcome.Unavailable, Detail: ex.Message);
        }
    }
}
