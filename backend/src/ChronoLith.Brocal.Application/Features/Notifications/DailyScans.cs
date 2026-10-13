using System.Globalization;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Automation;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Hse;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Notifications;

/// <summary>Notices produced by the reviews of one company, by kind (<c>documents</c>, <c>contracts</c>…).</summary>
public sealed record DailyScanResult(IReadOnlyDictionary<string, int> Counts)
{
    public int Total => Counts.Values.Sum();
}

/// <summary>
/// Scheduled reviews of one company (runs inside its tenant scope): permits, personnel documents, driver licenses,
/// contracts, price lists, corrective actions and API keys about to expire; mandatory documents missing; preventive
/// maintenance due, overdue invoices, signed tickets without invoice, low stock, expired quotes and approval requests past their deadline. Every reminder
/// carries a deduplication key, so running the review several times a day does not repeat it.
/// </summary>
public interface IDailyScanService
{
    Task<DailyScanResult> RunAsync(CancellationToken cancellationToken);
}

internal sealed class DailyScanService(
    IApplicationDbContext db,
    ITenantContext tenant,
    ITenantClock clock,
    INotificationPublisher notifications,
    IWorkflowEngine workflows,
    WorkflowReminders reminders) : IDailyScanService
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>Notice of contracts and price lists when the notification rule does not set its own anticipation.</summary>
    private const int DefaultContractWindow = 60;

    /// <summary>Corrective actions are reminded this many days before their due date.</summary>
    private const int CorrectiveActionWindow = 3;

    private const int ApiKeyWindow = 15;

    public async Task<DailyScanResult> RunAsync(CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>();
        var today = clock.Today;
        var settings = tenant.Tenant.Settings;
        var windows = await db.NotificationRules.AsNoTracking()
            .Where(r => r.DaysBefore != null)
            .ToDictionaryAsync(r => r.Event, r => r.DaysBefore!.Value, cancellationToken);

        if (tenant.IsModuleEnabled(BrocalModules.Permits) || tenant.IsModuleEnabled(BrocalModules.Personnel))
        {
            counts["documents"] = await ExpiringDocumentsAsync(today, settings.ExpiryWarningDays, windows, cancellationToken);
            counts["missingDocuments"] = await MissingDocumentsAsync(today, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Personnel))
        {
            counts["licenses"] = await DriverLicensesAsync(today, windows.GetValueOrDefault(NotificationEvent.DocumentExpiring, settings.ExpiryWarningDays), cancellationToken);
        }

        counts["contracts"] = await ContractsAsync(today, windows.GetValueOrDefault(NotificationEvent.ContractExpiring, DefaultContractWindow), cancellationToken);

        if (tenant.IsModuleEnabled(BrocalModules.Pricing))
        {
            counts["priceLists"] = await PriceListsAsync(today, windows.GetValueOrDefault(NotificationEvent.ContractExpiring, DefaultContractWindow), cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Hse))
        {
            counts["correctiveActions"] = await CorrectiveActionsAsync(today, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Integrations))
        {
            counts["apiKeys"] = await ApiKeysAsync(today, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Maintenance))
        {
            counts["maintenance"] = await MaintenanceDueAsync(today, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Invoicing))
        {
            counts["overdueInvoices"] = await OverdueInvoicesAsync(today, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Tickets))
        {
            counts["uninvoicedTickets"] = await UninvoicedTicketsAsync(today, settings.UninvoicedAlertDays, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Inventory))
        {
            counts["lowStock"] = await LowStockAsync(today, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Quotes))
        {
            counts["expiredQuotes"] = await ExpireQuotesAsync(today, cancellationToken);
        }

        counts["approvalReminders"] = await reminders.RemindAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return new DailyScanResult(counts.Where(c => c.Value > 0).ToDictionary());
    }

    /// <summary>
    /// Permits of units and documents of employees: only the latest document of each type counts (a renewal replaces
    /// the expired one) and only for active units and employees. The anticipation is the one of the document type,
    /// else the one of the notification rule, else the one of the company.
    /// </summary>
    private async Task<int> ExpiringDocumentsAsync(DateOnly today, int companyWindow, IReadOnlyDictionary<NotificationEvent, int> windows, CancellationToken cancellationToken)
    {
        var types = await db.DocumentTypes.AsNoTracking().Where(t => t.IsActive).ToDictionaryAsync(t => t.Id, cancellationToken);
        var horizon = today.AddDays(Math.Max(365, companyWindow));
        var candidates = await db.ComplianceDocuments.AsNoTracking()
            .Where(d => d.ExpiresOn != null && d.ExpiresOn <= horizon && d.ExpiresOn >= today.AddDays(-30))
            .Select(d => new { d.Id, d.Subject, d.SubjectId, d.DocumentTypeId, d.ExpiresOn })
            .ToListAsync(cancellationToken);
        candidates = candidates.Where(d => types.ContainsKey(d.DocumentTypeId)).ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Latest document of each subject and type (a document without expiry never expires).
        var subjectIds = candidates.Select(d => d.SubjectId).Distinct().ToList();
        var latest = (await db.ComplianceDocuments.AsNoTracking()
                .Where(d => subjectIds.Contains(d.SubjectId))
                .Select(d => new { d.Id, d.SubjectId, d.DocumentTypeId, d.ExpiresOn })
                .ToListAsync(cancellationToken))
            .GroupBy(d => (d.SubjectId, d.DocumentTypeId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.ExpiresOn ?? DateOnly.MaxValue).First().Id);

        var units = await db.Units.AsNoTracking().Where(u => subjectIds.Contains(u.Id) && u.IsActive).ToDictionaryAsync(u => u.Id, cancellationToken);
        var employees = await db.Employees.AsNoTracking().Where(e => subjectIds.Contains(e.Id) && e.IsActive).ToDictionaryAsync(e => e.Id, cancellationToken);
        var owners = await OwnersAsync(employees.Keys, cancellationToken);
        var count = 0;

        foreach (var document in candidates.Where(d => latest.GetValueOrDefault((d.SubjectId, d.DocumentTypeId)) == d.Id))
        {
            var isUnit = document.Subject == DocumentSubject.Unit;
            if (isUnit && (!tenant.IsModuleEnabled(BrocalModules.Permits) || !units.ContainsKey(document.SubjectId)) ||
                !isUnit && (!tenant.IsModuleEnabled(BrocalModules.Personnel) || !employees.ContainsKey(document.SubjectId)))
            {
                continue;
            }

            var type = types[document.DocumentTypeId];
            var @event = isUnit ? NotificationEvent.PermitExpiring : NotificationEvent.DocumentExpiring;
            var window = type.WarningDays ?? windows.GetValueOrDefault(@event, companyWindow);
            var days = document.ExpiresOn!.Value.DayNumber - today.DayNumber;
            if (days > window)
            {
                continue;
            }

            var owner = isUnit ? $"{units[document.SubjectId].Code} {units[document.SubjectId].Name}" : employees[document.SubjectId].Name;
            await notifications.PublishAsync(new NotificationMessage(
                @event,
                days < 0 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                days < 0 ? $"{type.Name} vencido" : $"{type.Name} por vencer",
                ExpiryText(owner, document.ExpiresOn.Value, days),
                isUnit ? $"flota/unidades/{document.SubjectId}" : $"personal/empleados/{document.SubjectId}",
                isUnit ? null : owners.GetValueOrDefault(document.SubjectId),
                DeduplicationKey: $"doc:{document.Id}:{document.ExpiresOn:yyyyMMdd}:{Bucket(days)}"), cancellationToken);
            count++;
        }

        return count;
    }

    /// <summary>Federal driver license of the employee record (needed by Carta Porte and the tanker operators).</summary>
    private async Task<int> DriverLicensesAsync(DateOnly today, int window, CancellationToken cancellationToken)
    {
        var horizon = today.AddDays(window);
        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive && e.LicenseExpiresOn != null && e.LicenseExpiresOn <= horizon && e.LicenseExpiresOn >= today.AddDays(-30))
            .ToListAsync(cancellationToken);
        var owners = await OwnersAsync(employees.Select(e => e.Id), cancellationToken);

        foreach (var employee in employees)
        {
            var expires = employee.LicenseExpiresOn!.Value;
            var days = expires.DayNumber - today.DayNumber;
            var license = string.Join(' ', new[] { employee.LicenseType, employee.LicenseNumber }.Where(s => !string.IsNullOrWhiteSpace(s)));
            await notifications.PublishAsync(new NotificationMessage(
                NotificationEvent.DocumentExpiring,
                days < 0 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                days < 0 ? "Licencia federal vencida" : "Licencia federal por vencer",
                ExpiryText(license.Length == 0 ? employee.Name : $"{employee.Name} (licencia {license})", expires, days),
                $"personal/empleados/{employee.Id}",
                owners.GetValueOrDefault(employee.Id),
                DeduplicationKey: $"license:{employee.Id}:{expires:yyyyMMdd}:{Bucket(days)}"), cancellationToken);
        }

        return employees.Count;
    }

    private async Task<int> ContractsAsync(DateOnly today, int window, CancellationToken cancellationToken)
    {
        var horizon = today.AddDays(window);
        var contracts = await db.Contracts.AsNoTracking()
            .Where(c => c.IsActive && c.Status == ContractStatus.Active && c.EndDate != null && c.EndDate <= horizon && c.EndDate >= today.AddDays(-30))
            .Select(c => new { c.Id, c.Code, c.Name, c.EndDate, c.CustomerId, Customer = db.Customers.Where(x => x.Id == c.CustomerId).Select(x => x.Name).FirstOrDefault() })
            .ToListAsync(cancellationToken);

        foreach (var contract in contracts)
        {
            var days = contract.EndDate!.Value.DayNumber - today.DayNumber;
            await notifications.PublishAsync(new NotificationMessage(
                NotificationEvent.ContractExpiring,
                days < 0 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                days < 0 ? $"Contrato {contract.Code} vencido" : $"Contrato {contract.Code} por vencer",
                ExpiryText($"{contract.Name} · {contract.Customer}", contract.EndDate.Value, days) + (days < 0 ? " Sigue activo: renuévalo o ciérralo." : string.Empty),
                $"catalogos/contratos/{contract.Id}",
                DeduplicationKey: $"contract:{contract.Id}:{contract.EndDate:yyyyMMdd}:{Bucket(days)}"), cancellationToken);
        }

        return contracts.Count;
    }

    private async Task<int> PriceListsAsync(DateOnly today, int window, CancellationToken cancellationToken)
    {
        var horizon = today.AddDays(window);
        var lists = await db.PriceLists.AsNoTracking()
            .Where(l => l.IsActive && l.ValidTo != null && l.ValidTo <= horizon && l.ValidTo >= today.AddDays(-30))
            .Select(l => new { l.Id, l.Code, l.Name, l.ValidTo })
            .ToListAsync(cancellationToken);

        foreach (var list in lists)
        {
            var days = list.ValidTo!.Value.DayNumber - today.DayNumber;
            await notifications.PublishAsync(new NotificationMessage(
                NotificationEvent.ContractExpiring,
                days < 0 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                days < 0 ? $"Tarifario {list.Code} vencido" : $"Tarifario {list.Code} por vencer",
                ExpiryText(list.Name, list.ValidTo.Value, days) + (days < 0 ? " Las boletas ya no toman sus precios." : string.Empty),
                $"tarifas/listas/{list.Id}",
                DeduplicationKey: $"pricelist:{list.Id}:{list.ValidTo:yyyyMMdd}:{Bucket(days)}"), cancellationToken);
        }

        return lists.Count;
    }

    /// <summary>Corrective and preventive actions of open incidents close to (or past) their due date.</summary>
    private async Task<int> CorrectiveActionsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var horizon = today.AddDays(CorrectiveActionWindow);
        var incidents = await db.Incidents.AsNoTracking().Where(i => i.Status != IncidentStatus.Closed).ToListAsync(cancellationToken);
        var count = 0;

        foreach (var incident in incidents)
        {
            for (var index = 0; index < incident.Actions.Count; index++)
            {
                var action = incident.Actions[index];
                if (action.Status == ActionStatus.Done || action.DueOn is not { } due || due > horizon)
                {
                    continue;
                }

                var days = due.DayNumber - today.DayNumber;
                await notifications.PublishAsync(new NotificationMessage(
                    NotificationEvent.CorrectiveActionDue,
                    days < 0 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                    days < 0 ? $"Acción correctiva vencida · {incident.Folio}" : $"Acción correctiva por vencer · {incident.Folio}",
                    $"{action.Description}{(action.Responsible is null ? string.Empty : $" (responsable: {action.Responsible})")}\n" + ExpiryText("Compromiso", due, days),
                    $"hse/incidentes/{incident.Id}",
                    // Weekly while it stays overdue.
                    DeduplicationKey: $"capa:{incident.Id}:{index}:{due:yyyyMMdd}:{(days < 0 ? $"w{today.DayNumber / 7}" : "due")}"), cancellationToken);
                count++;
            }
        }

        return count;
    }

    private async Task<int> ApiKeysAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var horizon = today.AddDays(ApiKeyWindow);
        var keys = await db.ApiKeys.AsNoTracking()
            .Where(k => !k.IsRevoked && k.ExpiresOn != null && k.ExpiresOn <= horizon && k.ExpiresOn >= today.AddDays(-7))
            .ToListAsync(cancellationToken);

        foreach (var key in keys)
        {
            var days = key.ExpiresOn!.Value.DayNumber - today.DayNumber;
            await notifications.PublishAsync(new NotificationMessage(
                NotificationEvent.ApiKeyExpiring,
                days < 0 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                days < 0 ? $"Llave de API '{key.Name}' vencida" : $"Llave de API '{key.Name}' por vencer",
                ExpiryText($"La integración que usa {key.Prefix}…", key.ExpiresOn.Value, days) + " Crea una nueva llave y actualiza el sistema externo.",
                "configuracion/integraciones",
                DeduplicationKey: $"apikey:{key.Id}:{key.ExpiresOn:yyyyMMdd}:{Bucket(days)}"), cancellationToken);
        }

        return keys.Count;
    }

    /// <summary>Weekly summary of the mandatory documents not captured for active units and employees.</summary>
    private async Task<int> MissingDocumentsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var mandatory = await db.DocumentTypes.AsNoTracking().Where(t => t.IsActive && t.IsMandatory).ToListAsync(cancellationToken);
        if (mandatory.Count == 0)
        {
            return 0;
        }

        var week = ISOWeek.GetYear(today.ToDateTime(TimeOnly.MinValue)) * 100 + ISOWeek.GetWeekOfYear(today.ToDateTime(TimeOnly.MinValue));
        var count = 0;
        foreach (var subject in new[] { DocumentSubject.Unit, DocumentSubject.Employee })
        {
            if (subject == DocumentSubject.Unit && !tenant.IsModuleEnabled(BrocalModules.Permits) || subject == DocumentSubject.Employee && !tenant.IsModuleEnabled(BrocalModules.Personnel))
            {
                continue;
            }

            var types = mandatory.Where(t => t.AppliesTo == subject).ToList();
            if (types.Count == 0)
            {
                continue;
            }

            var subjects = subject == DocumentSubject.Unit
                ? await db.Units.AsNoTracking().Where(u => u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken)
                : await db.Employees.AsNoTracking().Where(e => e.IsActive).Select(e => e.Id).ToListAsync(cancellationToken);
            var typeIds = types.Select(t => t.Id).ToList();
            var captured = (await db.ComplianceDocuments.AsNoTracking()
                    .Where(d => d.Subject == subject && typeIds.Contains(d.DocumentTypeId))
                    .Select(d => new { d.SubjectId, d.DocumentTypeId })
                    .ToListAsync(cancellationToken))
                .Select(d => (d.SubjectId, d.DocumentTypeId))
                .ToHashSet();

            var missing = types
                .Select(t => (Type: t, Missing: subjects.Count(s => !captured.Contains((s, t.Id)))))
                .Where(m => m.Missing > 0)
                .ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            var isUnit = subject == DocumentSubject.Unit;
            var total = missing.Sum(m => m.Missing);
            await notifications.PublishAsync(new NotificationMessage(
                NotificationEvent.DocumentMissing,
                NotificationSeverity.Warning,
                isUnit ? $"{total} permisos obligatorios sin capturar" : $"{total} documentos obligatorios del personal sin capturar",
                string.Join("\n", missing.Take(10).Select(m => $"{m.Type.Name}: {m.Missing} {(isUnit ? "unidad(es)" : "empleado(s)")}")),
                isUnit ? "permisos/matriz" : "personal/matriz",
                DeduplicationKey: $"missing:{subject}:{week}"), cancellationToken);
            count++;
        }

        return count;
    }

    private async Task<int> MaintenanceDueAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var plans = await db.MaintenancePlans.AsNoTracking().Include(p => p.Units).Where(p => p.IsActive).ToListAsync(cancellationToken);
        var unitIds = plans.SelectMany(p => p.Units.Select(u => u.UnitId)).Distinct().ToList();
        var units = await db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id) && u.IsActive).ToDictionaryAsync(u => u.Id, cancellationToken);
        var count = 0;

        foreach (var plan in plans)
        {
            foreach (var tracking in plan.Units)
            {
                if (!units.TryGetValue(tracking.UnitId, out var unit) || plan.DueFor(unit, today) is not { IsDue: true } due)
                {
                    continue;
                }

                var remaining = plan.Trigger switch
                {
                    MaintenanceTrigger.Kilometers => $"{due.Remaining:N0} km",
                    MaintenanceTrigger.Hours => $"{due.Remaining:N0} h",
                    _ => $"{due.Remaining:N0} días"
                };

                await notifications.PublishAsync(new NotificationMessage(
                    NotificationEvent.MaintenanceDue,
                    due.IsOverdue ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                    $"Mantenimiento {(due.IsOverdue ? "vencido" : "próximo")}: {unit.Code}",
                    $"{plan.Name} para {unit.Code} {unit.Name}. Restante: {remaining}.",
                    $"mantenimiento/programas/{plan.Id}",
                    DeduplicationKey: $"maint:{plan.Id}:{unit.Id}:{tracking.LastServiceOn:yyyyMMdd}:{(due.IsOverdue ? "over" : "due")}"), cancellationToken);
                count++;
            }
        }

        return count;
    }

    private async Task<int> OverdueInvoicesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var overdue = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Stamped && i.Balance > 0 && i.DueOn < today)
            .Select(i => new { i.Id, i.Series, i.Folio, i.CustomerId, i.Balance, i.DueOn, CustomerName = db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.Name).FirstOrDefault() })
            .ToListAsync(cancellationToken);

        // Weekly reminder per invoice.
        var week = today.DayNumber / 7;
        foreach (var invoice in overdue)
        {
            await notifications.PublishAsync(new NotificationMessage(
                NotificationEvent.InvoiceOverdue,
                NotificationSeverity.Warning,
                $"Factura {invoice.Series}-{invoice.Folio} vencida",
                $"{invoice.CustomerName}: saldo de {invoice.Balance:N2} vencido desde el {invoice.DueOn:dd/MM/yyyy} ({today.DayNumber - invoice.DueOn.DayNumber} días).",
                $"cobranza/facturas/{invoice.Id}",
                CustomerId: invoice.CustomerId,
                DeduplicationKey: $"overdue:{invoice.Id}:{week}"), cancellationToken);
        }

        return overdue.Count;
    }

    private async Task<int> UninvoicedTicketsAsync(DateOnly today, int alertDays, CancellationToken cancellationToken)
    {
        var limit = today.AddDays(-alertDays);
        var pending = await db.Tickets.AsNoTracking()
            .Where(t => (t.Status == TicketStatus.Signed || t.Status == TicketStatus.Approved) && t.ServiceDate <= limit)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Amount = g.Sum(t => t.Total) })
            .FirstOrDefaultAsync(cancellationToken);

        if (pending is null || pending.Count == 0)
        {
            return 0;
        }

        await notifications.PublishAsync(new NotificationMessage(
            NotificationEvent.UninvoicedTickets,
            NotificationSeverity.Warning,
            $"{pending.Count} boletas sin facturar",
            $"Hay {pending.Count} boletas firmadas con más de {alertDays} días sin factura por {pending.Amount:N2}.",
            "facturacion/tablero",
            DeduplicationKey: $"uninvoiced:{today:yyyyMMdd}"), cancellationToken);
        return 1;
    }

    private async Task<int> LowStockAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var levels = await db.StockLevels.AsNoTracking().GroupBy(s => s.ItemId).Select(g => new { ItemId = g.Key, Quantity = g.Sum(s => s.Quantity) }).ToListAsync(cancellationToken);
        var stock = levels.ToDictionary(l => l.ItemId, l => l.Quantity);
        var items = await db.InventoryItems.AsNoTracking().Where(i => i.IsActive && i.MinStock > 0).ToListAsync(cancellationToken);
        var low = items.Where(i => stock.GetValueOrDefault(i.Id) < i.MinStock).ToList();
        if (low.Count == 0)
        {
            return 0;
        }

        await notifications.PublishAsync(new NotificationMessage(
            NotificationEvent.LowStock,
            NotificationSeverity.Warning,
            $"{low.Count} artículos bajo el mínimo",
            string.Join("\n", low.Take(10).Select(i => $"{i.Code} {i.Name}: {stock.GetValueOrDefault(i.Id):0.##} (mínimo {i.MinStock:0.##})")),
            "inventario/existencias",
            DeduplicationKey: $"lowstock:{today:yyyyMMdd}"), cancellationToken);
        return 1;
    }

    /// <summary>Quotes past their validity expire; their pending approval requests are closed.</summary>
    private async Task<int> ExpireQuotesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var expired = await db.Quotes.Where(q => (q.Status == QuoteStatus.Draft || q.Status == QuoteStatus.Sent) && q.ValidUntil < today).ToListAsync(cancellationToken);
        foreach (var quote in expired)
        {
            quote.Expire();
            await workflows.CancelPendingAsync(AutomationDocument.Quote, quote.Id, "La cotización venció.", cancellationToken);
        }

        return expired.Count;
    }

    /// <summary>Membership of each employee (they hear about their own documents).</summary>
    private async Task<Dictionary<Guid, IReadOnlyCollection<Guid>>> OwnersAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return (await db.Memberships.AsNoTracking()
                .Where(m => m.EmployeeId != null && ids.Contains(m.EmployeeId.Value) && m.Status == MembershipStatus.Active)
                .Select(m => new { m.Id, EmployeeId = m.EmployeeId!.Value })
                .ToListAsync(cancellationToken))
            .GroupBy(m => m.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<Guid>)g.Select(m => m.Id).ToList());
    }

    /// <summary>One reminder when it enters the window, one a week before and one when it expires.</summary>
    private static string Bucket(int days) => days < 0 ? "vencido" : days <= 7 ? "7d" : "ventana";

    private static string ExpiryText(string owner, DateOnly expires, int days) =>
        days < 0
            ? $"{owner}: venció el {expires.ToString("dd/MM/yyyy", Mx)}."
            : days == 0
                ? $"{owner}: vence hoy."
                : $"{owner}: vence el {expires.ToString("dd/MM/yyyy", Mx)} (en {days} días).";
}
