using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChronoLith.Brocal.Application.UnitTests;

internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Simulated Shell: scripted feed pages, the catalogs of a full load and the reports it received.</summary>
internal sealed class FakeFeedClient : ICatalogFeedClient
{
    public Queue<CatalogFeedPage> Pages { get; } = new();

    public Dictionary<string, List<ShellCatalogRow>> Catalogs { get; } = new(StringComparer.Ordinal);

    public List<(long Since, int Limit)> Pulls { get; } = [];

    public List<(long Cursor, IReadOnlyList<long> Applied, IReadOnlyList<CatalogFeedError> Errors)> Reports { get; } = [];

    public List<string> FullLoads { get; } = [];

    public bool IsConfigured { get; set; } = true;

    public bool FailReports { get; set; }

    /// <summary>Returned when the script runs out of pages (an empty, up-to-date feed).</summary>
    public long Latest { get; set; }

    public Task<CatalogFeedPage> PullAsync(long since, int limit, CancellationToken cancellationToken)
    {
        Pulls.Add((since, limit));
        return Task.FromResult(Pages.Count > 0 ? Pages.Dequeue() : new CatalogFeedPage(since, Math.Max(Latest, since), false, false, []));
    }

    public Task ReportAsync(long cursor, IReadOnlyList<long> applied, IReadOnlyList<CatalogFeedError> errors, CancellationToken cancellationToken)
    {
        if (FailReports)
        {
            throw new HttpRequestException("503 Service Unavailable");
        }

        Reports.Add((cursor, applied, errors));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ShellCatalogRow>?> ReadAllAsync(string catalog, CancellationToken cancellationToken)
    {
        FullLoads.Add(catalog);
        return Task.FromResult<IReadOnlyList<ShellCatalogRow>?>(Catalogs.TryGetValue(catalog, out var rows) ? rows : []);
    }
}

internal sealed class InMemoryReplica : ICatalogReplica
{
    public Dictionary<Guid, SharedCatalogItem> Items { get; } = [];

    public CatalogFeedState State { get; } = CatalogFeedState.Create();

    public int Saves { get; private set; }

    public Task<CatalogFeedState> GetStateAsync(CancellationToken cancellationToken) => Task.FromResult(State);

    public Task<bool> AnyAsync(CancellationToken cancellationToken) => Task.FromResult(Items.Count > 0);

    public Task<Dictionary<Guid, SharedCatalogItem>> FindAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult(ids.Where(Items.ContainsKey).ToDictionary(id => id, id => Items[id]));

    public Task<IReadOnlyList<SharedCatalogItem>> AllAsync(string catalog, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SharedCatalogItem>>(Items.Values.Where(i => i.Catalog == catalog).ToList());

    public void Add(SharedCatalogItem item) => Items[item.Id] = item;

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingReconciler : ICatalogLinkReconciler
{
    public List<(Guid Id, string Catalog, bool IsActive, bool OwnChange)> Calls { get; } = [];

    public Task ReconcileAsync(SharedCatalogItem item, bool ownChange, CancellationToken cancellationToken)
    {
        Calls.Add((item.Id, item.Catalog, item.IsActive, ownChange));
        return Task.CompletedTask;
    }
}

internal static class Feed
{
    public static readonly TestClock Clock = new(new DateTimeOffset(2026, 11, 6, 18, 0, 0, TimeSpan.Zero));

    public static CatalogFeedChange Change(long id, string catalog, Guid entityId, string code, string action = "updated", string? source = null, bool replay = false, DateTimeOffset? updatedAt = null, Guid? fieldId = null, string? name = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["id"] = entityId,
            ["code"] = code,
            ["name"] = name ?? $"{code} nombre",
            ["isActive"] = action != "deleted",
            ["updatedAt"] = (updatedAt ?? Clock.GetUtcNow().AddMinutes(-1)).ToString("O"),
            ["fieldId"] = fieldId,
            ["fieldName"] = fieldId is null ? null : "Campo",
            ["latitude"] = 18.5m,
            ["longitude"] = -93.1m,
            ["status"] = "producing"
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(data));
        return new CatalogFeedChange(id, catalog, entityId, code, name ?? $"{code} nombre", action, Clock.GetUtcNow().AddMinutes(-1), "admin", source, document.RootElement.Clone(), replay);
    }

    public static CatalogFeedPage Page(long cursor, long latest, bool hasMore, params CatalogFeedChange[] changes) => new(cursor, latest, hasMore, false, changes);

    public static ShellCatalogRow Row(Guid id, string code, string name, Guid? parentId = null, bool isActive = true) =>
        new(id, new SharedCatalogData(code, name, parentId, isActive, Clock.GetUtcNow().AddDays(-1), $$$"""{"id":"{{{id}}}","code":"{{{code}}}","name":"{{{name}}}"}"""));

    public static Features.SharedCatalogs.CatalogFeedConsumer Consumer(FakeFeedClient feed, InMemoryReplica replica, RecordingReconciler? links = null) =>
        new(feed, replica, links ?? new RecordingReconciler(), Clock, NullLogger<Features.SharedCatalogs.CatalogFeedConsumer>.Instance);
}
