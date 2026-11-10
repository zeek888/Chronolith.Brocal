using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Platform;

/// <summary>Catalogs of the ChronoLith Shell that Brocal replicates (same codes as the change feed).</summary>
public static class SharedCatalogNames
{
    public const string Basins = "basins";
    public const string Assets = "assets";
    public const string Companies = "companies";
    public const string Fields = "fields";
    public const string Facilities = "facilities";
    public const string Rigs = "rigs";
    public const string Wells = "wells";

    /// <summary>Parents first, so a full load resolves <c>parentId</c> in one pass.</summary>
    public static readonly IReadOnlyList<string> All = [Basins, Assets, Companies, Fields, Facilities, Rigs, Wells];

    public static bool IsValid(string? catalog) => catalog is not null && All.Contains(catalog);

    /// <summary>Property of the Shell DTO that is the parent shown in lookups (field of a well, contractor of a rig).</summary>
    public static string? ParentProperty(string catalog) => catalog switch
    {
        Facilities or Wells => "fieldId",
        Rigs => "contractorId",
        Fields => "assetId",
        _ => null
    };
}

/// <summary>
/// Local copy of one element of a shared catalog of the Shell: the id of the Shell is the id of the row, plus code,
/// name, parent, active flag, the master's <c>updatedAt</c> and the JSON snapshot the feed delivered (same shape as
/// the Shell DTO). Installation-wide: every company reads the same replica, outside the tenant filter.
/// </summary>
public sealed class SharedCatalogItem : Entity
{
    public const int MaxCodeLength = 80;
    public const int MaxNameLength = 300;

    private SharedCatalogItem()
    {
    }

    private SharedCatalogItem(Guid id, string catalog)
        : base(id) => Catalog = catalog;

