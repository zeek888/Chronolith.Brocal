using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Features.Dispatch;
using ChronoLith.Brocal.Application.Features.Invoicing;
using ChronoLith.Brocal.Application.Features.People;
using ChronoLith.Brocal.Application.Features.Tickets;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Automation;

/// <summary>A document with workflows, read for its approval panel or to ask for the authorization again.</summary>
public sealed record DocumentSnapshot(AutomationDocument Document, Guid Id, string Reference, IReadOnlyDictionary<string, object?> Facts, bool AtGate);

/// <summary>Loads the documents that can carry workflows (with the visibility rules of each one, unless the system evaluates them).</summary>
internal sealed class DocumentLoader(IApplicationDbContext db, ITenantContext tenant, DocumentFacts facts)
{
    public async Task<DocumentSnapshot?> LoadAsync(AutomationDocument document, Guid id, CancellationToken cancellationToken, bool applyVisibility = true)
    {
        switch (document)
        {
            case AutomationDocument.ServiceTicket:
            {
                var ticket = await (applyVisibility ? db.Tickets.AsNoTracking().Visible(tenant) : db.Tickets.AsNoTracking()).Include(t => t.Lines).Include(t => t.Photos).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
                return ticket is null ? null : new(document, id, ticket.Folio, await facts.TicketAsync(ticket, cancellationToken), ticket.Status == TicketStatus.Signed);
            }

            case AutomationDocument.WorkOrder:
            {
                var order = await (applyVisibility ? db.WorkOrders.AsNoTracking().Visible(db, tenant) : db.WorkOrders.AsNoTracking()).FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
                return order is null ? null : new(document, id, order.Folio, await facts.WorkOrderAsync(order, cancellationToken), order.Status == WorkOrderStatus.Scheduled);
            }

            case AutomationDocument.Quote:
            {
                var quote = await db.Quotes.AsNoTracking().Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
                return quote is null ? null : new(document, id, quote.Folio, DocumentFacts.Quote(quote), quote.Status == QuoteStatus.Draft);
            }

            case AutomationDocument.Invoice:
            {
                var invoice = await (applyVisibility ? db.Invoices.AsNoTracking().Visible(tenant) : db.Invoices.AsNoTracking()).Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
                return invoice is null ? null : new(document, id, InvoiceReference(invoice), await facts.InvoiceAsync(invoice, cancellationToken), invoice.Status == InvoiceStatus.Draft);
            }

            case AutomationDocument.ExpenseReport:
            {
                var report = await (applyVisibility ? db.ExpenseReports.AsNoTracking().Visible(tenant) : db.ExpenseReports.AsNoTracking()).Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
                return report is null ? null : new(document, id, report.Folio, DocumentFacts.Expense(report), report.Status == ExpenseStatus.Submitted);
            }

            case AutomationDocument.MaintenanceOrder:
            {
                var order = await db.MaintenanceOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
                return order is null ? null : new(document, id, order.Folio, facts.Maintenance(order), order.IsOpen);
            }

            default:
                return null;
        }
    }

    /// <summary>Drafts have no folio yet: the series, the customer and the amount identify them.</summary>
    public static string InvoiceReference(Invoice invoice)
    {
        var reference = invoice.Folio is null ? $"{invoice.Series} (borrador) {invoice.Receiver.Name} {invoice.Total:N2}" : invoice.AuditReference;
        return reference.Length > 180 ? reference[..180] : reference;
    }
}
