using System.Globalization;
using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.Extensions.Logging;

namespace ChronoLith.Brocal.Application.Features.SharedCatalogs;

/// <summary>Outcome of one run of the feed consumer.</summary>
public sealed record CatalogFeedRunResult(int Pages, int Applied, int Errors, int Skipped, bool FullSync, long Cursor);

/// <summary>
/// Consumer of the change feed of the shared catalogs (<c>GET /api/catalogs/changes?consumer=brocal</c>), following
/// the rules of the module contract (section 5.1): pages of up to <see cref="PageSize"/> while <c>hasMore</c>, each
/// change applied in order and idempotently (upsert by id, <c>deleted</c> deactivates), replays applied even if they
/// are behind the cursor, own changes (<c>source: brocal</c>) refresh the replica without touching the local records,
/// a change that fails is reported and does not stop the page, the cursor of the response is adopted and saved only
/// after the page is applied, and <c>requiresFullSync</c> (or the first run) reloads every catalog completely.
/// </summary>
public sealed class CatalogFeedConsumer(
    ICatalogFeedClient feed,
    ICatalogReplica replica,
    ICatalogLinkReconciler links,
    TimeProvider clock,
    ILogger<CatalogFeedConsumer> logger)
{
    public const string ConsumerCode = PlatformPermissions.ModuleCode;

    public const int PageSize = 200;

    /// <summary>Pages read in one run before giving the turn back (a big backlog continues on the next run).</summary>
    public const int MaxPagesPerRun = 25;

    public bool Enabled => feed.IsConfigured;

    public async Task<CatalogFeedRunResult> RunAsync(bool fullReload, CancellationToken cancellationToken)
    {
        var state = await replica.GetStateAsync(cancellationToken);
        var pages = 0;
        var applied = 0;
        var errors = 0;
        var skipped = 0;
        var fullSync = false;
        var reportsOk = true;

        // First time (cursor 0 and nothing replicated) or asked by an administrator: full load, then follow the feed
        // from the latest change id the Shell reported before the load.
        if (fullReload || (state.Cursor == 0 && !await replica.AnyAsync(cancellationToken)))
        {
            var head = await feed.PullAsync(0, 1, cancellationToken);
            state.RecordPoll(head.Latest, clock.GetUtcNow());
            reportsOk &= await FullSyncAsync(state, head.Latest, cancellationToken);
            fullSync = true;
        }

        for (; pages < MaxPagesPerRun; pages++)
        {
            var page = await feed.PullAsync(state.Cursor, PageSize, cancellationToken);
            state.RecordPoll(page.Latest, clock.GetUtcNow());

            if (page.RequiresFullSync)
            {
                logger.LogWarning("El cursor {Cursor} quedó fuera de la ventana del feed de catálogos: recarga completa.", state.Cursor);
                reportsOk &= await FullSyncAsync(state, page.Cursor, cancellationToken);
                fullSync = true;
                continue;
            }

            var outcome = await ApplyPageAsync(page, cancellationToken);
            applied += outcome.Applied.Count - outcome.Skipped;
            errors += outcome.Errors.Count;
            skipped += outcome.Skipped;

            // The cursor is saved with the page it belongs to; the Shell learns it afterwards.
            state.AdoptCursor(page.Cursor);
            state.RecordRun(applied, errors);
            await replica.SaveChangesAsync(cancellationToken);
            reportsOk &= await ReportAsync(state, page.Cursor, outcome.Applied, outcome.Errors, cancellationToken);

            if (!page.HasMore)
            {
                pages++;
                break;
            }
        }

        if (errors == 0 && reportsOk)
        {
            state.ClearError();
            await replica.SaveChangesAsync(cancellationToken);
        }

        return new CatalogFeedRunResult(pages, applied, errors, skipped, fullSync, state.Cursor);
    }

    /// <summary>Applies one page: returns the ids applied (replays included) and the ones that failed with their message.</summary>
    internal async Task<PageOutcome> ApplyPageAsync(CatalogFeedPage page, CancellationToken cancellationToken)
    {
        var appliedIds = new List<long>();
        var errors = new List<CatalogFeedError>();
        var skipped = 0;
        var now = clock.GetUtcNow();

        // Replays go first in the response; the rest is already in id order.
        var ordered = page.Changes.OrderBy(c => c.Replay ? 0 : 1).ThenBy(c => c.Id).ToList();
        var existing = await replica.FindAsync(ordered.Select(c => c.EntityId).Distinct(), cancellationToken);

        foreach (var change in ordered)
        {
            try
            {
                var result = await ApplyChangeAsync(change, existing, now, cancellationToken);
                appliedIds.Add(change.Id);
                if (result == ChangeOutcome.Skipped)
                {
                    skipped++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "No se pudo aplicar el cambio {ChangeId} ({Catalog} {Code}) del feed de catálogos", change.Id, change.Catalog, change.Code);
                errors.Add(new CatalogFeedError(change.Id, Clip(ex.Message)));
            }
        }

        return new PageOutcome(appliedIds, errors, skipped);
    }

    private async Task<ChangeOutcome> ApplyChangeAsync(CatalogFeedChange change, Dictionary<Guid, SharedCatalogItem> existing, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!SharedCatalogNames.IsValid(change.Catalog))
        {
            throw new InvalidOperationException($"Catálogo desconocido: '{change.Catalog}'.");
        }

        var ownChange = string.Equals(change.Source, ConsumerCode, StringComparison.OrdinalIgnoreCase);
        var data = MasterJson.Parse(change.Catalog, change.Code, change.Name, change.Data);
        existing.TryGetValue(change.EntityId, out var item);

        if (string.Equals(change.Action, "deleted", StringComparison.OrdinalIgnoreCase))
        {
            if (item is null)
            {
                return ChangeOutcome.Skipped;
            }

            item.Deactivate(change.Id, now);
            await links.ReconcileAsync(item, ownChange, cancellationToken);
            return ChangeOutcome.Applied;
        }

        // Idempotent and safe after a full load: an old change (not a replay) never overwrites a newer snapshot.
        if (item is not null && !change.Replay && item.UpdatedAt is { } current && data.UpdatedAt is { } incoming && incoming < current)
        {
            return ChangeOutcome.Skipped;
        }

        if (item is null)
        {
            item = SharedCatalogItem.Create(change.EntityId, change.Catalog, data, change.Id, now);
            replica.Add(item);
            existing[change.EntityId] = item;
        }
        else
        {
            item.Apply(data, change.Id, now);
        }

        await links.ReconcileAsync(item, ownChange, cancellationToken);
        return ownChange ? ChangeOutcome.Skipped : ChangeOutcome.Applied;
    }

    /// <summary>
    /// Loads every catalog from <c>GET /api/catalogs/{catalog}</c>: upserts each row and deactivates the elements that
    /// disappeared from the master. The cursor jumps to <paramref name="cursor"/> (the latest id the Shell reported).
    /// </summary>
    private async Task<bool> FullSyncAsync(CatalogFeedState state, long cursor, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var catalog in SharedCatalogNames.All)
        {
            var rows = await feed.ReadAllAsync(catalog, cancellationToken);
            if (rows is null)
            {
                continue;
            }

            var current = (await replica.AllAsync(catalog, cancellationToken)).ToDictionary(i => i.Id);
            foreach (var row in rows)
            {
                if (current.TryGetValue(row.Id, out var item))
                {
                    item.Apply(row.Data, null, now);
                    current.Remove(row.Id);
                }
                else
                {
                    item = SharedCatalogItem.Create(row.Id, catalog, row.Data, null, now);
                    replica.Add(item);
                }

                await links.ReconcileAsync(item, ownChange: false, cancellationToken);
            }

            foreach (var missing in current.Values.Where(i => i.IsActive))
            {
                missing.Deactivate(null, now);
                await links.ReconcileAsync(missing, ownChange: false, cancellationToken);
            }

            logger.LogInformation("Catálogo compartido {Catalog}: {Count} elementos cargados del Shell.", catalog, rows.Count);
        }

        state.RecordFullSync(cursor, clock.GetUtcNow());
        await replica.SaveChangesAsync(cancellationToken);
        return await ReportAsync(state, cursor, [], [], cancellationToken);
    }

    /// <summary>A report that fails does not stop the feed: the next page reports the cursor again. Returns whether the Shell took it.</summary>
    private async Task<bool> ReportAsync(CatalogFeedState state, long cursor, IReadOnlyList<long> applied, IReadOnlyList<CatalogFeedError> errors, CancellationToken cancellationToken)
    {
        var ok = true;
        try
        {
            await feed.ReportAsync(cursor, applied, errors, cancellationToken);
            state.RecordReport(clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo reportar el cursor {Cursor} del feed de catálogos al Shell", cursor);
            state.RecordError($"Reporte al Shell: {ex.Message}", clock.GetUtcNow());
            ok = false;
        }

        await replica.SaveChangesAsync(cancellationToken);
        return ok;
    }

    private static string Clip(string text) => text.Length > 1000 ? text[..1000] : text;

    internal enum ChangeOutcome
    {
        Applied,
        Skipped
    }

    internal sealed record PageOutcome(IReadOnlyList<long> Applied, IReadOnlyList<CatalogFeedError> Errors, int Skipped);
}