    public string Catalog { get; private set; } = null!;

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>Field of a well or facility, asset of a field, contractor of a rig.</summary>
    public Guid? ParentId { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>When the master changed it last (<c>updatedAt</c> of the snapshot); decides conflicts with local records.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>JSON of the Shell DTO (<c>WellDto</c>, <c>FacilityDto</c>…) as delivered by the feed or the full load.</summary>
    public string Snapshot { get; private set; } = "{}";

    /// <summary>Feed change that produced the current state (null after a full load).</summary>
    public long? LastChangeId { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    public static SharedCatalogItem Create(Guid id, string catalog, SharedCatalogData data, long? changeId, DateTimeOffset now)
    {
        var item = new SharedCatalogItem(id, catalog);
        item.Apply(data, changeId, now);
        return item;
    }

    /// <summary>Upsert of the master state (feed change or full load row).</summary>
    public void Apply(SharedCatalogData data, long? changeId, DateTimeOffset now)
    {
        Code = Trim(data.Code, MaxCodeLength);
        Name = Trim(string.IsNullOrWhiteSpace(data.Name) ? data.Code : data.Name, MaxNameLength);
        ParentId = data.ParentId;
        IsActive = data.IsActive;
        UpdatedAt = data.UpdatedAt;
        Snapshot = string.IsNullOrWhiteSpace(data.Snapshot) ? "{}" : data.Snapshot;
        LastChangeId = changeId ?? LastChangeId;
        SyncedAt = now;
    }

    /// <summary>A <c>deleted</c> change (or an element that disappeared from the master): elements with history are kept, inactive.</summary>
    public void Deactivate(long? changeId, DateTimeOffset now)
    {
        IsActive = false;
        LastChangeId = changeId ?? LastChangeId;
        SyncedAt = now;
    }

    private static string Trim(string value, int max)
    {
        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }
}

/// <summary>What the master says about an element (parsed from the feed <c>data</c> or a catalog row).</summary>
public sealed record SharedCatalogData(string Code, string Name, Guid? ParentId, bool IsActive, DateTimeOffset? UpdatedAt, string Snapshot);

/// <summary>
/// Where Brocal is in the change feed of the Shell (single global row): the cursor adopted after the last page
/// applied, when it polled and reported, the last full load and the last error. Saved only after a page is applied.
/// </summary>
public sealed class CatalogFeedState : Entity
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000002");

    public const int MaxErrorLength = 1000;

    private CatalogFeedState()
        : base(SingletonId)
    {
    }

    public static CatalogFeedState Create() => new();

    /// <summary>Id of the last change applied; sent as <c>since</c> on the next poll (0 = never synced).</summary>
    public long Cursor { get; private set; }

    /// <summary>Latest change id the Shell reported on the last poll (lag = Latest - Cursor).</summary>
    public long Latest { get; private set; }

    public DateTimeOffset? LastPolledAt { get; private set; }

    public DateTimeOffset? LastReportedAt { get; private set; }

    public DateTimeOffset? LastFullSyncAt { get; private set; }

    /// <summary>Changes applied on the last run and changes that failed (reported to the Shell as errors).</summary>
    public int LastRunApplied { get; private set; }

    public int LastRunErrors { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? LastErrorAt { get; private set; }

    public void RecordPoll(long latest, DateTimeOffset now)
    {
        Latest = latest;
        LastPolledAt = now;
    }

    /// <summary>Adopts the cursor of the response once the page is applied (an administrator may have rewound it).</summary>
    public void AdoptCursor(long cursor) => Cursor = cursor;

    public void RecordRun(int applied, int errors)
    {
        LastRunApplied = applied;
        LastRunErrors = errors;
    }

    public void RecordReport(DateTimeOffset now) => LastReportedAt = now;

    public void RecordFullSync(long cursor, DateTimeOffset now)
    {
        Cursor = cursor;
        LastFullSyncAt = now;
    }

    public void RecordError(string error, DateTimeOffset now)
    {
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        LastErrorAt = now;
    }

    public void ClearError()
    {
        LastError = null;
        LastErrorAt = null;
    }
}

public enum CatalogPushStatus
{
    Pending = 0,
    Sent = 1,
    Rejected = 2,
    Failed = 3,
    Discarded = 4
}

/// <summary>
/// Write to the master catalog of the Shell that could not be sent with the user's token (no
/// <c>shell.catalogs.manage</c> or the Shell did not answer): the service account sends it later with retries.
/// Also keeps the rejections of the master so the administrator can see them. Not tenant-filtered: the company is
/// recorded to open the record that was pushed.
/// </summary>
public sealed class CatalogPushMessage : Entity
{
    public const int MaxAttempts = 8;

    private CatalogPushMessage()
    {
    }

    public CatalogPushMessage(
        Guid tenantId,
        string entityType,
        Guid entityId,
        string catalog,
        string method,
        Guid? shellId,
        string payload,
        string summary,
        DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        EntityType = entityType;
        EntityId = entityId;
        Catalog = catalog;
        Method = method;
        ShellId = shellId;
        Payload = payload;
        Summary = summary.Length > 300 ? summary[..300] : summary;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
    }

    /// <summary>Company that owns the local record (not a tenant filter: platform administrators see every push).</summary>
    public Guid TenantId { get; private set; }

    /// <summary><c>Site</c> or <c>Customer</c>.</summary>
    public string EntityType { get; private set; } = null!;

    public Guid EntityId { get; private set; }

    /// <summary><c>wells</c>, <c>facilities</c> or <c>companies</c>.</summary>
    public string Catalog { get; private set; } = null!;

    /// <summary><c>POST</c> creates the element in the master; <c>PUT</c> updates <see cref="ShellId"/>.</summary>
    public string Method { get; private set; } = null!;

    public Guid? ShellId { get; private set; }

    /// <summary>JSON body of the Shell input (<c>WellInput</c>, <c>FacilityInput</c>, <c>CompanyInput</c>).</summary>
    public string Payload { get; private set; } = null!;

    /// <summary>Code and name of the record, for the administration screen.</summary>
    public string Summary { get; private set; } = null!;

    public CatalogPushStatus Status { get; private set; } = CatalogPushStatus.Pending;

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string? LastError { get; private set; }

    public bool IsOpen => Status is CatalogPushStatus.Pending or CatalogPushStatus.Failed or CatalogPushStatus.Rejected;

    public void MarkSent(DateTimeOffset now, Guid? createdId)
    {
        Status = CatalogPushStatus.Sent;
        SentAt = now;
        Attempts++;
        LastError = null;
        ShellId ??= createdId;
    }

    /// <summary>The master did not answer or refused the token: exponential back-off (1, 2, 4… minutes) until <see cref="MaxAttempts"/>.</summary>
    public void MarkFailed(DateTimeOffset now, string error)
    {
        Attempts++;
        LastError = Clip(error);
        if (Attempts >= MaxAttempts)
        {
            Status = CatalogPushStatus.Failed;
            return;
        }

        NextAttemptAt = now.AddMinutes(Math.Pow(2, Attempts - 1));
    }

    /// <summary>The master rejected the data (400, 404, 409): nothing to retry until the record is fixed.</summary>
    public void MarkRejected(DateTimeOffset now, string error)
    {
        Attempts++;
        Status = CatalogPushStatus.Rejected;
        LastError = Clip(error);
        SentAt = null;
        NextAttemptAt = now;
    }

    public void Retry(DateTimeOffset now)
    {
        Status = CatalogPushStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
    }

    /// <summary>Superseded (the master changed later) or dismissed by an administrator.</summary>
    public void Discard(DateTimeOffset now, string reason)
    {
        Status = CatalogPushStatus.Discarded;
        LastError = Clip(reason);
        SentAt = now;
    }

    private static string Clip(string text) => text.Length > 1000 ? text[..1000] : text;
}
