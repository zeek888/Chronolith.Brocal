using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Hse;
using ChronoLith.Brocal.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChronoLith.Brocal.Application.Features.Indicators;

/// <summary>
/// Reads, for the wells and facilities of the Shell touched by a change set, what their indicators are calculated
/// from, across every company of the installation (platform read: the tenant filter is ignored on purpose, the values
/// of a subject add up the sites of every operational company linked to it). The queries go through the indexes on
/// <c>site_id</c>, <c>shell_catalog_id</c> and <c>compliance_documents (subject_id, document_type_id)</c>.
/// </summary>
internal sealed class IndicatorDataLoader(IApplicationDbContext db, TimeProvider clock, ILogger<IndicatorDataLoader> logger) : IIndicatorDataSource
{
    private sealed record SiteRow(Guid Id, Guid TenantId, Guid SubjectId, bool IsActive);

    private sealed record TenantRules(TimeZoneInfo Zone, DateOnly Today, int WarningDays);

    public async Task<IndicatorDataSet> LoadAsync(IndicatorChangeSet changes, DateOnly today, CancellationToken cancellationToken)
    {
        // ---- Subjects: explicit ones, the ones of the sites that changed and, with All, every linked one ----
        var hints = new Dictionary<Guid, string?>(changes.Subjects);
        if (changes.Sites.Count > 0)
        {
            var changedSites = changes.Sites.ToList();
            var linked = await db.Sites.IgnoreQueryFilters().AsNoTracking()
                .Where(s => changedSites.Contains(s.Id) && s.ShellCatalogId != null)
                .Select(s => s.ShellCatalogId!.Value)
                .ToListAsync(cancellationToken);
            foreach (var id in linked)
            {
                hints.TryAdd(id, null);
            }
        }

        var explicitIds = hints.Keys.ToList();
        var siteQuery = db.Sites.IgnoreQueryFilters().AsNoTracking().Where(s => s.ShellCatalogId != null);
        if (!changes.All)
        {
            siteQuery = siteQuery.Where(s => explicitIds.Contains(s.ShellCatalogId!.Value));
        }

        var allSites = await siteQuery.Select(s => new SiteRow(s.Id, s.TenantId, s.ShellCatalogId!.Value, s.IsActive)).ToListAsync(cancellationToken);

        // Companies: only the operational ones (active, or in a trial that has not ended) add to a subject. A cancelled
        // or suspended company keeps its records, but they no longer describe the well.
        var now = clock.GetUtcNow();
        var allTenantIds = allSites.Select(s => s.TenantId).Distinct().ToList();
        var tenantRows = allTenantIds.Count == 0
            ? []
            : await db.Tenants.AsNoTracking().Where(t => allTenantIds.Contains(t.Id)).ToListAsync(cancellationToken);
        var tenants = tenantRows
            .Where(t => t.IsOperational(today))
            .ToDictionary(t => t.Id, t =>
            {
                var zone = BusinessCalendar.Resolve(t.Settings.TimeZone);
                return new TenantRules(zone, BusinessCalendar.DateOf(now, zone), t.Settings.ExpiryWarningDays);
            });

        var groups = allSites.GroupBy(s => s.SubjectId).ToDictionary(g => g.Key, g => g.ToList());
        var subjectIds = groups.Keys.Concat(explicitIds).Distinct().ToList();
        var catalogs = subjectIds.Count == 0
            ? []
            : await db.SharedCatalogItems.AsNoTracking()
                .Where(i => subjectIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Catalog, i.IsActive })
                .ToDictionaryAsync(i => i.Id, i => (i.Catalog, i.IsActive), cancellationToken);

        var retired = new List<RetiredSubject>();
        foreach (var id in explicitIds.Where(id => !groups.ContainsKey(id)))
        {
            var type = BrocalIndicators.SubjectTypeOf(catalogs.GetValueOrDefault(id).Catalog) ?? hints[id];
            if (type is null)
            {
                logger.LogDebug("No se retiran los indicadores de {Subject}: se desconoce si es pozo o instalación.", id);
                continue;
            }

            retired.Add(new RetiredSubject(type, id));
        }

        var subjects = new Dictionary<Guid, (string Type, List<SiteRow> Sites)>();
        foreach (var (id, sites) in groups)
        {
            // Only wells and facilities the replica knows: the Shell rejects the whole request for an unknown subject.
            if (!catalogs.TryGetValue(id, out var catalog) || BrocalIndicators.SubjectTypeOf(catalog.Catalog) is not { } type)
            {
                logger.LogDebug("Sin indicadores para {Subject}: no es un pozo ni una instalación de la réplica del Shell.", id);
                continue;
            }

            // Published while an active site of an operational company links it and the element is active in the Shell
            // (an inactive one may have been deleted there: the Shell would reject the whole request). Otherwise the
            // subject stopped belonging to Brocal and its values are retired (once: the publisher remembers it).
            var operational = sites.Where(s => tenants.ContainsKey(s.TenantId)).ToList();
            if (catalog.IsActive && operational.Any(s => s.IsActive))
            {
                subjects[id] = (type, operational);
            }
            else if (changes.All || hints.ContainsKey(id))
            {
                retired.Add(new RetiredSubject(type, id));
            }
        }

