using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Notifications;

/// <summary>
/// Daily reminders of one company (runs inside its tenant scope): permits and personnel documents about to expire,
/// preventive maintenance due, overdue invoices, signed tickets without invoice, low stock and expired quotes.
/// Every reminder carries a deduplication key, so running the scan several times a day does not repeat it.
/// </summary>
public interface IDailyScanService
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}

internal sealed class DailyScanService(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock, INotificationPublisher notifications) : IDailyScanService
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        var today = clock.Today;
        var settings = tenant.Tenant.Settings;

        if (tenant.IsModuleEnabled(BrocalModules.Permits) || tenant.IsModuleEnabled(BrocalModules.Personnel))
        {
            count += await ExpiringDocumentsAsync(today, settings.ExpiryWarningDays, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Maintenance))
        {
            count += await MaintenanceDueAsync(today, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Invoicing))
        {
            count += await OverdueInvoicesAsync(today, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Tickets))
        {
            count += await UninvoicedTicketsAsync(today, settings.UninvoicedAlertDays, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Inventory))
        {
            count += await LowStockAsync(today, cancellationToken);
        }

        if (tenant.IsModuleEnabled(BrocalModules.Quotes))
        {
            var expired = await db.Quotes.Where(q => (q.Status == QuoteStatus.Draft || q.Status == QuoteStatus.Sent) && q.ValidUntil < today).ToListAsync(cancellationToken);
            expired.ForEach(q => q.Expire());
        }

        await db.SaveChangesAsync(cancellationToken);
        return count;
    }

    private async Task<int> ExpiringDocumentsAsync(DateOnly today, int warningDays, CancellationToken cancellationToken)
    {
        var horizon = today.AddDays(warningDays);
        var documents = await db.ComplianceDocuments.AsNoTracking()
            .Where(d => d.ExpiresOn != null && d.ExpiresOn <= horizon && d.ExpiresOn >= today.AddDays(-30))
            .ToListAsync(cancellationToken);

        if (documents.Count == 0)
        {
            return 0;
        }

        var types = await CatalogNames.LoadAsync<DocumentType>(db, documents.Select(d => d.DocumentTypeId), cancellationToken);
        var units = await CatalogNames.LoadAsync<Unit>(db, documents.Where(d => d.Subject == DocumentSubject.Unit).Select(d => d.SubjectId), cancellationToken);
        var employees = await CatalogNames.LoadAsync<Employee>(db, documents.Where(d => d.Subject == DocumentSubject.Employee).Select(d => d.SubjectId), cancellationToken);
        var count = 0;

        foreach (var document in documents)
        {
            var isUnit = document.Subject == DocumentSubject.Unit;
            if (isUnit && !tenant.IsModuleEnabled(BrocalModules.Permits) || !isUnit && !tenant.IsModuleEnabled(BrocalModules.Personnel))
            {
                continue;
            }

            var owner = isUnit ? $"{units.CodeOf(document.SubjectId)} {units.NameOf(document.SubjectId)}" : employees.NameOf(document.SubjectId);
            var days = document.ExpiresOn!.Value.DayNumber - today.DayNumber;
            var expired = days < 0;
            // One reminder when it enters the window, one a week before and one when it expires.
            var bucket = expired ? "vencido" : days <= 7 ? "7d" : "ventana";

            await notifications.PublishAsync(new NotificationMessage(
                isUnit ? NotificationEvent.PermitExpiring : NotificationEvent.DocumentExpiring,
                expired ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                expired ? $"{types.NameOf(document.DocumentTypeId)} vencido" : $"{types.NameOf(document.DocumentTypeId)} por vencer",
                expired
                    ? $"{owner}: venció el {document.ExpiresOn:dd/MM/yyyy}."
                    : $"{owner}: vence el {document.ExpiresOn:dd/MM/yyyy} (en {days} días).",
                isUnit ? $"flota/unidades/{document.SubjectId}" : $"personal/empleados/{document.SubjectId}",
                DeduplicationKey: $"doc:{document.Id}:{document.ExpiresOn:yyyyMMdd}:{bucket}"), cancellationToken);
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
}
