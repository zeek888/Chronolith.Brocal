using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Application.UnitTests;

/// <summary>Rules of the consumer of the change feed (module contract, section 5.1) against a simulated Shell.</summary>
public sealed class CatalogFeedConsumerTests
{
    private readonly Guid _well = Guid.NewGuid();
    private readonly Guid _field = Guid.NewGuid();
    private readonly Guid _company = Guid.NewGuid();

    /// <summary>A replica that already synced once (cursor 10, one well), so a run does not start with a full load.</summary>
    private InMemoryReplica SyncedReplica()
    {
        var replica = new InMemoryReplica();
        replica.State.RecordFullSync(10, Feed.Clock.GetUtcNow().AddDays(-1));
        replica.Add(SharedCatalogItem.Create(_well, SharedCatalogNames.Wells, new SharedCatalogData("KU-61", "Ku 61", _field, true, Feed.Clock.GetUtcNow().AddDays(-2), "{}"), 10, Feed.Clock.GetUtcNow().AddDays(-1)));
        return replica;
    }

    [Fact]
    public async Task Applies_a_page_in_order_adopts_its_cursor_and_reports_the_applied_ids()
    {
        var feed = new FakeFeedClient();
        var replica = SyncedReplica();
        feed.Pages.Enqueue(Feed.Page(13, 13, false,
            Feed.Change(11, SharedCatalogNames.Fields, _field, "KU"),
            Feed.Change(12, SharedCatalogNames.Wells, _well, "KU-61", name: "Ku 61 (renombrado)", fieldId: _field),
            Feed.Change(13, SharedCatalogNames.Companies, _company, "PEP")));

        var result = await Feed.Consumer(feed, replica).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Applied);
        Assert.Equal(0, result.Errors);
        Assert.Equal(13, replica.State.Cursor);
        Assert.Equal(3, replica.Items.Count);
        Assert.Equal("Ku 61 (renombrado)", replica.Items[_well].Name);
        Assert.Equal(12, replica.Items[_well].LastChangeId);
        Assert.Equal(SharedCatalogNames.Companies, replica.Items[_company].Catalog);
        var report = Assert.Single(feed.Reports);
        Assert.Equal(13, report.Cursor);
        Assert.Equal([11, 12, 13], report.Applied);
        Assert.Empty(report.Errors);
        Assert.Equal([(10L, 200)], feed.Pulls);
    }

    [Fact]
    public async Task Applying_the_same_page_twice_leaves_the_replica_as_it_was()
    {
        var feed = new FakeFeedClient();
        var replica = SyncedReplica();
        var page = Feed.Page(12, 12, false, Feed.Change(11, SharedCatalogNames.Fields, _field, "KU"), Feed.Change(12, SharedCatalogNames.Wells, _well, "KU-61", fieldId: _field));
        feed.Pages.Enqueue(page);
        feed.Pages.Enqueue(page);

        var consumer = Feed.Consumer(feed, replica);
        await consumer.RunAsync(false, TestContext.Current.CancellationToken);
        var first = replica.Items.ToDictionary(i => i.Key, i => (i.Value.Code, i.Value.Name, i.Value.ParentId, i.Value.IsActive));
        await consumer.RunAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(2, replica.Items.Count);
        Assert.Equal(first, replica.Items.ToDictionary(i => i.Key, i => (i.Value.Code, i.Value.Name, i.Value.ParentId, i.Value.IsActive)));
        Assert.Equal(12, replica.State.Cursor);
    }

    [Fact]
    public async Task Pulls_again_while_the_feed_has_more()
    {
        var feed = new FakeFeedClient();
        var replica = SyncedReplica();
        feed.Pages.Enqueue(Feed.Page(11, 12, true, Feed.Change(11, SharedCatalogNames.Fields, _field, "KU")));
        feed.Pages.Enqueue(Feed.Page(12, 12, false, Feed.Change(12, SharedCatalogNames.Companies, _company, "PEP")));

        var result = await Feed.Consumer(feed, replica).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Pages);
        Assert.Equal([(10L, 200), (11L, 200)], feed.Pulls);
        Assert.Equal(2, feed.Reports.Count);
        Assert.Equal(12, replica.State.Cursor);
    }

    [Fact]
    public async Task A_replay_behind_the_cursor_is_applied_and_reported()
    {
        var feed = new FakeFeedClient();
        var replica = SyncedReplica();
        replica.State.AdoptCursor(50);
        feed.Pages.Enqueue(Feed.Page(50, 50, false, Feed.Change(7, SharedCatalogNames.Wells, _well, "KU-61", name: "Ku 61 reproducido", replay: true, updatedAt: Feed.Clock.GetUtcNow().AddDays(-5))));

        var result = await Feed.Consumer(feed, replica).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Applied);
        Assert.Equal("Ku 61 reproducido", replica.Items[_well].Name);
        Assert.Equal(50, replica.State.Cursor);
        Assert.Equal([7L], Assert.Single(feed.Reports).Applied);
    }

    [Fact]
    public async Task An_older_change_never_overwrites_a_newer_snapshot_unless_it_is_a_replay()
    {
        var feed = new FakeFeedClient();
        var replica = SyncedReplica();
        feed.Pages.Enqueue(Feed.Page(11, 11, false, Feed.Change(11, SharedCatalogNames.Wells, _well, "KU-61", name: "Viejo", updatedAt: Feed.Clock.GetUtcNow().AddDays(-9))));

        var result = await Feed.Consumer(feed, replica).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal("Ku 61", replica.Items[_well].Name);
        Assert.Equal(1, result.Skipped);
        Assert.Equal([11L], Assert.Single(feed.Reports).Applied);
    }

    [Fact]
    public async Task Own_changes_refresh_the_replica_but_do_not_touch_the_linked_records()
    {
        var feed = new FakeFeedClient();
        var replica = SyncedReplica();
        var links = new RecordingReconciler();
        feed.Pages.Enqueue(Feed.Page(12, 12, false,
            Feed.Change(11, SharedCatalogNames.Wells, _well, "KU-61", name: "Desde Brocal", source: "brocal"),
            Feed.Change(12, SharedCatalogNames.Fields, _field, "KU", source: "activities")));

        var result = await Feed.Consumer(feed, replica, links).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal("Desde Brocal", replica.Items[_well].Name);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, result.Applied);
        Assert.Contains(links.Calls, c => c.Id == _well && c.OwnChange);
        Assert.Contains(links.Calls, c => c.Id == _field && !c.OwnChange);
        Assert.Equal([11, 12], Assert.Single(feed.Reports).Applied);
    }

    [Fact]
    public async Task A_change_that_fails_is_reported_as_an_error_and_does_not_stop_the_page()
    {
        var feed = new FakeFeedClient();
        var replica = SyncedReplica();
        feed.Pages.Enqueue(Feed.Page(13, 13, false,
            Feed.Change(11, SharedCatalogNames.Fields, _field, "KU"),
            Feed.Change(12, "pipelines", Guid.NewGuid(), "DUCTO-1"),
            Feed.Change(13, SharedCatalogNames.Companies, _company, "PEP")));

        var result = await Feed.Consumer(feed, replica).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Applied);
        Assert.Equal(1, result.Errors);
        Assert.Equal(13, replica.State.Cursor);
        var report = Assert.Single(feed.Reports);
        Assert.Equal([11, 13], report.Applied);
        var error = Assert.Single(report.Errors);
        Assert.Equal(12, error.ChangeId);
        Assert.Contains("pipelines", error.Message);
        Assert.Equal(1, replica.State.LastRunErrors);
    }

    [Fact]
    public async Task The_cursor_of_the_response_is_adopted_even_when_an_administrator_rewound_it()
    {
        var feed = new FakeFeedClient();
        var replica = SyncedReplica();
        replica.State.AdoptCursor(100);
        feed.Pages.Enqueue(Feed.Page(40, 100, false, Feed.Change(40, SharedCatalogNames.Fields, _field, "KU")));

        await Feed.Consumer(feed, replica).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(40, replica.State.Cursor);
        Assert.Equal(40, Assert.Single(feed.Reports).Cursor);
    }

    [Fact]
    public async Task Deleted_changes_deactivate_the_element_instead_of_removing_it()
    {
        var feed = new FakeFeedClient();
        var replica = SyncedReplica();
        var links = new RecordingReconciler();
        feed.Pages.Enqueue(Feed.Page(11, 11, false, Feed.Change(11, SharedCatalogNames.Wells, _well, "KU-61", action: "deleted")));

        await Feed.Consumer(feed, replica, links).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.False(replica.Items[_well].IsActive);
        Assert.Contains(links.Calls, c => c.Id == _well && !c.IsActive);
    }

    [Fact]
    public async Task RequiresFullSync_reloads_every_catalog_and_continues_from_the_cursor_received()
    {
        var feed = new FakeFeedClient();
        var replica = SyncedReplica();
        var stale = Guid.NewGuid();
        replica.Add(SharedCatalogItem.Create(stale, SharedCatalogNames.Wells, new SharedCatalogData("VIEJO-1", "Ya no existe", null, true, null, "{}"), 9, Feed.Clock.GetUtcNow()));
        feed.Pages.Enqueue(new CatalogFeedPage(900, 900, false, true, []));
        feed.Catalogs[SharedCatalogNames.Fields] = [Feed.Row(_field, "KU", "Ku")];
        feed.Catalogs[SharedCatalogNames.Wells] = [Feed.Row(_well, "KU-61", "Ku 61 maestro", _field), Feed.Row(Guid.NewGuid(), "KU-62", "Ku 62", _field)];
        feed.Latest = 900;

        var result = await Feed.Consumer(feed, replica).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.True(result.FullSync);
        Assert.Equal(SharedCatalogNames.All, feed.FullLoads);
        Assert.Equal(900, replica.State.Cursor);
        Assert.NotNull(replica.State.LastFullSyncAt);
        Assert.Equal("Ku 61 maestro", replica.Items[_well].Name);
        Assert.False(replica.Items[stale].IsActive);
        Assert.Equal(4, replica.Items.Count);
        Assert.Contains(feed.Reports, r => r.Cursor == 900);
        Assert.Equal(900, feed.Pulls.Last().Since);
    }

    [Fact]
    public async Task The_first_run_loads_the_catalogs_completely_and_then_follows_the_feed()
    {
        var feed = new FakeFeedClient();
        var replica = new InMemoryReplica();
        feed.Pages.Enqueue(Feed.Page(1, 30, true, Feed.Change(1, SharedCatalogNames.Fields, _field, "KU")));
        feed.Catalogs[SharedCatalogNames.Wells] = [Feed.Row(_well, "KU-61", "Ku 61", _field)];
        feed.Latest = 30;

        var result = await Feed.Consumer(feed, replica).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.True(result.FullSync);
        Assert.Equal((0L, 1), feed.Pulls[0]);
        Assert.Equal((30L, 200), feed.Pulls[1]);
        Assert.Equal(30, replica.State.Cursor);
        Assert.Single(replica.Items);
        Assert.Equal(SharedCatalogNames.All, feed.FullLoads);
    }

    [Fact]
    public async Task A_report_that_fails_is_recorded_and_the_feed_goes_on()
    {
        var feed = new FakeFeedClient { FailReports = true };
        var replica = SyncedReplica();
        feed.Pages.Enqueue(Feed.Page(11, 11, false, Feed.Change(11, SharedCatalogNames.Fields, _field, "KU")));

        var result = await Feed.Consumer(feed, replica).RunAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Applied);
        Assert.Equal(11, replica.State.Cursor);
        Assert.Contains("Reporte al Shell", replica.State.LastError);
    }
}
