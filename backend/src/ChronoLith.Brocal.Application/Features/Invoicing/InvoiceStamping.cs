using System.Globalization;
using System.Text;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Automation;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Invoicing;

/// <summary>
/// Stamps a draft invoice: takes the folio of its series, builds the CFDI, calls the PAC of the company, stores the XML
/// and the printed representation (PDF), marks the tickets as invoiced and notifies. Runs inside a transaction: if the
/// PAC rejects it, nothing changes and the folio is not consumed.
/// </summary>
internal sealed class InvoiceStamping(
    IApplicationDbContext db,
    ITenantContext tenant,
    IFolioService folios,
    IPacGateway pac,
    IFileService files,
    InvoicePdfService pdf,
    INotificationPublisher notifications,
    IAuditLog audit,
    ITenantClock clock,
    InvoiceAuthorization authorization)
{
    public async Task<Result<Invoice>> StampAsync(Invoice invoice, CfdiCartaPorte? cartaPorte, CancellationToken cancellationToken)
    {
        var fiscal = tenant.Tenant.Fiscal;
        if (!fiscal.IsComplete)
        {
            return InvoiceErrors.IssuerIncomplete;
        }

        if (string.IsNullOrWhiteSpace(invoice.Receiver.TaxRegime) || string.IsNullOrWhiteSpace(invoice.Receiver.PostalCode))
        {
            return InvoiceErrors.IncompleteReceiver;
        }

        // With their lines: a draft stamped on its own (not right after creating it) loads its tickets here.
        var tickets = await db.Tickets.Include(t => t.Lines).Where(t => invoice.TicketIds.Contains(t.Id)).ToListAsync(cancellationToken);
        foreach (var ticket in tickets)
        {
            var invoiceable = ticket.EnsureInvoiceable(tenant.Tenant.Settings.RequireApprovalBeforeInvoice);
            if (invoiceable.IsFailure)
            {
                return Error.Conflict(invoiceable.Error.Code, $"{ticket.Folio}: {invoiceable.Error.Description}");
            }
        }

        var released = await authorization.EnsureTicketsReleasedAsync(tickets, cancellationToken);
        if (released.IsFailure)
        {
            return released.Error;
        }

        var folio = await folios.NextAsync(FolioKeys.Invoice(invoice.Series), cancellationToken);
        var issuer = new InvoiceIssuer(fiscal.Rfc!, fiscal.LegalName!, fiscal.TaxRegime!, fiscal.PostalCode!);
        var now = clock.LocalNow;
        var document = new CfdiDocument(
            invoice.Type == CfdiType.Transfer ? "T" : "I",
            invoice.Series,
            folio.ToString(CultureInfo.InvariantCulture),
            new DateTimeOffset(now.DateTime, TimeSpan.Zero),
            invoice.PaymentForm,
            invoice.PaymentMethod,
            invoice.Currency,
            invoice.ExchangeRate,
            invoice.Subtotal,
            invoice.Discount,
            invoice.Vat,
            invoice.Total,
            issuer.ExpeditionPostalCode,
            new CfdiParty(issuer.Rfc, issuer.Name, issuer.TaxRegime, issuer.ExpeditionPostalCode, null),
            new CfdiParty(invoice.Receiver.Rfc, invoice.Receiver.Name, invoice.Receiver.TaxRegime!, invoice.Receiver.PostalCode, invoice.Receiver.CfdiUse),
            invoice.Lines.OrderBy(l => l.Order).Select(l => new CfdiConcept(l.SatProductCode, l.Quantity, l.SatUnitCode, l.UnitName, l.Description, l.UnitPrice, l.Amount, l.Discount,
                l.TaxObject, l.VatRate, l.Vat)).ToList(),
            cartaPorte);

        var stamp = await pac.StampAsync(fiscal, document, cancellationToken);
        if (!stamp.Success || stamp.Uuid is null || stamp.Xml is null)
        {
            return Error.Failure(InvoiceErrors.StampFailed.Code, $"{InvoiceErrors.StampFailed.Description} {stamp.Error}".Trim());
        }

        var xml = await files.SaveBytesAsync(Encoding.UTF8.GetBytes(stamp.Xml), $"{invoice.Series}-{folio}_{stamp.Uuid}.xml", "application/xml",
            FileCategory.InvoiceXml, cancellationToken, nameof(Invoice), invoice.Id);
        if (xml.IsFailure)
        {
            return xml.Error;
        }

        var stamped = invoice.MarkStamped(folio, issuer, stamp.Uuid, stamp.StampedAt ?? clock.UtcNow, fiscal.PacProvider, xml.Value.Id);
        if (stamped.IsFailure)
        {
            return stamped.Error;
        }

        foreach (var ticket in tickets)
        {
            ticket.MarkInvoiced(invoice.Id, clock.UtcNow, tenant.Tenant.Settings.RequireApprovalBeforeInvoice);
        }

        if (invoice.EstimateId is { } estimateId)
        {
            var estimate = await db.Estimates.FirstOrDefaultAsync(e => e.Id == estimateId, cancellationToken);
            estimate?.LinkInvoice(invoice.Id);
        }

        var rendered = await pdf.GenerateAsync(invoice, stamp.Simulated, cancellationToken);
        if (rendered.IsSuccess)
        {
            invoice.AttachPdf(rendered.Value.Id);
        }

        audit.Record(AuditAction.Stamped, nameof(Invoice), invoice.Id, invoice.AuditReference, null, new { stamp.Uuid, invoice.Total, stamp.Simulated });
        await notifications.PublishAsync(new NotificationMessage(
            NotificationEvent.InvoiceStamped,
            NotificationSeverity.Success,
            $"Factura {invoice.Series}-{folio} timbrada",
            $"{invoice.Receiver.Name}: {invoice.Total:N2} {invoice.Currency}. UUID {stamp.Uuid}." + (stamp.Simulated ? " (PAC simulado, sin validez fiscal)" : string.Empty),
            $"facturacion/facturas/{invoice.Id}",
            CustomerId: invoice.CustomerId,
            AttachmentFileId: invoice.PdfFileId), cancellationToken);

        return invoice;
    }
}