        if (subjects.Count == 0)
        {
            return new IndicatorDataSet([], retired);
        }

        var sitesOfSubjects = subjects.Values.SelectMany(s => s.Sites).ToList();
        var siteIds = sitesOfSubjects.Select(s => s.Id).ToList();
        var days = IndicatorCalculator.DaysToPublish(changes.Days, today).ToList();

        // ---- Tickets ----
        var tickets = await db.Tickets.IgnoreQueryFilters().AsNoTracking()
            .Where(t => siteIds.Contains(t.SiteId) && (t.Status == TicketStatus.Draft || days.Contains(t.ServiceDate)))
            .Select(t => new { t.SiteId, t.Status, t.ServiceDate })
            .ToListAsync(cancellationToken);

        // ---- Work orders in progress (and, through them, the personnel and units deployed) ----
        var orders = await db.WorkOrders.IgnoreQueryFilters().AsNoTracking()
            .Where(w => siteIds.Contains(w.SiteId)
                        && (w.Status == WorkOrderStatus.Scheduled || w.Status == WorkOrderStatus.Dispatched || w.Status == WorkOrderStatus.InProgress))
            .ToListAsync(cancellationToken);

        // ---- HSE incidents: the open ones and the ones of the days to publish ----
        var fromUtc = new DateTimeOffset(days[0].AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var incidents = await db.Incidents.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.SiteId != null && siteIds.Contains(i.SiteId.Value) && (i.Status != IncidentStatus.Closed || i.OccurredAt >= fromUtc))
            .Select(i => new { SiteId = i.SiteId!.Value, i.TenantId, i.OccurredAt, i.Status, i.Severity })
            .ToListAsync(cancellationToken);

        var documents = await LoadDocumentsAsync(orders, tenants, cancellationToken);

        var result = new List<SubjectFacts>();
        foreach (var (id, (type, sites)) in subjects)
        {
            var ids = sites.Select(s => s.Id).ToHashSet();
            var subjectTickets = tickets.Where(t => ids.Contains(t.SiteId)).ToList();
            var subjectOrders = orders.Where(o => ids.Contains(o.SiteId)).ToList();
            var subjectIncidents = incidents.Where(i => ids.Contains(i.SiteId)).ToList();
            var open = subjectIncidents.Where(i => i.Status != IncidentStatus.Closed).ToList();
            var deployed = documents.ForOrders(subjectOrders);

            result.Add(new SubjectFacts(
                type,
                id,
                sites.Any(s => s.IsActive),
                subjectOrders.Count,
                subjectTickets.Count(t => t.Status == TicketStatus.Draft),
                subjectTickets.Where(t => t.Status != TicketStatus.Cancelled).GroupBy(t => t.ServiceDate).ToDictionary(g => g.Key, g => g.Count()),
                subjectIncidents
                    .Select(i => BusinessCalendar.DateOf(i.OccurredAt, tenants.TryGetValue(i.TenantId, out var rules) ? rules.Zone : BusinessCalendar.Resolve(null)))
                    .Where(days.Contains)
                    .GroupBy(d => d)
                    .ToDictionary(g => g.Key, g => g.Count()),
                open.Count,
                open.Count == 0 ? null : open.Max(i => i.Severity),
                deployed.Employees,
                deployed.Units,
                deployed.Cells));
        }

