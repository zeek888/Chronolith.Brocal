using System.Globalization;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Tickets;

/// <summary>Builds the branded PDF of a ticket (header with the company logo, form answers, lines, photos and signatures).</summary>
internal sealed class TicketPdfService(
    IApplicationDbContext db,
    IFileService files,
    IPdfRenderer renderer,
    IDocumentBrandingProvider branding,
    ITenantClock clock)
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public async Task<byte[]> RenderAsync(ServiceTicket ticket, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == ticket.CustomerId, cancellationToken);
        var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == ticket.SiteId, cancellationToken);
        var type = await db.ServiceTypes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == ticket.ServiceTypeId, cancellationToken);
        var contract = ticket.ContractId is null ? null : await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == ticket.ContractId, cancellationToken);
        var workOrder = ticket.WorkOrderId is null ? null : await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == ticket.WorkOrderId, cancellationToken);
        var units = await CatalogNames.LoadAsync<Unit>(db, ticket.UnitIds, cancellationToken);
        var employees = await CatalogNames.LoadAsync<Employee>(db, ticket.EmployeeIds, cancellationToken);
        var crews = await CatalogNames.LoadAsync<Crew>(db, [ticket.CrewId], cancellationToken);
        var services = await CatalogNames.LoadAsync<Service>(db, ticket.Lines.Select(l => l.ServiceId), cancellationToken);

        var general = new List<DocumentField>
        {
            new("Cliente", customer is null ? null : $"{customer.Name} ({customer.Rfc})"),
            new("Contrato", contract is null ? null : $"{contract.Code} · {contract.Name}"),
            new("Locación / pozo", site is null ? null : $"{site.Code} · {site.Name}" + (site.FieldName is null ? string.Empty : $" · {site.FieldName}")),
            new("Tipo de servicio", type?.Name),
            new("Orden de trabajo", workOrder?.Folio + (workOrder?.CustomerReference is null ? string.Empty : $" (ref. {workOrder.CustomerReference})")),
            new("Fecha del servicio", ticket.ServiceDate.ToString("dd/MM/yyyy", Mx)),
            new("Inicio", Local(ticket.StartedAt)),
            new("Término", Local(ticket.FinishedAt)),
            new("Representante del cliente", ticket.CustomerRepresentative)
        };

        var resources = new List<DocumentField>
        {
            new("Unidades", string.Join(", ", ticket.UnitIds.Where(units.ContainsKey).Select(id => $"{units[id].Code} {units[id].Name}"))),
            new("Cuadrilla", crews.NameOf(ticket.CrewId)),
            new("Personal", string.Join(", ", ticket.EmployeeIds.Where(employees.ContainsKey).Select(id => employees[id].Name)))
        };

        var answers = type?.Fields
            .Select(f => new DocumentField(f.Label + (f.Unit is null ? string.Empty : $" ({f.Unit})"), FormatAnswer(f, ticket.Answers.GetValueOrDefault(f.Key))))
            .ToList() ?? [];

        var lines = new DocumentTable(
            "Conceptos",
            ["Clave", "Concepto", "Cantidad", "Unidad", "P. unitario", "Importe"],
            ticket.Lines.OrderBy(l => l.Order).Select(l => (IReadOnlyList<string>)
            [
                services.CodeOf(l.ServiceId) ?? string.Empty,
                l.Description,
                l.Quantity.ToString("#,##0.###", Mx),
                l.UnitName,
                l.UnitPrice.ToString("C2", Mx),
                l.Amount.ToString("C2", Mx)
            ]).ToList(),
            [false, false, true, false, true, true]);

        var totals = new DocumentTotals(
        [
            new("Subtotal", ticket.Subtotal.ToString("C2", Mx)),
            new("IVA", ticket.Vat.ToString("C2", Mx)),
            new($"Total {ticket.Currency}", ticket.Total.ToString("C2", Mx))
        ]);

        var images = new List<DocumentImage>();
        foreach (var photo in ticket.Photos.OrderBy(p => p.Order).Take(24))
        {
            if (await files.ReadAllBytesAsync(photo.FileId, cancellationToken) is { } bytes)
            {
                var caption = photo.Caption ?? string.Empty;
                caption += $" · {Local(photo.TakenAt)}";
                if (photo.Latitude is not null)
                {
                    caption += $" · {photo.Latitude:0.00000}, {photo.Longitude:0.00000}";
                }

                images.Add(new DocumentImage(bytes, caption.Trim(' ', '·')));
            }
        }

        var signatureImage = ticket.SignatureFileId is { } signatureId ? await files.ReadAllBytesAsync(signatureId, cancellationToken) : null;
        var signatures = new List<DocumentSignature>
        {
            new("Por el cliente", ticket.SignerName, ticket.SignerPosition, signatureImage, ticket.SignedAt is null ? null : Local(ticket.SignedAt)),
            new("Por la empresa", ticket.CapturedByName, "Responsable del servicio", null, Local(ticket.CreatedAt))
        };

        var texts = new List<(string, string)>();
        if (ticket.Description is not null)
        {
            texts.Add(("Trabajo realizado", ticket.Description));
        }

        if (ticket.Observations is not null)
        {
            texts.Add(("Observaciones", ticket.Observations));
        }

        if (ticket.CancellationReason is not null)
        {
            texts.Add(("Motivo de cancelación", ticket.CancellationReason));
        }

        var document = new BusinessDocument(
            "Boleta de servicio",
            ticket.Folio,
            StatusLabel(ticket.Status),
            ticket.ServiceDate.ToString("dd 'de' MMMM 'de' yyyy", Mx),
            [("Datos generales", general), ("Recursos", resources), ("Datos del servicio", answers)],
            [lines],
            totals,
            texts,
            images,
            signatures,
            ticket.Status switch
            {
                TicketStatus.Draft => "BORRADOR",
                TicketStatus.Cancelled => "CANCELADA",
                _ => null
            },
            $"Brocal · {ticket.Folio} · {ticket.Id}");

        return renderer.Render(document, await branding.GetAsync(cancellationToken));
    }

    /// <summary>Renders and stores the PDF of a closed ticket.</summary>
    public async Task<Result<StoredFile>> GenerateAsync(ServiceTicket ticket, CancellationToken cancellationToken)
    {
        var bytes = await RenderAsync(ticket, cancellationToken);
        return await files.SaveBytesAsync(bytes, $"Boleta_{ticket.Folio}.pdf", "application/pdf", FileCategory.GeneratedPdf, cancellationToken, nameof(ServiceTicket), ticket.Id);
    }

    private string? Local(DateTimeOffset? value) =>
        value is null ? null : TimeZoneInfo.ConvertTime(value.Value, clock.TimeZone).ToString("dd/MM/yyyy HH:mm", Mx);

    private static string? FormatAnswer(FormFieldDefinition field, string? value) => field.Type switch
    {
        FormFieldType.Checkbox => value is null ? null : value is "true" or "1" or "si" or "sí" ? "Sí" : "No",
        FormFieldType.Number when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number.ToString("#,##0.###", Mx),
        _ => value
    };

    internal static string StatusLabel(TicketStatus status) => status switch
    {
        TicketStatus.Draft => "Borrador",
        TicketStatus.Signed => "Firmada",
        TicketStatus.Approved => "Validada",
        TicketStatus.Invoiced => "Facturada",
        _ => "Cancelada"
    };
}