/// <summary>
/// Business rules (moment "stamp") and approval workflow of an invoice, and the validation workflows of its
/// tickets: a ticket waiting for (or rejected in) its validation cannot be invoiced.
/// </summary>
internal sealed class InvoiceAuthorization(DocumentFacts facts, IBusinessRules rules, IWorkflowEngine workflows)
{
    public async Task<Result<WorkflowGateResult>> CheckAsync(Invoice invoice, GatePersistence persistence, CancellationToken cancellationToken)
    {
        var values = await facts.InvoiceAsync(invoice, cancellationToken);
        var reference = DocumentLoader.InvoiceReference(invoice);
        var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.Invoice, invoice.Id, reference, values), AutomationTrigger.Stamp, cancellationToken);
        if (checks.IsFailure)
        {
            return checks.Error;
        }

        return await workflows.GateAsync(new WorkflowSubject(AutomationDocument.Invoice, invoice.Id, reference, values), persistence, cancellationToken);
    }

    public async Task<Result> EnsureTicketsReleasedAsync(IReadOnlyList<ServiceTicket> tickets, CancellationToken cancellationToken)
    {
        var statuses = await workflows.LatestStatusesAsync(AutomationDocument.ServiceTicket, tickets.Select(t => t.Id).ToList(), cancellationToken);
        foreach (var ticket in tickets)
        {
            switch (statuses.GetValueOrDefault(ticket.Id, WorkflowStatus.Approved))
            {
                case WorkflowStatus.Pending:
                    return Error.Conflict("Workflow.InProgress", $"{ticket.Folio} está en autorización: se factura cuando se valide.");
                case WorkflowStatus.Rejected when ticket.Status == TicketStatus.Signed:
                    return WorkflowErrors.Rejected(ticket.Folio);
            }
        }

        return Result.Success();
    }
}

