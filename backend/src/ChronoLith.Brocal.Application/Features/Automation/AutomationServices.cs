using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Inventory;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Automation;

/// <summary>Permissions that open each kind of document (the workflow panel of a document needs one of them).</summary>
internal static class AutomationAccess
{
    public static readonly IReadOnlyDictionary<AutomationDocument, string[]> Read = new Dictionary<AutomationDocument, string[]>
    {
        [AutomationDocument.ServiceTicket] = [Permissions.TicketsRead, Permissions.TicketsReadOwn],
        [AutomationDocument.WorkOrder] = [Permissions.DispatchRead, Permissions.DispatchReadOwn, Permissions.DispatchManage],
        [AutomationDocument.Quote] = [Permissions.QuotesRead, Permissions.QuotesManage],
        [AutomationDocument.Invoice] = [Permissions.InvoicingRead, Permissions.InvoicingIssue, Permissions.CollectionsRead],
        [AutomationDocument.ExpenseReport] = [Permissions.ExpensesCapture, Permissions.ExpensesRead, Permissions.ExpensesApprove],
        [AutomationDocument.MaintenanceOrder] = [Permissions.MaintenanceRead, Permissions.MaintenanceManage],
        [AutomationDocument.FuelLoad] = [Permissions.FuelRead, Permissions.FuelCapture, Permissions.FuelManage],
        [AutomationDocument.InventoryMovement] = [Permissions.InventoryRead, Permissions.InventoryMove, Permissions.InventoryManage]
    };

    /// <summary>Permissions of the guarded action: who may ask for the authorization again.</summary>
    public static readonly IReadOnlyDictionary<AutomationDocument, string[]> Request = new Dictionary<AutomationDocument, string[]>
    {
        [AutomationDocument.ServiceTicket] = [Permissions.TicketsApprove, Permissions.TicketsCapture],
        [AutomationDocument.WorkOrder] = [Permissions.DispatchManage],
        [AutomationDocument.Quote] = [Permissions.QuotesManage],
        [AutomationDocument.Invoice] = [Permissions.InvoicingIssue],
        [AutomationDocument.ExpenseReport] = [Permissions.ExpensesCapture, Permissions.ExpensesApprove],
        [AutomationDocument.MaintenanceOrder] = [Permissions.MaintenanceManage]
    };
}

/// <summary>Active staff member (never a customer portal user) chosen as approver or recipient.</summary>
public sealed record Approver(Guid MembershipId, string Name, string Email, bool IsTenantAdmin);

/// <summary>
/// Turns an <see cref="Audience"/> into the active staff members of the current company: by role, by role template,
/// by permission (company administrators hold every permission) or the company administrators themselves. Roles and
/// members are read once per request.
/// </summary>
internal sealed class AudienceResolver(IApplicationDbContext db, ITenantContext tenant)
{
    private List<RoleRow>? _roles;
    private List<MemberRow>? _members;

    public async Task<IReadOnlyList<Approver>> ResolveAsync(Audience audience, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        var roles = audience.RoleIds
            .Concat(_roles!.Where(r => r.TemplateKey is not null && audience.RoleTemplates.Contains(r.TemplateKey)).Select(r => r.Id))
            .ToHashSet();

        var permissions = audience.Permissions.Where(p => tenant.Tenant.IsModuleEnabled(BrocalModules.ModuleOf(p))).ToList();
        var permissionRoles = _roles!.Where(r => r.Permissions.Any(permissions.Contains)).Select(r => r.Id).ToHashSet();

        return _members!
            .Where(m => audience.TenantAdmins && m.IsTenantAdmin
                        || m.RoleIds.Any(roles.Contains)
                        || permissions.Count > 0 && (m.IsTenantAdmin || m.RoleIds.Any(permissionRoles.Contains)))
            .Select(m => new Approver(m.Id, m.FullName, m.Email, m.IsTenantAdmin))
            .ToList();
    }

