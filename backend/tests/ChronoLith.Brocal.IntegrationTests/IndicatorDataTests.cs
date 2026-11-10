using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Indicators;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Hse;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Authentication;
using ChronoLith.Brocal.Infrastructure.Persistence;
using ChronoLith.Brocal.Infrastructure.Persistence.Interceptors;
using ChronoLith.Brocal.Infrastructure.Platform;
using ChronoLith.Brocal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.IntegrationTests;

/// <summary>
/// The database side of the indicators over the demo company: what the loader reads for a well or facility of the
/// Shell (adding up the linked sites) and the marker the persistence layer writes with the business change.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IndicatorDataTests(BrocalApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly WorkOrderStatus[] Open = [WorkOrderStatus.Scheduled, WorkOrderStatus.Dispatched, WorkOrderStatus.InProgress];

    /// <summary>Links demo sites to elements of the replica for the duration of a test.</summary>
    private sealed class LinkedSites(BrocalDbContext db) : IAsyncDisposable
    {
        public Guid Well { get; } = Guid.NewGuid();

        public Guid Facility { get; } = Guid.NewGuid();

        /// <summary>In the replica, linked to nothing (a site that was unlinked from it).</summary>
        public Guid Unlinked { get; } = Guid.NewGuid();

        /// <summary>Linked from a site but unknown to the replica (the Shell would reject it).</summary>
        public Guid Unknown { get; } = Guid.NewGuid();

        public List<Site> WellSites { get; } = [];

        public async Task LinkAsync()
        {
            var now = DateTimeOffset.UtcNow;
            db.SharedCatalogItems.Add(SharedCatalogItem.Create(Well, SharedCatalogNames.Wells, new SharedCatalogData("IND-W", "Pozo de indicadores", null, true, now, "{}"), null, now));
            db.SharedCatalogItems.Add(SharedCatalogItem.Create(Facility, SharedCatalogNames.Facilities, new SharedCatalogData("IND-F", "Batería de indicadores", null, true, now, "{}"), null, now));
            db.SharedCatalogItems.Add(SharedCatalogItem.Create(Unlinked, SharedCatalogNames.Wells, new SharedCatalogData("IND-U", "Pozo desvinculado", null, true, now, "{}"), null, now));

            var demo = await db.Tenants.AsNoTracking().SingleAsync(t => t.Slug == BrocalApiFactory.DemoTenant, Ct);
            var sites = await db.Sites.IgnoreQueryFilters().Where(s => s.TenantId == demo.Id).ToListAsync(Ct);
            foreach (var site in sites)
            {
                switch (site.Code)
                {
                    case "SEN-101" or "SAM-845" or "CUN-12":
                        site.LinkToCatalog(Well, null, null);
                        WellSites.Add(site);
                        break;
                    case "BAT-PAR":
                        site.LinkToCatalog(Facility, null, null);
                        break;
                    case "PATIO-VHSA":
                        site.LinkToCatalog(Unknown, null, null);
                        break;
                }
            }

            await db.SaveChangesAsync(Ct);
        }

        public async ValueTask DisposeAsync()
        {
            Guid?[] links = [Well, Facility, Unknown];
            Guid[] items = [Well, Facility, Unlinked];
            await db.Sites.IgnoreQueryFilters().Where(s => links.Contains(s.ShellCatalogId)).ExecuteUpdateAsync(s => s.SetProperty(x => x.ShellCatalogId, (Guid?)null));
            await db.SharedCatalogItems.Where(i => items.Contains(i.Id)).ExecuteDeleteAsync();
            await db.IndicatorMessages.ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task The_loader_adds_up_the_sites_linked_to_a_well_and_retires_the_subjects_left_without_sites()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrocalDbContext>();
        var loader = scope.ServiceProvider.GetRequiredService<IIndicatorDataSource>();
        await using var linked = new LinkedSites(db);
        await linked.LinkAsync();

        var today = BusinessCalendar.Today(TimeProvider.System);
        var siteIds = linked.WellSites.Select(s => s.Id).ToList();
        var orders = await db.WorkOrders.IgnoreQueryFilters().AsNoTracking().Where(w => siteIds.Contains(w.SiteId) && Open.Contains(w.Status)).ToListAsync(Ct);
        var drafts = await db.Tickets.IgnoreQueryFilters().CountAsync(t => siteIds.Contains(t.SiteId) && t.Status == TicketStatus.Draft, Ct);
        var day = await db.Tickets.IgnoreQueryFilters()
            .Where(t => siteIds.Contains(t.SiteId) && t.Status != TicketStatus.Cancelled && t.ServiceDate <= today)
            .OrderByDescending(t => t.ServiceDate).Select(t => t.ServiceDate).FirstAsync(Ct);
        var ticketsThatDay = await db.Tickets.IgnoreQueryFilters().CountAsync(t => siteIds.Contains(t.SiteId) && t.Status != TicketStatus.Cancelled && t.ServiceDate == day, Ct);
        var openIncidents = await db.Incidents.IgnoreQueryFilters().CountAsync(i => i.SiteId != null && siteIds.Contains(i.SiteId.Value) && i.Status != IncidentStatus.Closed, Ct);
        var orderUnits = orders.SelectMany(o => o.UnitIds).Distinct().ToList();
        var activeUnits = await db.Units.IgnoreQueryFilters().CountAsync(u => orderUnits.Contains(u.Id) && u.IsActive, Ct);

        var changes = new IndicatorChangeSet();
        changes.AddSite(siteIds[0]);
        changes.AddDay(day);
        changes.AddSubject(linked.Unlinked, null);
        var data = await loader.LoadAsync(changes, today, Ct);

        var facts = Assert.Single(data.Subjects);
        Assert.Equal(BrocalIndicators.Well, facts.SubjectType);
        Assert.Equal(linked.Well, facts.SubjectId);
        Assert.True(facts.IsActive);
        Assert.True(orders.Count > 0, "el demo tiene órdenes abiertas en los pozos");
        Assert.Equal(orders.Count, facts.OpenWorkOrders);
        Assert.Equal(drafts, facts.UnsignedTickets);
        Assert.Equal(ticketsThatDay, facts.TicketsByDay[day]);
        Assert.Equal(openIncidents, facts.OpenIncidents);
        Assert.Equal(activeUnits, facts.DeployedUnits);
        Assert.True(facts.DeployedEmployees > 0, "las cuadrillas de las órdenes abiertas cuentan como personal desplegado");

        // One cell per deployed person or unit and required type: the mandatory types plus the ones the contract of the
        // demo demands (H2S, medical exam and SSPA of the people; TC, RC policy and VFM of the units).
        var demo = await db.Tenants.AsNoTracking().SingleAsync(t => t.Slug == BrocalApiFactory.DemoTenant, Ct);
        var types = await db.DocumentTypes.IgnoreQueryFilters().AsNoTracking().Where(t => t.TenantId == demo.Id && t.IsActive).ToListAsync(Ct);
        var requirement = await db.ComplianceRequirements.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.TenantId == demo.Id && r.Code == "PEP-640", Ct);
        var unitTypes = types.Count(t => t.AppliesTo == Domain.Compliance.DocumentSubject.Unit && (t.IsMandatory || requirement.UnitDocumentTypeIds.Contains(t.Id)));
        var employeeTypes = types.Count(t => t.AppliesTo == Domain.Compliance.DocumentSubject.Employee && (t.IsMandatory || requirement.EmployeeDocumentTypeIds.Contains(t.Id)));
        Assert.True(orders.All(o => o.ContractId == requirement.ContractId), "las órdenes del demo son del contrato del requisito");
        Assert.Equal(facts.DeployedUnits * unitTypes + facts.DeployedEmployees * employeeTypes, facts.Documents.Count);
        Assert.Contains(facts.Documents, d => d.Status == Domain.Common.ComplianceStatus.Valid);

        // The subject no site links to anymore is retired (its type comes from the replica).
        Assert.Equal(new RetiredSubject(BrocalIndicators.Well, linked.Unlinked), Assert.Single(data.Retired));

        // The daily republication covers every active linked subject the replica knows (not the unknown one).
        var all = await loader.LoadAsync(new IndicatorChangeSet { All = true }, today, Ct);
        Assert.Contains(all.Subjects, s => s.SubjectId == linked.Well);
        Assert.Contains(all.Subjects, s => s.SubjectId == linked.Facility && s.SubjectType == BrocalIndicators.Facility);
        Assert.DoesNotContain(all.Subjects, s => s.SubjectId == linked.Unknown);
        Assert.DoesNotContain(all.Retired, r => r.SubjectId == linked.Well || r.SubjectId == linked.Facility || r.SubjectId == linked.Unknown);

        // And the values computed from it are complete for the well.
        var values = IndicatorCalculator.Calculate(facts, today, changes.Days, DateTimeOffset.UtcNow, new IndicatorPublishingSettings("/brocal/", "brocal.access"));
        Assert.Contains(values, v => v.Indicator == BrocalIndicators.DocumentsCompliance && v.Value is >= 0 and <= 100);
        Assert.Equal(values.Count, values.Select(v => (v.Indicator, v.Period)).Distinct().Count());
        Assert.Contains(values, v => v.Indicator == BrocalIndicators.TicketsServices && v.Period == day && v.Value == ticketsThatDay);
    }

    [Fact]
    public async Task Subjects_without_an_active_site_of_an_operational_company_or_inactive_in_the_replica_are_retired()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrocalDbContext>();
        var loader = scope.ServiceProvider.GetRequiredService<IIndicatorDataSource>();
        await using var linked = new LinkedSites(db);
        await linked.LinkAsync();
        var today = BusinessCalendar.Today(TimeProvider.System);
        var demo = await db.Tenants.AsNoTracking().SingleAsync(t => t.Slug == BrocalApiFactory.DemoTenant, Ct);
        var facilitySite = await db.Sites.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.ShellCatalogId == linked.Facility, Ct);

        try
        {
            // The only site of the facility is deactivated: retired (with its hint and in the daily run), never published.
            await db.Sites.IgnoreQueryFilters().Where(s => s.Id == facilitySite.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), Ct);
            var hinted = new IndicatorChangeSet();
            hinted.AddSite(facilitySite.Id);
            var byHint = await loader.LoadAsync(hinted, today, Ct);
            Assert.Empty(byHint.Subjects);
            Assert.Equal(new RetiredSubject(BrocalIndicators.Facility, linked.Facility), Assert.Single(byHint.Retired));
            var daily = await loader.LoadAsync(new IndicatorChangeSet { All = true }, today, Ct);
            Assert.Contains(new RetiredSubject(BrocalIndicators.Facility, linked.Facility), daily.Retired);
            Assert.Contains(daily.Subjects, s => s.SubjectId == linked.Well);

            // The well is deactivated (or deleted) in the Shell: the replica keeps it inactive and it is no longer published.
            await db.SharedCatalogItems.Where(i => i.Id == linked.Well).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), Ct);
            var inactive = await loader.LoadAsync(new IndicatorChangeSet { All = true }, today, Ct);
            Assert.DoesNotContain(inactive.Subjects, s => s.SubjectId == linked.Well);
            Assert.Contains(new RetiredSubject(BrocalIndicators.Well, linked.Well), inactive.Retired);
            await db.SharedCatalogItems.Where(i => i.Id == linked.Well).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true), Ct);

            // The only company linked to the well is cancelled: its records no longer add up and the well is retired.
            await db.Tenants.Where(t => t.Id == demo.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TenantStatus.Cancelled), Ct);
            var cancelled = await loader.LoadAsync(new IndicatorChangeSet { All = true }, today, Ct);
            Assert.DoesNotContain(cancelled.Subjects, s => s.SubjectId == linked.Well);
            Assert.Contains(new RetiredSubject(BrocalIndicators.Well, linked.Well), cancelled.Retired);
        }
        finally
        {
            await db.Tenants.Where(t => t.Id == demo.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, demo.Status));
            await db.Sites.IgnoreQueryFilters().Where(s => s.Id == facilitySite.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, facilitySite.IsActive));
        }

        // Changing the status of a company recalculates everything; deactivating a well of the replica, that well.
        var tenant = await db.Tenants.SingleAsync(t => t.Id == demo.Id, Ct);
        db.Entry(tenant).Property(t => t.Status).CurrentValue = TenantStatus.Suspended;
        Assert.True(IndicatorChangeInterceptor.Collect(db.ChangeTracker.Entries(), BusinessCalendar.Resolve(null)).All);
        db.Entry(tenant).State = EntityState.Unchanged;
        db.Entry(tenant).Reload();

        var well = await db.SharedCatalogItems.SingleAsync(i => i.Id == linked.Well, Ct);
        well.Deactivate(null, DateTimeOffset.UtcNow);
        var deactivated = IndicatorChangeInterceptor.Collect(db.ChangeTracker.Entries(), BusinessCalendar.Resolve(null));
        Assert.False(deactivated.All);
        Assert.Equal(BrocalIndicators.Well, deactivated.Subjects[linked.Well]);
        db.Entry(well).State = EntityState.Unchanged;
        db.Entry(well).Reload();
    }

    [Fact]
    public async Task The_outbox_store_keeps_pending_messages_on_purge_and_remembers_the_subjects()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrocalDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IIndicatorOutbox>();
        var now = DateTimeOffset.UtcNow;
        var subject = Guid.NewGuid();

        await db.IndicatorMessages.ExecuteDeleteAsync(Ct);
        try
        {
            var done = IndicatorMessage.Publish("{}", 1, now.AddDays(-8));
            done.MarkDone(now.AddDays(-8));
            var pending = IndicatorMessage.Retire($"api/indicators/values/brocal/work-orders.open/well/{subject}", now.AddDays(-9));
            outbox.Add(done);
            outbox.Add(pending);
            var row = IndicatorSubject.Create(subject, BrocalIndicators.Well, now);
            row.MarkPublished(BrocalIndicators.Well, true, now);
            outbox.AddSubject(row);
            await outbox.SaveChangesAsync(Ct);

            Assert.Equal(1, await outbox.PurgeAsync(now.AddDays(-7), Ct));
            var left = await db.IndicatorMessages.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(pending.Id, left.Id);
            Assert.True(left.KeepRetrying);

            var all = await outbox.SubjectsAsync(null, Ct);
            Assert.True(all[subject].IsPublished);
            Assert.True(all[subject].HasNextExpiry);
            Assert.Empty(await outbox.SubjectsAsync([Guid.NewGuid()], Ct));
            Assert.Empty(await outbox.SubjectsAsync([], Ct));
        }
        finally
        {
            await db.IndicatorMessages.ExecuteDeleteAsync();
            await db.IndicatorSubjects.Where(s => s.Id == subject).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task The_marker_is_written_with_the_business_change_only_when_connected_to_a_shell()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrocalDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        await using var linked = new LinkedSites(db);
        await linked.LinkAsync();

        IndicatorChangeInterceptor Interceptor(TenancyOptions tenancy) => new(
            Options.Create(new IndicatorOptions()),
            Options.Create(new ShellOptions { ApiUrl = "http://shell.test" }),
            Options.Create(new KeycloakOptions { AuthServerUrl = "http://keycloak.test", Realm = "chronolith", ClientId = "chronolith-brocal-api", ClientSecret = "secret" }),
            Options.Create(tenancy),
            tenantContext,
            TimeProvider.System);

        // A ticket moved to another site and day touches both sites and both days.
        var ticket = await db.Tickets.IgnoreQueryFilters().FirstAsync(t => t.SiteId == linked.WellSites[0].Id, Ct);
        var originalDay = ticket.ServiceDate;
        db.Entry(ticket).Property(t => t.SiteId).CurrentValue = linked.WellSites[1].Id;
        db.Entry(ticket).Property(t => t.ServiceDate).CurrentValue = originalDay.AddDays(-1);
        var moved = IndicatorChangeInterceptor.Collect(db.ChangeTracker.Entries(), BusinessCalendar.Resolve(null));
        Assert.Contains(linked.WellSites[0].Id, moved.Sites);
        Assert.Contains(linked.WellSites[1].Id, moved.Sites);
        Assert.Contains(originalDay, moved.Days);
        Assert.Contains(originalDay.AddDays(-1), moved.Days);
        db.Entry(ticket).State = EntityState.Unchanged;
        db.Entry(ticket).Reload();

        // Unlinking a site: the old subject is recalculated (or retired), with the type of the site.
        var site = linked.WellSites[2];
        db.Entry(site).Property(s => s.ShellCatalogId).CurrentValue = null;
        var unlinked = IndicatorChangeInterceptor.Collect(db.ChangeTracker.Entries(), BusinessCalendar.Resolve(null));
        Assert.Equal(BrocalIndicators.Well, unlinked.Subjects[linked.Well]);
        Assert.Contains(site.Id, unlinked.Sites);

        // Standalone without the service account of the Shell: nothing is queued.
        Interceptor(new TenancyOptions { Mode = "Standalone" }).Capture(db);
        Assert.Empty(db.ChangeTracker.Entries<IndicatorMessage>());

        // Connected: the marker goes in the same SaveChanges as the business change.
        Interceptor(new TenancyOptions { Mode = "ChronoLith" }).Capture(db);
        await db.SaveChangesAsync(Ct);

        var marker = await db.IndicatorMessages.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(IndicatorMessageKind.Recalculate, marker.Kind);
        Assert.Equal(IndicatorMessageStatus.Pending, marker.Status);
        var saved = IndicatorChangeSet.FromJson(marker.Payload);
        Assert.Contains(site.Id, saved.Sites);
        Assert.Equal(BrocalIndicators.Well, saved.Subjects[linked.Well]);
        Assert.Null(await db.Sites.IgnoreQueryFilters().Where(s => s.Id == site.Id).Select(s => s.ShellCatalogId).SingleAsync(Ct));

        // A change that feeds no indicator queues nothing.
        Assert.True(IndicatorChangeInterceptor.Collect(db.ChangeTracker.Entries(), BusinessCalendar.Resolve(null)).IsEmpty);
    }
}