/// <summary>PDF of a ticket: the stored one when it is closed, a fresh preview while it is a draft.</summary>
public sealed record GetTicketPdfQuery(Guid TicketId, bool Regenerate) : IQuery<FileContent>;

internal sealed class GetTicketPdfQueryHandler(IApplicationDbContext db, ITenantContext tenant, IFileService files, TicketPdfService pdf)
    : IQueryHandler<GetTicketPdfQuery, FileContent>
{
    public async Task<Result<FileContent>> Handle(GetTicketPdfQuery query, CancellationToken cancellationToken)
    {
        var ticket = await TicketLoader.LoadAsync(db, tenant, query.TicketId, cancellationToken);
        if (ticket is null)
        {
            return TicketErrors.NotFound;
        }

        if (!query.Regenerate && ticket.PdfFileId is { } stored && await files.ReadAllBytesAsync(stored, cancellationToken) is { } bytes)
        {
            return new FileContent($"Boleta_{ticket.Folio}.pdf", "application/pdf", bytes);
        }

        if (query.Regenerate && ticket.Status is TicketStatus.Signed or TicketStatus.Approved or TicketStatus.Invoiced)
        {
            var generated = await pdf.GenerateAsync(ticket, cancellationToken);
            if (generated.IsSuccess)
            {
                ticket.AttachPdf(generated.Value.Id);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return new FileContent($"Boleta_{ticket.Folio}.pdf", "application/pdf", await pdf.RenderAsync(ticket, cancellationToken));
    }
}

/// <summary>Logo and palette of the current company for documents.</summary>
internal sealed class DocumentBrandingProvider(ITenantContext tenant, IFileService files) : IDocumentBrandingProvider
{
    public async Task<DocumentBranding> GetAsync(CancellationToken cancellationToken)
    {
        var snapshot = tenant.Tenant;
        var palette = snapshot.Branding.UseChronoLithTheme ? ThemePalette.ChronoLith : snapshot.Branding.Palette;
        var logo = snapshot.Branding.LogoFileId is { } logoId ? await files.ReadAllBytesAsync(logoId, cancellationToken) : null;

        return new DocumentBranding(
            snapshot.Branding.AppName,
            snapshot.Fiscal.LegalName ?? snapshot.LegalName ?? snapshot.Name,
            snapshot.Fiscal.Rfc ?? snapshot.TaxId,
            logo,
            palette.Primary,
            palette.Tertiary,
            snapshot.Branding.DocumentFooter);
    }
}