/// <summary>Printed representation of a CFDI with the company branding.</summary>
internal sealed class InvoicePdfService(IApplicationDbContext db, IFileService files, IPdfRenderer renderer, IDocumentBrandingProvider branding)
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public async Task<byte[]> RenderAsync(Invoice invoice, bool simulated, CancellationToken cancellationToken)
    {
        var tickets = await db.Tickets.AsNoTracking().Where(t => invoice.TicketIds.Contains(t.Id)).Select(t => t.Folio).ToListAsync(cancellationToken);
        var fields = new List<DocumentField>
        {
            new("Receptor", $"{invoice.Receiver.Name}"),
            new("RFC receptor", invoice.Receiver.Rfc),
            new("Régimen fiscal", invoice.Receiver.TaxRegime),
            new("C.P. domicilio fiscal", invoice.Receiver.PostalCode),
            new("Uso CFDI", invoice.Receiver.CfdiUse),
            new("Método / forma de pago", $"{invoice.PaymentMethod} / {invoice.PaymentForm}"),
            new("Moneda", invoice.Currency == "MXN" ? "MXN" : $"{invoice.Currency} (TC {invoice.ExchangeRate})"),
            new("Vencimiento", invoice.DueOn.ToString("dd/MM/yyyy", Mx)),
            new("COPADE", invoice.CopadeNumber),
            new("Boletas", tickets.Count == 0 ? null : string.Join(", ", tickets))
        };

        var fiscal = new List<DocumentField>
        {
            new("Folio fiscal (UUID)", invoice.Uuid),
            new("Fecha de timbrado", invoice.StampedAt?.ToString("dd/MM/yyyy HH:mm", Mx)),
            new("Emisor", invoice.Issuer is null ? null : $"{invoice.Issuer.Name} · {invoice.Issuer.Rfc}"),
            new("Régimen del emisor", invoice.Issuer?.TaxRegime),
            new("Lugar de expedición", invoice.Issuer?.ExpeditionPostalCode),
            new("PAC", invoice.PacProvider)
        };

        var lines = new DocumentTable("Conceptos", ["Clave SAT", "Unidad", "Descripción", "Cantidad", "P. unitario", "Importe"],
            invoice.Lines.OrderBy(l => l.Order).Select(l => (IReadOnlyList<string>)
            [
                l.SatProductCode, $"{l.SatUnitCode} {l.UnitName}", l.Description, l.Quantity.ToString("#,##0.###", Mx), l.UnitPrice.ToString("C2", Mx), l.Amount.ToString("C2", Mx)
            ]).ToList(), [false, false, false, true, true, true]);

        var totals = new List<DocumentField> { new("Subtotal", invoice.Subtotal.ToString("C2", Mx)) };
        if (invoice.Discount > 0)
        {
            totals.Add(new("Descuento", invoice.Discount.ToString("C2", Mx)));
        }

        totals.Add(new("IVA trasladado", invoice.Vat.ToString("C2", Mx)));
        totals.Add(new($"Total {invoice.Currency}", invoice.Total.ToString("C2", Mx)));

        var document = new BusinessDocument(
            invoice.Type == CfdiType.Transfer ? "CFDI de traslado" : "Factura",
            invoice.Folio is null ? $"{invoice.Series} · borrador" : $"{invoice.Series}-{invoice.Folio}",
            invoice.Status switch
            {
                InvoiceStatus.Draft => "Borrador",
                InvoiceStatus.Stamped => "Vigente",
                InvoiceStatus.CancellationPending => "Cancelación en proceso",
                _ => "Cancelada"
            },
            invoice.IssueDate.ToString("dd 'de' MMMM 'de' yyyy", Mx),
            [("Datos del receptor", fields), ("Datos fiscales", fiscal)],
            [lines],
            new DocumentTotals(totals),
            [("Leyenda", "Este documento es una representación impresa de un CFDI." + (invoice.Notes is null ? string.Empty : $" {invoice.Notes}"))],
            [],
            [],
            invoice.Status == InvoiceStatus.Draft ? "BORRADOR" : invoice.Status == InvoiceStatus.Cancelled ? "CANCELADA" : simulated ? "SIN VALIDEZ FISCAL (PAC SIMULADO)" : null,
            invoice.Uuid is null ? null : $"UUID {invoice.Uuid}");

        return renderer.Render(document, await branding.GetAsync(cancellationToken));
    }

    public async Task<Result<StoredFile>> GenerateAsync(Invoice invoice, bool simulated, CancellationToken cancellationToken) =>
        await files.SaveBytesAsync(await RenderAsync(invoice, simulated, cancellationToken), $"{invoice.Series}-{invoice.Folio}.pdf", FileContent.Pdf,
            FileCategory.GeneratedPdf, cancellationToken, nameof(Invoice), invoice.Id);
}
