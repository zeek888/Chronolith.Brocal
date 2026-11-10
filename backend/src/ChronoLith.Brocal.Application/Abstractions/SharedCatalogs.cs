using System.Text.Json;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Application.Abstractions;

// ---- Replica (read side) ---------------------------------------------------------------------------

public sealed record ShellLookupItem(Guid Id, string Code, string Name, Guid? ParentId, bool IsActive);

/// <summary>
/// Shared catalogs of the ChronoLith Shell (wells, fields, facilities, companies…) read from the local replica that
/// the change feed keeps up to date. Empty (not available) when Brocal is not connected to a Shell.
/// </summary>
public interface ISharedCatalogs
{
    /// <summary>Brocal is connected to a Shell with a service account: the replica, the feed and the push to the master work.</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<ShellLookupItem>> LookupAsync(string catalog, bool activeOnly, Guid? parentId, CancellationToken cancellationToken);

    Task<SharedCatalogItem?> FindAsync(string catalog, Guid id, CancellationToken cancellationToken);

    /// <summary>Drops the cached lookups of a catalog after the replica changed.</summary>
    void Changed(string catalog);
}

// ---- Change feed (consumer side) -------------------------------------------------------------------

/// <summary>One change of the feed (<c>GET /api/catalogs/changes</c>); <see cref="Data"/> has the shape of the Shell DTO.</summary>
public sealed record CatalogFeedChange(
    long Id,
    string Catalog,
    Guid EntityId,
    string Code,
    string Name,
    string Action,
    DateTimeOffset OccurredAt,
    string? Actor,
    string? Source,
    JsonElement Data,
    bool Replay);

public sealed record CatalogFeedPage(long Cursor, long Latest, bool HasMore, bool RequiresFullSync, IReadOnlyList<CatalogFeedChange> Changes);

public sealed record CatalogFeedError(long ChangeId, string Message);

/// <summary>Row of <c>GET /api/catalogs/{catalog}</c> (full load).</summary>
public sealed record ShellCatalogRow(Guid Id, SharedCatalogData Data);

/// <summary>Calls to the Shell made with the service account of Brocal (feed, report and full load).</summary>
public interface ICatalogFeedClient
{
    /// <summary><c>Shell:ApiUrl</c> and the service account of the realm <c>chronolith</c> are configured.</summary>
    bool IsConfigured { get; }

    Task<CatalogFeedPage> PullAsync(long since, int limit, CancellationToken cancellationToken);

    Task ReportAsync(long cursor, IReadOnlyList<long> applied, IReadOnlyList<CatalogFeedError> errors, CancellationToken cancellationToken);

    /// <summary>Every element of a catalog, page by page; null when the Shell does not have that catalog.</summary>
    Task<IReadOnlyList<ShellCatalogRow>?> ReadAllAsync(string catalog, CancellationToken cancellationToken);
}

/// <summary>Storage of the replica and the feed state (a unit of work of its own, outside the tenant filter).</summary>
public interface ICatalogReplica
{
    /// <summary>The single feed state row, created on first use.</summary>
    Task<CatalogFeedState> GetStateAsync(CancellationToken cancellationToken);

    Task<bool> AnyAsync(CancellationToken cancellationToken);

    Task<Dictionary<Guid, SharedCatalogItem>> FindAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<SharedCatalogItem>> AllAsync(string catalog, CancellationToken cancellationToken);

    void Add(SharedCatalogItem item);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Propagates a master element applied to the replica to the local records linked to it (sites, customers).</summary>
public interface ICatalogLinkReconciler
{
    /// <summary>
    /// Called after each element is upserted in the replica. <paramref name="ownChange"/> tells the change came from
    /// Brocal itself (<c>X-ChronoLith-Source: brocal</c>): the replica is refreshed but local records are left alone.
    /// </summary>
    Task ReconcileAsync(SharedCatalogItem item, bool ownChange, CancellationToken cancellationToken);
}

/// <summary>Runs the catalog synchronization (push of the outbox, feed pull) on demand; serialized across instances.</summary>
public interface ICatalogSyncRunner
{
    bool BackgroundEnabled { get; }

    int IntervalMinutes { get; }

    /// <summary>Null when another run is in progress or the integration is not configured.</summary>
    Task<CatalogSyncRunSummary?> RunAsync(bool fullReload, CancellationToken cancellationToken);
}

public sealed record CatalogSyncRunSummary(int Pages, int Applied, int Errors, int Pushed, bool FullSync, long Cursor);

// ---- Master catalog (write side) -------------------------------------------------------------------

public enum WriteBackOutcome
{
    /// <summary>Not connected to a Shell.</summary>
    Disabled,

    Sent,

    /// <summary>The token has no <c>shell.catalogs.manage</c> (or there is no user token): queue it for the service account.</summary>
    Forbidden,

    /// <summary>The Shell did not answer: queue it.</summary>
    Unavailable,

    /// <summary>The master refused the data (400, 404, 409, 422).</summary>
    Rejected
}

public sealed record WriteBackResult(WriteBackOutcome Outcome, Guid? CreatedId = null, string? Detail = null);

/// <summary>Writes to the master catalogs of the Shell (<c>POST/PUT /api/catalogs/*</c>) with <c>X-ChronoLith-Source: brocal</c>.</summary>
public interface IMasterCatalogWriter
{
    /// <summary>With the token of the current user (immediate push) or with the service account (outbox).</summary>
    Task<WriteBackResult> SendAsync(string method, string path, string payloadJson, bool asServiceAccount, CancellationToken cancellationToken);
}