    /// <summary>Approvers of a step; when nobody matches (a role was deleted) the company administrators approve, so no request gets stuck.</summary>
    public async Task<IReadOnlyList<Approver>> ApproversAsync(WorkflowStep step, CancellationToken cancellationToken)
    {
        var approvers = await ResolveAsync(step.Approvers, cancellationToken);
        return approvers.Count > 0 ? approvers : await ResolveAsync(Audience.Empty with { TenantAdmins = true }, cancellationToken);
    }

    public async Task<IReadOnlyList<Approver>> AdministratorsAsync(CancellationToken cancellationToken) =>
        await ResolveAsync(Audience.Empty with { TenantAdmins = true }, cancellationToken);

    /// <summary>Who may decide the current step of a pending request.</summary>
    public async Task<bool> CanDecideAsync(WorkflowInstance instance, MembershipSnapshot? membership, CancellationToken cancellationToken)
    {
        if (membership is null || instance.Step is not { } step || instance.HasDecided(membership.Id))
        {
            return false;
        }

        if (!step.AllowRequester && instance.RequestedById == membership.Id)
        {
            return false;
        }

        return (await ApproversAsync(step, cancellationToken)).Any(a => a.MembershipId == membership.Id);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        _roles ??= (await db.Roles.AsNoTracking().Select(r => new { r.Id, r.TemplateKey, r.Permissions }).ToListAsync(cancellationToken))
            .Select(r => new RoleRow(r.Id, r.TemplateKey, r.Permissions))
            .ToList();

        _members ??= (await db.Memberships.AsNoTracking()
                .Where(m => m.Status == MembershipStatus.Active && m.CustomerId == null)
                .Select(m => new { m.Id, m.FullName, m.Email, m.IsTenantAdmin, m.RoleIds })
                .ToListAsync(cancellationToken))
            .Select(m => new MemberRow(m.Id, m.FullName, m.Email, m.IsTenantAdmin, m.RoleIds))
            .ToList();
    }

    private sealed record RoleRow(Guid Id, string? TemplateKey, IReadOnlyList<string> Permissions);

    private sealed record MemberRow(Guid Id, string FullName, string Email, bool IsTenantAdmin, IReadOnlyList<Guid> RoleIds);
}