/// <summary>Reads the master element out of the JSON the Shell returns (catalog DTOs), tolerant to missing fields.</summary>
public static class MasterJson
{
    public static SharedCatalogData Parse(string catalog, string? code, string? name, JsonElement data)
    {
        var p = data.ValueKind == JsonValueKind.Object ? data : default;
        var parentProperty = SharedCatalogNames.ParentProperty(catalog);

        return new SharedCatalogData(
            Str(p, "code") ?? code ?? string.Empty,
            Str(p, "name") ?? name ?? code ?? string.Empty,
            parentProperty is null ? null : Id(p, parentProperty),
            Bool(p, "isActive") ?? true,
            Date(p, "updatedAt"),
            p.ValueKind == JsonValueKind.Object ? p.GetRawText() : "{}");
    }

    public static string? Str(JsonElement p, string property) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static Guid? Id(JsonElement p, string property) => Guid.TryParse(Str(p, property), out var g) ? g : null;

    public static decimal? Dec(JsonElement p, string property) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;

    public static int? Int(JsonElement p, string property) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    public static bool? Bool(JsonElement p, string property) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(property, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    public static DateTimeOffset? Date(JsonElement p, string property) =>
        DateTimeOffset.TryParse(Str(p, property), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    /// <summary>Snapshot stored in the replica, parsed again.</summary>
    public static JsonElement Snapshot(SharedCatalogItem item)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(item.Snapshot) ? "{}" : item.Snapshot);
        return document.RootElement.Clone();
    }
}
