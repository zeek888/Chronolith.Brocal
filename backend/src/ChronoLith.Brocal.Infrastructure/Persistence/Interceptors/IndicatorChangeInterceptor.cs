using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Indicators;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Hse;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Authentication;
using ChronoLith.Brocal.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Outbox of the indicators: when a save touches what the indicators are calculated from (tickets, work orders,
/// incidents, linked sites, compliance documents, units, people, crews, the status of a company, the active flag of a
/// well or facility of the replica), a <see cref="IndicatorMessage"/> marker with
/// the affected sites, subjects and days is added to the same unit of work, so it commits or rolls back with the
/// business change. The values are calculated later by <see cref="IndicatorPublisher"/>. Nothing is written when
/// Brocal is not connected to a Shell.
/// </summary>
internal sealed class IndicatorChangeInterceptor(
    IOptions<IndicatorOptions> indicators,
    IOptions<ShellOptions> shell,
    IOptions<KeycloakOptions> keycloak,
    IOptions<TenancyOptions> tenancy,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>Adds the marker of the pending changes to the context (before it saves them).</summary>
    internal void Capture(DbContext? context)
    {
        if (context is null || !IndicatorAvailability.IsEnabled(indicators.Value, shell.Value, keycloak.Value, tenancy.Value))
        {
            return;
        }

        var zone = BusinessCalendar.Resolve(tenantContext.HasTenant ? tenantContext.Tenant.Settings.TimeZone : null);
        var changes = Collect(context.ChangeTracker.Entries(), zone);
        if (!changes.IsEmpty)
        {
            context.Add(IndicatorMessage.Recalculate(changes.ToJson(), timeProvider.GetUtcNow()));
        }
    }

    /// <summary>What the pending changes affect (before and after the change: a ticket moved to another site or day touches both).</summary>
    internal static IndicatorChangeSet Collect(IEnumerable<EntityEntry> entries, TimeZoneInfo zone)
    {
        var changes = new IndicatorChangeSet();
        foreach (var entry in entries.Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            switch (entry.Entity)
            {
                case ServiceTicket:
                    foreach (var site in Values<Guid>(entry, nameof(ServiceTicket.SiteId)))
                    {
                        changes.AddSite(site);
                    }

                    foreach (var day in Values<DateOnly>(entry, nameof(ServiceTicket.ServiceDate)))
                    {
                        changes.AddDay(day);
                    }

                    break;

                case WorkOrder:
                    foreach (var site in Values<Guid>(entry, nameof(WorkOrder.SiteId)))
                    {
                        changes.AddSite(site);
                    }

                    break;

                case Incident:
                    foreach (var site in Values<Guid?>(entry, nameof(Incident.SiteId)))
                    {
                        changes.AddSite(site);
                    }

                    foreach (var occurredAt in Values<DateTimeOffset>(entry, nameof(Incident.OccurredAt)))
                    {
                        changes.AddDay(BusinessCalendar.DateOf(occurredAt, zone));
                    }

                    break;

                case Site site:
                    changes.AddSite(site.Id);
                    if (entry.State != EntityState.Added)
                    {
                        // The subject the site was linked to before (unlinked or deleted): recalculated, or retired when no site is left.
                        changes.AddSubject((Guid?)entry.Property(nameof(Site.ShellCatalogId)).OriginalValue, SubjectTypeOf((SiteType)entry.Property(nameof(Site.Type)).OriginalValue!));
                    }

                    if (entry.State != EntityState.Deleted)
                    {
                        changes.AddSubject(site.ShellCatalogId, SubjectTypeOf(site.Type));
                    }

                    break;

                case ComplianceDocument or DocumentType or ComplianceRequirement or Crew:
                    changes.All = true;
                    break;

                case Unit or Employee when entry.State != EntityState.Modified || IsModified(entry, nameof(CatalogAggregate.IsActive), nameof(CatalogAggregate.Code)):
                    changes.All = true;
                    break;

                // A company suspended, cancelled, reactivated or with another trial end: only operational companies add up.
                case Tenant when entry.State == EntityState.Deleted
                                 || entry.State == EntityState.Modified && IsModified(entry, nameof(Tenant.Status), nameof(Tenant.TrialEndsOn)):
                    changes.All = true;
                    break;

                // A well or facility deactivated (or deleted) in the Shell stops being published; reactivated, it comes back.
                case SharedCatalogItem item when entry.State == EntityState.Modified && IsModified(entry, nameof(SharedCatalogItem.IsActive))
                                                 && BrocalIndicators.SubjectTypeOf(item.Catalog) is { } type:
                    changes.AddSubject(item.Id, type);
                    break;
            }
        }

        return changes;
    }

    private static string? SubjectTypeOf(SiteType type) => type switch
    {
        SiteType.Well => BrocalIndicators.Well,
        SiteType.Facility => BrocalIndicators.Facility,
        _ => null
    };

    /// <summary>Original and current value of a property (only the current one for a new row).</summary>
    private static IEnumerable<T> Values<T>(EntityEntry entry, string property)
    {
        var current = entry.Property(property);
        if (entry.State != EntityState.Added && current.OriginalValue is T original)
        {
            yield return original;
        }

        if (entry.State != EntityState.Deleted && current.CurrentValue is T value)
        {
            yield return value;
        }
    }

    private static bool IsModified(EntityEntry entry, params string[] properties) =>
        properties.Any(p => entry.Property(p) is { IsModified: true } property && !Equals(property.OriginalValue, property.CurrentValue));
}