/// <summary>
/// Workflows and rules that apply to the current company: global ones (unless the company switched off an optional
/// one) and its own. Workflows are tried in order: mandatory global, company, optional global (then by priority).
/// </summary>
internal sealed class AutomationDefinitions(IApplicationDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<WorkflowDefinition>> WorkflowsAsync(AutomationDocument document, CancellationToken cancellationToken)
    {
        if (!tenant.HasTenant || !tenant.IsModuleEnabled(AutomationCatalog.Of(document).Module))
        {
            return [];
        }

        var tenantId = tenant.TenantId;
        var definitions = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Document == document && d.IsEnabled && (d.TenantId == null || d.TenantId == tenantId))
            .ToListAsync(cancellationToken);
        var optedOut = await OptedOutAsync(AutomationKind.Workflow, cancellationToken);

        return definitions
            .Where(d => !(d.IsGlobal && !d.IsMandatory && optedOut.Contains(d.Id)))
            .OrderBy(d => d.IsGlobal ? d.IsMandatory ? 0 : 2 : 1)
            .ThenBy(d => d.Priority)
            .ThenBy(d => d.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>First workflow whose conditions match the facts (null: the document needs no approval).</summary>
    public async Task<WorkflowDefinition?> ApplicableWorkflowAsync(AutomationDocument document, IReadOnlyDictionary<string, object?> facts, CancellationToken cancellationToken) =>
        (await WorkflowsAsync(document, cancellationToken)).FirstOrDefault(d => RuleConditions.Matches(d.Conditions, d.Match, facts));

    public async Task<IReadOnlyList<BusinessRule>> RulesAsync(AutomationDocument document, AutomationTrigger trigger, CancellationToken cancellationToken)
    {
        if (!tenant.HasTenant || !tenant.IsModuleEnabled(AutomationCatalog.Of(document).Module))
        {
            return [];
        }

        var tenantId = tenant.TenantId;
        var rules = await db.BusinessRules.AsNoTracking()
            .Where(r => r.Document == document && r.IsEnabled && (r.TenantId == null || r.TenantId == tenantId))
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return [];
        }

        var optedOut = await OptedOutAsync(AutomationKind.Rule, cancellationToken);
        return rules
            .Where(r => r.Triggers.Contains(trigger))
            .Where(r => !(r.IsGlobal && !r.IsMandatory && optedOut.Contains(r.Id)))
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private async Task<HashSet<Guid>> OptedOutAsync(AutomationKind kind, CancellationToken cancellationToken) =>
        (await db.AutomationOptOuts.AsNoTracking().Where(o => o.Kind == kind).Select(o => o.DefinitionId).ToListAsync(cancellationToken)).ToHashSet();
}

/// <summary>
/// The data each kind of document offers to conditions (<see cref="AutomationCatalog"/>). Amounts and counts are
/// decimals, flags booleans, enum values their JSON names and references the ids of the records.
/// </summary>
internal sealed class DocumentFacts(IApplicationDbContext db, ITenantClock clock)
{
    public async Task<Dictionary<string, object?>> TicketAsync(ServiceTicket ticket, CancellationToken cancellationToken)
    {
        var people = await PeopleAsync(ticket.EmployeeIds, ticket.CrewId, cancellationToken);
        var (expiredPeople, expiredUnits) = await ExpiredAsync(people, ticket.UnitIds, ticket.ServiceDate, cancellationToken);
        var contractExpired = false;
        if (ticket.ContractId is { } contractId)
        {
            var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, cancellationToken);
            contractExpired = contract is null || !contract.IsCurrentOn(ticket.ServiceDate);
        }

        return new Dictionary<string, object?>
        {
            ["total"] = ticket.Total,
            ["subtotal"] = ticket.Subtotal,
            ["lineCount"] = (decimal)ticket.Lines.Count,
            ["manualPriceLines"] = (decimal)ticket.Lines.Count(l => l.PriceSource == PriceSource.Manual),
            ["photoCount"] = (decimal)ticket.Photos.Count,
            ["hasSignature"] = ticket.SignatureFileId is not null,
            ["durationHours"] = ticket is { StartedAt: { } start, FinishedAt: { } end } ? Math.Round((decimal)(end - start).TotalHours, 2) : null,
            ["daysSinceService"] = (decimal)(clock.Today.DayNumber - ticket.ServiceDate.DayNumber),
            ["personnelCount"] = (decimal)people.Count,
            ["unitCount"] = (decimal)ticket.UnitIds.Count,
            ["personnelWithExpiredDocuments"] = (decimal)expiredPeople,
            ["unitsWithExpiredDocuments"] = (decimal)expiredUnits,
            ["hasWorkOrder"] = ticket.WorkOrderId is not null,
            ["contractExpired"] = contractExpired,
            ["customerId"] = ticket.CustomerId,
            ["serviceTypeId"] = ticket.ServiceTypeId,
            ["siteId"] = ticket.SiteId
        };
    }

    public async Task<Dictionary<string, object?>> WorkOrderAsync(WorkOrder order, CancellationToken cancellationToken)
    {
        var people = await PeopleAsync(order.EmployeeIds, order.CrewId, cancellationToken);
        var start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(order.ScheduledStart, clock.TimeZone).DateTime);
        var (expiredPeople, expiredUnits) = await ExpiredAsync(people, order.UnitIds, start, cancellationToken);
        var unitIds = order.UnitIds.ToList();
        var notAvailable = unitIds.Count == 0
            ? 0
            : await db.Units.AsNoTracking().CountAsync(u => unitIds.Contains(u.Id) && (!u.IsActive || u.Status == UnitStatus.Workshop || u.Status == UnitStatus.OutOfService), cancellationToken);

        return new Dictionary<string, object?>
        {
            ["priority"] = Camel(order.Priority),
            ["unitCount"] = (decimal)order.UnitIds.Count,
            ["personnelCount"] = (decimal)people.Count,
            ["hasCrew"] = order.CrewId is not null,
            ["durationHours"] = Math.Round((decimal)(order.ScheduledEnd - order.ScheduledStart).TotalHours, 2),
            ["hoursUntilStart"] = Math.Round((decimal)(order.ScheduledStart - clock.UtcNow).TotalHours, 1),
            ["personnelWithExpiredDocuments"] = (decimal)expiredPeople,
            ["unitsWithExpiredDocuments"] = (decimal)expiredUnits,
            ["unitsNotAvailable"] = (decimal)notAvailable,
            ["fromQuote"] = order.QuoteId is not null,
            ["customerId"] = order.CustomerId,
            ["serviceTypeId"] = order.ServiceTypeId,
            ["siteId"] = order.SiteId
        };
    }

    public static Dictionary<string, object?> Quote(Quote quote) => new()
    {
        ["total"] = quote.Total,
        ["subtotal"] = quote.Subtotal,
        ["discount"] = quote.Discount,
        ["discountPercent"] = quote.Subtotal > 0 ? Math.Round(quote.Discount / quote.Subtotal * 100, 2) : 0m,
        ["maxLineDiscountPercent"] = quote.Lines.Count == 0 ? 0m : quote.Lines.Max(l => l.DiscountPercent),
        ["lineCount"] = (decimal)quote.Lines.Count,
        ["validityDays"] = (decimal)(quote.ValidUntil.DayNumber - quote.IssuedOn.DayNumber),
        ["currency"] = quote.Currency,
        ["hasContract"] = quote.ContractId is not null,
        ["customerId"] = quote.CustomerId
    };

    public async Task<Dictionary<string, object?>> InvoiceAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var overdue = await db.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == invoice.CustomerId && i.Status == InvoiceStatus.Stamped && i.Balance > 0 && i.DueOn < today)
            .SumAsync(i => (decimal?)i.Balance, cancellationToken) ?? 0;

        return new Dictionary<string, object?>
        {
            ["total"] = invoice.Total,
            ["subtotal"] = invoice.Subtotal,
            ["paymentMethod"] = invoice.PaymentMethod,
            ["currency"] = invoice.Currency,
            ["ticketCount"] = (decimal)invoice.TicketIds.Count,
            ["lineCount"] = (decimal)invoice.Lines.Count,
            ["creditDays"] = (decimal)(invoice.DueOn.DayNumber - invoice.IssueDate.DayNumber),
            ["hasCopade"] = !string.IsNullOrWhiteSpace(invoice.CopadeNumber),
            ["isManual"] = invoice.TicketIds.Count == 0 && invoice.EstimateId is null && invoice.TripId is null,
            ["customerOverdueBalance"] = overdue,
            ["customerId"] = invoice.CustomerId
        };
    }

    public static Dictionary<string, object?> Expense(ExpenseReport report) => new()
    {
        ["total"] = report.Total,
        ["advanceAmount"] = report.AdvanceAmount,
        ["balance"] = report.Balance,
        ["itemCount"] = (decimal)report.Items.Count,
        ["itemsWithoutInvoice"] = (decimal)report.Items.Count(i => !i.HasInvoice),
        ["amountWithoutInvoice"] = report.Items.Where(i => !i.HasInvoice).Sum(i => i.Amount),
        ["itemsWithoutReceipt"] = (decimal)report.Items.Count(i => i.ReceiptFileId is null),
        ["maxItemAmount"] = report.Items.Count == 0 ? 0m : report.Items.Max(i => i.Amount),
        ["periodDays"] = (decimal)(report.PeriodEnd.DayNumber - report.PeriodStart.DayNumber + 1),
        ["hasWorkOrder"] = report.WorkOrderId is not null,
        ["employeeId"] = report.EmployeeId
    };

    public Dictionary<string, object?> Maintenance(MaintenanceOrder order) => new()
    {
        ["totalCost"] = order.TotalCost,
        ["partsCost"] = order.PartsCost,
        ["laborCost"] = order.LaborCost,
        ["externalCost"] = order.ExternalCost,
        ["type"] = Camel(order.Type),
        ["pendingTasks"] = (decimal)order.Tasks.Count(t => !t.Done),
        ["downtimeHours"] = Math.Round((decimal)order.DowntimeHours(clock.UtcNow), 1),
        ["unitId"] = order.UnitId
    };

    public static Dictionary<string, object?> Fuel(FuelLoad load) => new()
    {
        ["liters"] = load.Liters,
        ["amount"] = load.Amount,
        ["pricePerLiter"] = load.PricePerLiter,
        ["isFullTank"] = load.IsFullTank,
        ["hasReceipt"] = load.ReceiptFileId is not null,
        ["deviationPercent"] = load.DeviationPercent,
        ["isAnomaly"] = load.IsAnomaly,
        ["paymentMethod"] = Camel(load.PaymentMethod),
        ["unitId"] = load.UnitId
    };

    public static Dictionary<string, object?> Movement(InventoryMovement movement, decimal stockAfter, decimal onHandAfter, decimal minStock) => new()
    {
        ["type"] = Camel(movement.Type),
        ["quantity"] = movement.Quantity,
        ["totalCost"] = movement.TotalCost,
        ["stockAfter"] = stockAfter,
        ["belowMinimum"] = minStock > 0 && onHandAfter < minStock,
        ["itemId"] = movement.ItemId,
        ["warehouseId"] = movement.WarehouseId
    };

    public static string Camel(Enum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// <summary>Employees of the document plus the members of its crew.</summary>
    private async Task<IReadOnlyCollection<Guid>> PeopleAsync(IReadOnlyList<Guid> employeeIds, Guid? crewId, CancellationToken cancellationToken)
    {
        var people = employeeIds.ToHashSet();
        if (crewId is { } id)
        {
            var crew = await db.Crews.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            people.UnionWith(crew?.MemberIds ?? []);
        }

        return people;
    }

    /// <summary>
    /// People and units with an expired document on a date: for every document type the latest document counts
    /// (a renewed permit replaces the expired one); documents of inactive types are ignored.
    /// </summary>
    private async Task<(int People, int Units)> ExpiredAsync(IReadOnlyCollection<Guid> employeeIds, IReadOnlyList<Guid> unitIds, DateOnly on, CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0 && unitIds.Count == 0)
        {
            return (0, 0);
        }

        var people = employeeIds.ToList();
        var units = unitIds.ToList();
        var activeTypes = db.DocumentTypes.Where(t => t.IsActive).Select(t => t.Id);
        var documents = await db.ComplianceDocuments.AsNoTracking()
            .Where(d => d.Subject == DocumentSubject.Employee && people.Contains(d.SubjectId) || d.Subject == DocumentSubject.Unit && units.Contains(d.SubjectId))
            .Where(d => activeTypes.Contains(d.DocumentTypeId))
            .Select(d => new { d.Subject, d.SubjectId, d.DocumentTypeId, d.ExpiresOn })
            .ToListAsync(cancellationToken);

        var expired = documents
            .GroupBy(d => (d.Subject, d.SubjectId, d.DocumentTypeId))
            .Where(g => g.Max(d => d.ExpiresOn ?? DateOnly.MaxValue) < on)
            .Select(g => (g.Key.Subject, g.Key.SubjectId))
            .Distinct()
            .ToList();

        return (expired.Count(e => e.Subject == DocumentSubject.Employee), expired.Count(e => e.Subject == DocumentSubject.Unit));
    }
}
