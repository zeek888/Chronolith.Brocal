using System.Globalization;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Management;

public enum ReportFormat
{
    Pdf = 0,
    Excel = 1
}

/// <summary>
/// Reporte mensual por cliente o campo con la marca de la empresa: summary by service type and site, the tickets of the
/// month and the invoicing status. Portal users always get the report of their own customer.
/// </summary>
public sealed record GetMonthlyReportQuery(int Year, int Month, Guid? CustomerId, string? FieldName, ReportFormat Format) : IQuery<FileContent>;

internal sealed class GetMonthlyReportQueryHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    IPdfRenderer pdf,
    IExcelWriter excel,
    IDocumentBrandingProvider branding,
    IAuditLog audit) : IQueryHandler<GetMonthlyReportQuery, FileContent>
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public async Task<Result<FileContent>> Handle(GetMonthlyReportQuery query, CancellationToken cancellationToken)
    {
        if (query.Month is < 1 or > 12 || query.Year is < 2000 or > 2100)
        {
            return Error.Validation("Reports.InvalidPeriod", "Indica un mes válido.");
        }

        var customerId = tenant.Membership?.CustomerId ?? query.CustomerId;
        var from = new DateOnly(query.Year, query.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var sites = await db.Sites.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        var siteFilter = string.IsNullOrWhiteSpace(query.FieldName)
            ? null
            : sites.Values.Where(s => string.Equals(s.FieldName, query.FieldName, StringComparison.OrdinalIgnoreCase)).Select(s => s.Id).ToHashSet();

        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => t.ServiceDate >= from && t.ServiceDate <= to && t.Status != TicketStatus.Draft && t.Status != TicketStatus.Cancelled)
            .WhereIf(customerId is not null, t => t.CustomerId == customerId)
            .OrderBy(t => t.ServiceDate).ThenBy(t => t.FolioNumber)
            .ToListAsync(cancellationToken);
        if (siteFilter is not null)
        {
            tickets = tickets.Where(t => siteFilter.Contains(t.SiteId)).ToList();
        }

        var types = await CatalogNames.LoadAsync<ServiceType>(db, tickets.Select(t => t.ServiceTypeId), cancellationToken);
        var units = await CatalogNames.LoadAsync<Unit>(db, tickets.SelectMany(t => t.UnitIds), cancellationToken);
        var customer = customerId is null ? null : await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        var invoiceIds = tickets.Where(t => t.InvoiceId is not null).Select(t => t.InvoiceId!.Value).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => $"{i.Series}-{i.Folio}", cancellationToken);

        var period = from.ToString("MMMM yyyy", Mx);
        var title = $"Reporte mensual de servicios · {period}";
        var subject = customer?.Name ?? "Todos los clientes";
        if (query.FieldName is not null)
        {
            subject += $" · Campo {query.FieldName}";
        }

        var byType = tickets.GroupBy(t => t.ServiceTypeId).Select(g => (Name: types.NameOf(g.Key) ?? "—", Count: g.Count(), Amount: g.Sum(t => t.Subtotal))).OrderByDescending(x => x.Amount).ToList();
        var bySite = tickets.GroupBy(t => t.SiteId).Select(g => (Name: sites.GetValueOrDefault(g.Key)?.Name ?? "—", Count: g.Count(), Amount: g.Sum(t => t.Subtotal))).OrderByDescending(x => x.Amount).ToList();
        var detail = tickets.Select(t => new object?[]
        {
            t.Folio, t.ServiceDate, sites.GetValueOrDefault(t.SiteId)?.Name, types.NameOf(t.ServiceTypeId), string.Join(", ", t.UnitIds.Select(id => units.CodeOf(id))),
            StatusText(t.Status), t.InvoiceId is { } invoiceId ? invoices.GetValueOrDefault(invoiceId) : null, t.Subtotal, t.Total
        }).ToList();

        audit.Record(AuditAction.Exported, "MonthlyReport", customerId, $"{period} · {subject}");
        await db.SaveChangesAsync(cancellationToken);

        var fileName = $"Reporte_{query.Year}-{query.Month:00}{(customer is null ? string.Empty : "_" + customer.Code)}";
        if (query.Format == ReportFormat.Excel)
        {
            var bytes = excel.Write(
            [
                new ExcelSheet("Resumen", ["Concepto", "Servicios", "Importe"],
                    byType.Select(x => new object?[] { x.Name, x.Count, x.Amount }).Concat([["Total", tickets.Count, tickets.Sum(t => t.Subtotal)]]).ToList(), $"{title} · {subject}"),
                new ExcelSheet("Por locación", ["Locación", "Servicios", "Importe"], bySite.Select(x => new object?[] { x.Name, x.Count, x.Amount }).ToList()),
                new ExcelSheet("Boletas", ["Folio", "Fecha", "Locación", "Tipo de servicio", "Unidades", "Estatus", "Factura", "Subtotal", "Total"], detail)
            ]);
            return new FileContent($"{fileName}.xlsx", FileContent.Xlsx, bytes);
        }

        var document = new BusinessDocument(
            "Reporte mensual",
            from.ToString("MM/yyyy", Mx),
            null,
            period,
            [("Alcance", [new("Cliente / campo", subject), new("Servicios", tickets.Count.ToString(Mx)), new("Importe antes de IVA", tickets.Sum(t => t.Subtotal).ToString("C2", Mx)),
                new("Facturado", tickets.Where(t => t.Status == TicketStatus.Invoiced).Sum(t => t.Subtotal).ToString("C2", Mx))])],
            [
                new DocumentTable("Por tipo de servicio", ["Tipo de servicio", "Servicios", "Importe"], byType.Select(x => (IReadOnlyList<string>)[x.Name, x.Count.ToString(Mx), x.Amount.ToString("C2", Mx)]).ToList(), [false, true, true]),
                new DocumentTable("Por locación", ["Locación", "Servicios", "Importe"], bySite.Select(x => (IReadOnlyList<string>)[x.Name, x.Count.ToString(Mx), x.Amount.ToString("C2", Mx)]).ToList(), [false, true, true]),
                new DocumentTable("Boletas del periodo", ["Folio", "Fecha", "Locación", "Servicio", "Estatus", "Factura", "Subtotal"],
                    tickets.Select(t => (IReadOnlyList<string>)
                    [
                        t.Folio, t.ServiceDate.ToString("dd/MM", Mx), sites.GetValueOrDefault(t.SiteId)?.Name ?? "—", types.NameOf(t.ServiceTypeId) ?? "—", StatusText(t.Status),
                        t.InvoiceId is { } id ? invoices.GetValueOrDefault(id) ?? "" : "", t.Subtotal.ToString("C2", Mx)
                    ]).ToList(), [false, false, false, false, false, false, true])
            ],
            new DocumentTotals([new("Total del periodo (antes de IVA)", tickets.Sum(t => t.Subtotal).ToString("C2", Mx))]),
            [],
            [],
            []);

        return new FileContent($"{fileName}.pdf", FileContent.Pdf, pdf.Render(document, await branding.GetAsync(cancellationToken)));
    }

    private static string StatusText(TicketStatus status) => status switch
    {
        TicketStatus.Signed => "Firmada",
        TicketStatus.Approved => "Validada",
        TicketStatus.Invoiced => "Facturada",
        _ => status.ToString()
    };
}

/// <summary>Fields (campos) of the sites, to choose the scope of a report.</summary>
public sealed record GetReportFieldsQuery : IQuery<IReadOnlyList<string>>;

internal sealed class GetReportFieldsQueryHandler(IApplicationDbContext db) : IQueryHandler<GetReportFieldsQuery, IReadOnlyList<string>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(GetReportFieldsQuery query, CancellationToken cancellationToken) =>
        await db.Sites.AsNoTracking().Where(s => s.FieldName != null).Select(s => s.FieldName!).Distinct().OrderBy(f => f).ToListAsync(cancellationToken);
}