        return new IndicatorDataSet(result, retired);
    }

    /// <summary>
    /// Required documents of the people and units of the open orders: the mandatory types of their company plus the
    /// types a requirement of the order's contract (or customer) demands; the latest document of each type decides.
    /// </summary>
    private async Task<DeployedDocuments> LoadDocumentsAsync(IReadOnlyList<WorkOrder> orders, IReadOnlyDictionary<Guid, TenantRules> tenants, CancellationToken cancellationToken)
    {
        if (orders.Count == 0)
        {
            return DeployedDocuments.Empty;
        }

        var tenantIds = orders.Select(o => o.TenantId).Distinct().ToList();
        var crewIds = orders.Where(o => o.CrewId != null).Select(o => o.CrewId!.Value).Distinct().ToList();
        var crews = crewIds.Count == 0
            ? []
            : (await db.Crews.IgnoreQueryFilters().AsNoTracking().Where(c => crewIds.Contains(c.Id)).ToListAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.MemberIds);

        var unitIds = orders.SelectMany(o => o.UnitIds).Distinct().ToList();
        var employeeIds = orders.SelectMany(o => o.EmployeeIds.Concat(o.CrewId is { } crew && crews.TryGetValue(crew, out var members) ? members : [])).Distinct().ToList();

        var units = unitIds.Count == 0
            ? []
            : await db.Units.IgnoreQueryFilters().AsNoTracking().Where(u => unitIds.Contains(u.Id) && u.IsActive).ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
        var employees = employeeIds.Count == 0
            ? []
            : await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => employeeIds.Contains(e.Id) && e.IsActive).ToDictionaryAsync(e => e.Id, e => e.Code, cancellationToken);

        var types = await db.DocumentTypes.IgnoreQueryFilters().AsNoTracking()
            .Where(t => tenantIds.Contains(t.TenantId) && t.IsActive)
            .ToListAsync(cancellationToken);
        var requirements = await db.ComplianceRequirements.IgnoreQueryFilters().AsNoTracking()
            .Where(r => tenantIds.Contains(r.TenantId) && r.IsActive && (r.ContractId != null || r.CustomerId != null))
            .ToListAsync(cancellationToken);

        var holders = units.Keys.Concat(employees.Keys).ToList();
        var captured = holders.Count == 0
            ? []
            : await db.ComplianceDocuments.IgnoreQueryFilters().AsNoTracking()
                .Where(d => holders.Contains(d.SubjectId))
                .Select(d => new CapturedDocument(d.Subject, d.SubjectId, d.DocumentTypeId, d.ExpiresOn))
                .ToListAsync(cancellationToken);

        return new DeployedDocuments(crews, units, employees, types.ToDictionary(t => t.Id), requirements, captured, tenants);
    }

    private sealed record CapturedDocument(DocumentSubject Subject, Guid HolderId, Guid TypeId, DateOnly? ExpiresOn);

    private sealed class DeployedDocuments(
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> crews,
        IReadOnlyDictionary<Guid, string> units,
        IReadOnlyDictionary<Guid, string> employees,
        IReadOnlyDictionary<Guid, DocumentType> types,
        IReadOnlyList<ComplianceRequirement> requirements,
        IReadOnlyList<CapturedDocument> captured,
        IReadOnlyDictionary<Guid, TenantRules> tenants)
    {
        public static DeployedDocuments Empty { get; } = new(
            new Dictionary<Guid, IReadOnlyList<Guid>>(), new Dictionary<Guid, string>(), new Dictionary<Guid, string>(), new Dictionary<Guid, DocumentType>(), [], [],
            new Dictionary<Guid, TenantRules>());

        private readonly ILookup<(Guid Holder, Guid Type), CapturedDocument> _byHolderAndType = captured.ToLookup(d => (d.HolderId, d.TypeId));

        public (int Employees, int Units, IReadOnlyList<DocumentCell> Cells) ForOrders(IReadOnlyList<WorkOrder> orders)
        {
            var cells = new Dictionary<(Guid Holder, Guid Type), DocumentCell>();
            var deployedUnits = new HashSet<Guid>();
            var deployedEmployees = new HashSet<Guid>();

            foreach (var order in orders)
            {
                var rules = tenants.TryGetValue(order.TenantId, out var found) ? found : new TenantRules(BusinessCalendar.Resolve(null), DateOnly.FromDateTime(DateTime.UtcNow), 30);
                var unitTypes = RequiredTypes(order, DocumentSubject.Unit);
                var employeeTypes = RequiredTypes(order, DocumentSubject.Employee);
                var people = order.EmployeeIds.Concat(order.CrewId is { } crew && crews.TryGetValue(crew, out var members) ? members : []).Distinct();

                foreach (var unit in order.UnitIds.Distinct().Where(units.ContainsKey))
                {
                    deployedUnits.Add(unit);
                    AddCells(cells, unit, units[unit], unitTypes, rules);
                }

                foreach (var employee in people.Where(employees.ContainsKey))
                {
                    deployedEmployees.Add(employee);
                    AddCells(cells, employee, employees[employee], employeeTypes, rules);
                }
            }

            return (deployedEmployees.Count, deployedUnits.Count, cells.Values.ToList());
        }

        private List<DocumentType> RequiredTypes(WorkOrder order, DocumentSubject subject)
        {
            var demanded = requirements
                .Where(r => r.TenantId == order.TenantId && (r.ContractId is { } contract ? contract == order.ContractId : r.CustomerId == order.CustomerId))
                .SelectMany(r => subject == DocumentSubject.Unit ? r.UnitDocumentTypeIds : r.EmployeeDocumentTypeIds)
                .ToHashSet();

            return types.Values
                .Where(t => t.TenantId == order.TenantId && t.AppliesTo == subject && (t.IsMandatory || demanded.Contains(t.Id)))
                .ToList();
        }

        private void AddCells(Dictionary<(Guid, Guid), DocumentCell> cells, Guid holder, string code, IReadOnlyList<DocumentType> required, TenantRules rules)
        {
            foreach (var type in required)
            {
                if (cells.ContainsKey((holder, type.Id)))
                {
                    continue;
                }

                var latest = _byHolderAndType[(holder, type.Id)].OrderByDescending(d => d.ExpiresOn ?? DateOnly.MaxValue).FirstOrDefault();
                cells[(holder, type.Id)] = new DocumentCell(code, type.Name, latest is not null, latest?.ExpiresOn, type.WarningDays ?? rules.WarningDays, rules.Today);
            }
        }
    }
}
