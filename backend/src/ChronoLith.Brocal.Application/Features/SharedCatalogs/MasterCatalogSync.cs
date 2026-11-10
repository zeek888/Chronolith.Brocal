using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChronoLith.Brocal.Application.Features.SharedCatalogs;

public enum CatalogPushOutcome
{
    /// <summary>Not connected, not linked, or a record the master does not model (yards, fields as sites).</summary>
    Skipped,

    /// <summary>The master has the change.</summary>
    Sent,

    /// <summary>Queued for the service account (the user cannot write the catalog or the Shell did not answer).</summary>
    Queued,

    /// <summary>The master rejected the data; the record stays local until it is fixed.</summary>
    Rejected
}

public sealed record CatalogPushResult(CatalogPushOutcome Outcome, Guid? ShellId, string? Detail);

/// <summary>
/// Keeps the sites and customers linked to the master catalog of ChronoLith equal in both directions: a local change
/// is pushed at once with the user's token (or queued for the service account), and the feed consumer applies what
/// changed in the master through <see cref="LinkedRecordsReconciler"/>, the side that changed later winning.
/// Every write carries <c>X-ChronoLith-Source: brocal</c>, so the change comes back marked as our own.
/// </summary>
public sealed class MasterCatalogSync(
    IApplicationDbContext db,
    ISharedCatalogs catalogs,
    IMasterCatalogWriter writer,
    ITenantContext tenantContext,
    TimeProvider clock,
    ILogger<MasterCatalogSync> logger)
{
    public bool Enabled => catalogs.IsAvailable;

    /// <summary>Pushes a site after it was saved. With <paramref name="allowCreate"/> an unlinked site is created in the master (or linked by code).</summary>
    public async Task<Result<CatalogPushResult>> PushSiteAsync(Site site, bool allowCreate, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return new CatalogPushResult(CatalogPushOutcome.Skipped, null, null);
        }

        var catalog = site.ShellCatalog;
        if (catalog is null)
        {
            return allowCreate ? SharedCatalogErrors.SiteTypeNotShared : new CatalogPushResult(CatalogPushOutcome.Skipped, null, null);
        }

        if (site.ShellCatalogId is null)
        {
            if (!allowCreate)
            {
                return new CatalogPushResult(CatalogPushOutcome.Skipped, null, null);
            }

            // The same code already exists in the master: link instead of creating a duplicate.
            var existing = (await catalogs.LookupAsync(catalog, false, null, cancellationToken)).FirstOrDefault(i => string.Equals(i.Code, site.Code, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                site.LinkToCatalog(existing.Id, existing.ParentId, await FieldNameAsync(existing.ParentId, cancellationToken));
            }
            else if (catalog == SharedCatalogNames.Wells && site.ShellFieldId is null)
            {
                return SharedCatalogErrors.FieldRequired;
            }
        }

        var master = site.ShellCatalogId is { } id ? await catalogs.FindAsync(catalog, id, cancellationToken) : null;
        Guid? operatorId = null;
        if (site.CustomerId is { } customerId)
        {
            operatorId = await db.Customers.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == customerId).Select(c => c.ShellCompanyId).FirstOrDefaultAsync(cancellationToken);
        }

        var payload = ShellPayloads.ForSite(catalog, site, master, operatorId);
        return await SendAsync("Site", site.Id, site.TenantId, catalog, site.ShellCatalogId, payload, $"{site.Code} · {site.Name}", cancellationToken,
            created => site.LinkToCatalog(created, site.ShellFieldId, null));
    }

    /// <summary>Pushes a customer linked to a company of the master (name, code, type and RFC).</summary>
    public async Task<Result<CatalogPushResult>> PushCustomerAsync(Customer customer, bool allowCreate, CancellationToken cancellationToken)
    {
        if (!Enabled || customer.ShellCompanyId is null && !allowCreate)
        {
            return new CatalogPushResult(CatalogPushOutcome.Skipped, null, null);
        }

        if (customer.ShellCompanyId is null)
        {
            var existing = (await catalogs.LookupAsync(SharedCatalogNames.Companies, false, null, cancellationToken)).FirstOrDefault(i => string.Equals(i.Code, customer.Code, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                customer.LinkToCompany(existing.Id);
            }
        }

        var master = customer.ShellCompanyId is { } id ? await catalogs.FindAsync(SharedCatalogNames.Companies, id, cancellationToken) : null;
        var payload = ShellPayloads.ForCustomer(customer, master);
        return await SendAsync("Customer", customer.Id, customer.TenantId, SharedCatalogNames.Companies, customer.ShellCompanyId, payload, $"{customer.Code} · {customer.Name}", cancellationToken,
            customer.LinkToCompany);
    }

    private async Task<Result<CatalogPushResult>> SendAsync(
        string entityType,
        Guid entityId,
        Guid tenantId,
        string catalog,
        Guid? shellId,
        string payload,
        string summary,
        CancellationToken cancellationToken,
        Action<Guid> link)
    {
        var method = shellId is null ? "POST" : "PUT";
        var path = ShellPayloads.PathOf(catalog, shellId);
        var result = await writer.SendAsync(method, path, payload, asServiceAccount: false, cancellationToken);
        var now = clock.GetUtcNow();

        switch (result.Outcome)
        {
            case WriteBackOutcome.Disabled:
                return new CatalogPushResult(CatalogPushOutcome.Skipped, shellId, null);

            case WriteBackOutcome.Sent:
                if (shellId is null && result.CreatedId is { } created)
                {
                    link(created);
                    shellId = created;
                }

                await DiscardOpenAsync(entityId, "enviado directamente al maestro", now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return new CatalogPushResult(CatalogPushOutcome.Sent, shellId, null);

            case WriteBackOutcome.Rejected:
                logger.LogWarning("El catálogo maestro rechazó {Entity} {Summary}: {Detail}", entityType, summary, result.Detail);
                await DiscardOpenAsync(entityId, "sustituido por un envío más reciente", now, cancellationToken);
                var rejected = new CatalogPushMessage(tenantId, entityType, entityId, catalog, method, shellId, payload, summary, now);
                rejected.MarkRejected(now, result.Detail ?? "rechazado");
                db.CatalogPushMessages.Add(rejected);
                await db.SaveChangesAsync(cancellationToken);
                return new CatalogPushResult(CatalogPushOutcome.Rejected, shellId, result.Detail);

            default:
                await DiscardOpenAsync(entityId, "sustituido por un envío más reciente", now, cancellationToken);
                db.CatalogPushMessages.Add(new CatalogPushMessage(tenantId, entityType, entityId, catalog, method, shellId, payload, summary, now));
                await db.SaveChangesAsync(cancellationToken);
                return new CatalogPushResult(CatalogPushOutcome.Queued, shellId, result.Detail);
        }
    }

    /// <summary>
    /// Sends the queued writes with the service account (needs a Shell role with <c>shell.catalogs.manage</c>).
    /// Returns how many reached the master.
    /// </summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var pending = await db.CatalogPushMessages
            .Where(m => m.Status == CatalogPushStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var message in pending)
        {
            var result = await writer.SendAsync(message.Method, ShellPayloads.PathOf(message.Catalog, message.ShellId), message.Payload, asServiceAccount: true, cancellationToken);
            now = clock.GetUtcNow();
            switch (result.Outcome)
            {
                case WriteBackOutcome.Sent:
                    message.MarkSent(now, result.CreatedId);
                    if (message.Method == "POST" && result.CreatedId is { } created)
                    {
                        await LinkCreatedAsync(message, created, cancellationToken);
                    }

                    sent++;
                    break;

                case WriteBackOutcome.Rejected:
                    message.MarkRejected(now, result.Detail ?? "rechazado");
                    break;

                case WriteBackOutcome.Disabled:
                    message.Discard(now, "integración desactivada");
                    break;

                default:
                    message.MarkFailed(now, result.Detail ?? "sin respuesta");
                    break;
            }
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    private async Task LinkCreatedAsync(CatalogPushMessage message, Guid created, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == message.TenantId, cancellationToken);
        if (tenant is null)
        {
            return;
        }

        using var scope = tenantContext.BeginScope(TenantSnapshot.From(tenant));
        if (message.EntityType == "Site")
        {
            var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == message.EntityId, cancellationToken);
            site?.LinkToCatalog(created, null, null);
        }
        else
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == message.EntityId, cancellationToken);
            customer?.LinkToCompany(created);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task DiscardOpenAsync(Guid entityId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var open in await db.CatalogPushMessages.Where(m => m.EntityId == entityId && m.Status == CatalogPushStatus.Pending).ToListAsync(cancellationToken))
        {
            open.Discard(now, reason);
        }
    }

    private async Task<string?> FieldNameAsync(Guid? fieldId, CancellationToken cancellationToken) =>
        fieldId is { } id ? (await catalogs.FindAsync(SharedCatalogNames.Fields, id, cancellationToken))?.Name : null;
}

public static class SharedCatalogErrors
{
    public static readonly Error NotAvailable = Error.Failure("SharedCatalogs.NotAvailable", "Brocal no está conectado a un Shell de ChronoLith.");

    public static readonly Error SiteTypeNotShared = Error.Validation("SharedCatalogs.SiteTypeNotShared", "Solo los pozos y las instalaciones existen en el catálogo maestro de ChronoLith.");

    public static readonly Error FieldRequired = Error.Validation("SharedCatalogs.FieldRequired", "El catálogo maestro requiere el campo del pozo: elige un campo de ChronoLith.");

    public static readonly Error UnknownCatalog = Error.Validation("SharedCatalogs.UnknownCatalog", "Catálogo compartido desconocido.");

    public static readonly Error PushNotFound = Error.NotFound("SharedCatalogs.PushNotFound", "El envío no existe.");

    public static readonly Error SyncRunning = Error.Conflict("SharedCatalogs.SyncRunning", "Ya hay una sincronización en curso; intenta en unos minutos.");
}

/// <summary>Bodies for the Shell inputs (<c>WellInput</c>, <c>FacilityInput</c>, <c>CompanyInput</c>), merged with the master snapshot.</summary>
public static class ShellPayloads
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string PathOf(string catalog, Guid? shellId) => shellId is null ? $"api/catalogs/{catalog}" : $"api/catalogs/{catalog}/{shellId}";

    /// <summary>
    /// Shared fields come from the site (code, name, coordinates, field); everything the master knows and Brocal does
    /// not (type, trajectory, status, rig, dates, depths, notes) is kept from the snapshot.
    /// </summary>
    public static string ForSite(string catalog, Site site, SharedCatalogItem? master, Guid? operatorId)
    {
        var m = master is null ? default : MasterJson.Snapshot(master);
        var body = new Dictionary<string, object?>
        {
            ["code"] = site.Code,
            ["name"] = site.Name,
            ["fieldId"] = site.ShellFieldId ?? MasterJson.Id(m, "fieldId"),
            ["latitude"] = site.Latitude,
            ["longitude"] = site.Longitude,
            ["notes"] = MasterJson.Str(m, "notes")
        };

        if (catalog == SharedCatalogNames.Wells)
        {
            body["facilityId"] = MasterJson.Id(m, "facilityId");
            body["type"] = MasterJson.Str(m, "type") ?? "development";
            body["trajectory"] = MasterJson.Str(m, "trajectory") ?? "vertical";
            body["status"] = MasterJson.Str(m, "status") ?? "producing";
            body["operatorId"] = MasterJson.Id(m, "operatorId") ?? operatorId;
            body["rigId"] = MasterJson.Id(m, "rigId");
            body["spudDate"] = MasterJson.Str(m, "spudDate");
            body["completionDate"] = MasterJson.Str(m, "completionDate");
            body["plannedDepthMeters"] = MasterJson.Int(m, "plannedDepthMeters");
            body["actualDepthMeters"] = MasterJson.Int(m, "actualDepthMeters");
        }
        else
        {
            body["type"] = MasterJson.Str(m, "type") ?? "other";
        }

        return JsonSerializer.Serialize(body, Json);
    }

    /// <summary>Code, name and RFC from the customer; the type (operator by default), country and notes from the master.</summary>
    public static string ForCustomer(Customer customer, SharedCatalogItem? master)
    {
        var m = master is null ? default : MasterJson.Snapshot(master);
        var genericRfc = customer.Rfc is MexicanIds.GenericRfc or MexicanIds.ForeignRfc;
        var body = new Dictionary<string, object?>
        {
            ["code"] = customer.Code,
            ["name"] = customer.Name,
            ["type"] = MasterJson.Str(m, "type") ?? "operator",
            ["taxId"] = genericRfc ? MasterJson.Str(m, "taxId") : customer.Rfc,
            ["country"] = MasterJson.Str(m, "country") ?? (customer.Rfc == MexicanIds.ForeignRfc ? null : "MX"),
            ["notes"] = MasterJson.Str(m, "notes")
        };

        return JsonSerializer.Serialize(body, Json);
    }

    /// <summary>Id of the element the master created (<c>201 { id }</c>).</summary>
    public static Guid? CreatedId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var guid) ? guid : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Applies a master element to the sites and customers linked to it (every company): when the master changed later
/// than the local record its shared fields win and pending pushes of that record are discarded; when the local record
/// is newer, a push is queued for the service account. Elements the master deactivated deactivate their sites.
/// </summary>
internal sealed class LinkedRecordsReconciler(IApplicationDbContext db, ISharedCatalogs catalogs, ITenantContext tenantContext, TimeProvider clock, ILogger<LinkedRecordsReconciler> logger)
    : ICatalogLinkReconciler
{
    public async Task ReconcileAsync(SharedCatalogItem item, bool ownChange, CancellationToken cancellationToken)
    {
        if (ownChange)
        {
            return;
        }

        switch (item.Catalog)
        {
            case SharedCatalogNames.Wells:
            case SharedCatalogNames.Facilities:
                await ReconcileSitesAsync(item, cancellationToken);
                break;

            case SharedCatalogNames.Companies:
                await ReconcileCustomersAsync(item, cancellationToken);
                break;
        }
    }

    private async Task ReconcileSitesAsync(SharedCatalogItem item, CancellationToken cancellationToken)
    {
        var sites = await db.Sites.IgnoreQueryFilters().Where(s => s.ShellCatalogId == item.Id).ToListAsync(cancellationToken);
        if (sites.Count == 0)
        {
            return;
        }

        var snapshot = MasterJson.Snapshot(item);
        var fieldId = MasterJson.Id(snapshot, "fieldId") ?? item.ParentId;
        var fieldName = MasterJson.Str(snapshot, "fieldName") ?? (fieldId is { } f ? (await catalogs.FindAsync(SharedCatalogNames.Fields, f, cancellationToken))?.Name : null);
        var latitude = MasterJson.Dec(snapshot, "latitude");
        var longitude = MasterJson.Dec(snapshot, "longitude");
        var now = clock.GetUtcNow();

        foreach (var group in sites.GroupBy(s => s.TenantId))
        {
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == group.Key, cancellationToken);
            if (tenant is null)
            {
                continue;
            }

            using var scope = tenantContext.BeginScope(TenantSnapshot.From(tenant));
            foreach (var site in group)
            {
                if (!item.IsActive)
                {
                    if (site.IsActive)
                    {
                        site.Deactivate();
                        logger.LogInformation("Locación {Code} desactivada: su elemento ya no está activo en el catálogo maestro.", site.Code);
                    }

                    continue;
                }

                var same = string.Equals(site.Code, item.Code, StringComparison.Ordinal) && site.Name == item.Name && site.Latitude == latitude && site.Longitude == longitude
                    && (fieldId is null || site.ShellFieldId == fieldId);
                if (same)
                {
                    continue;
                }

                var localUpdated = site.UpdatedAt ?? site.CreatedAt;
                if (item.UpdatedAt is null || item.UpdatedAt >= localUpdated)
                {
                    var codeTaken = await db.Sites.AnyAsync(s => s.Code == item.Code && s.Id != site.Id, cancellationToken);
                    var result = site.ApplyMaster(codeTaken ? null : item.Code, item.Name, latitude, longitude, fieldId, fieldName);
                    if (result.IsFailure)
                    {
                        logger.LogWarning("No se aplicó el catálogo maestro a la locación {Code}: {Error}", site.Code, result.Error.Description);
                        continue;
                    }

                    await DiscardPendingAsync(site.Id, now, cancellationToken);
                }
                else
                {
                    await QueueIfIdleAsync(site.TenantId, "Site", site.Id, item.Catalog, item.Id, ShellPayloads.ForSite(item.Catalog, site, item, null), $"{site.Code} · {site.Name}", now, cancellationToken);
                }
            }
        }
    }

    private async Task ReconcileCustomersAsync(SharedCatalogItem item, CancellationToken cancellationToken)
    {
        var customers = await db.Customers.IgnoreQueryFilters().Where(c => c.ShellCompanyId == item.Id).ToListAsync(cancellationToken);
        if (customers.Count == 0 || !item.IsActive)
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var group in customers.GroupBy(c => c.TenantId))
        {
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == group.Key, cancellationToken);
            if (tenant is null)
            {
                continue;
            }

            using var scope = tenantContext.BeginScope(TenantSnapshot.From(tenant));
            foreach (var customer in group.Where(c => c.Name != item.Name))
            {
                var localUpdated = customer.UpdatedAt ?? customer.CreatedAt;
                if (item.UpdatedAt is null || item.UpdatedAt >= localUpdated)
                {
                    if (customer.ApplyMaster(item.Name).IsSuccess)
                    {
                        await DiscardPendingAsync(customer.Id, now, cancellationToken);
                    }
                }
                else
                {
                    await QueueIfIdleAsync(customer.TenantId, "Customer", customer.Id, item.Catalog, item.Id, ShellPayloads.ForCustomer(customer, item), $"{customer.Code} · {customer.Name}", now, cancellationToken);
                }
            }
        }
    }

    private async Task DiscardPendingAsync(Guid entityId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var pending in await db.CatalogPushMessages.Where(m => m.EntityId == entityId && m.Status == CatalogPushStatus.Pending).ToListAsync(cancellationToken))
        {
            pending.Discard(now, "sustituido por el catálogo maestro");
        }
    }

    private async Task QueueIfIdleAsync(Guid tenantId, string entityType, Guid entityId, string catalog, Guid shellId, string payload, string summary, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.CatalogPushMessages.AnyAsync(m => m.EntityId == entityId && m.Status == CatalogPushStatus.Pending, cancellationToken))
        {
            return;
        }

        db.CatalogPushMessages.Add(new CatalogPushMessage(tenantId, entityType, entityId, catalog, "PUT", shellId, payload, summary, now));
    }
}
