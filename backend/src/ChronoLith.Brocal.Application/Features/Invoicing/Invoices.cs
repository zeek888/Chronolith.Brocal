using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Invoicing;

public sealed record InvoiceSummaryDto(
    Guid Id, string Series, long? Folio, InvoiceStatus Status, CfdiType Type, Guid CustomerId, string ReceiverName, string ReceiverRfc, DateOnly IssueDate, DateOnly DueOn,
    decimal Total, decimal Balance, string Currency, string? Uuid, string? CopadeNumber, int TicketCount, bool IsOverdue, int DaysOverdue);

public sealed record InvoiceLineDto(Guid Id, int Order, Guid? TicketId, Guid? ServiceId, string SatProductCode, string SatUnitCode, string UnitName, string Description,
    decimal Quantity, decimal UnitPrice, decimal Amount, decimal Discount, string TaxObject, decimal VatRate, decimal Vat);

public sealed record InvoiceDetailDto(
    InvoiceSummaryDto Summary, InvoiceReceiver Receiver, InvoiceIssuer? Issuer, string PaymentForm, string PaymentMethod, decimal ExchangeRate, decimal Subtotal,
    decimal Discount, decimal Vat, decimal PaidAmount, IReadOnlyList<InvoiceLineDto> Lines, IReadOnlyList<Guid> TicketIds, Guid? EstimateId, Guid? TripId,
    DateTimeOffset? StampedAt, string? PacProvider, string? XmlUrl, string? PdfUrl, string? CancellationMotive, string? SubstitutionUuid, DateTimeOffset? CancelledAt,
    string? Notes, IReadOnlyList<PaymentListItemDto> Payments);

public sealed record PaymentListItemDto(Guid PaymentId, DateTimeOffset PaidAt, decimal Amount, int Partiality, string PaymentForm, string? Reference, PaymentComplementStatus ComplementStatus);

internal static class InvoiceVisibility
{
    /// <summary>Portal users only see the stamped invoices of their customer.</summary>
    public static IQueryable<Invoice> Visible(this IQueryable<Invoice> invoices, ITenantContext tenant) =>
        tenant.Membership?.CustomerId is { } customer
            ? invoices.Where(i => i.CustomerId == customer && i.Status != InvoiceStatus.Draft)
            : invoices;
}

public sealed record SearchInvoicesQuery(string? Search, InvoiceStatus? Status, Guid? CustomerId, DateOnly? From, DateOnly? To, bool? OnlyWithBalance, bool? OnlyOverdue,
    int Page = 1, int PageSize = 25) : IQuery<PagedResult<InvoiceSummaryDto>>;

internal sealed class SearchInvoicesQueryHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock) : IQueryHandler<SearchInvoicesQuery, PagedResult<InvoiceSummaryDto>>
{
    public async Task<Result<PagedResult<InvoiceSummaryDto>>> Handle(SearchInvoicesQuery query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var request = new PageRequest(query.Search, query.Page, query.PageSize);
        var invoices = db.Invoices.AsNoTracking().Visible(tenant)
            .WhereIf(query.Status is not null, i => i.Status == query.Status)
            .WhereIf(query.CustomerId is not null, i => i.CustomerId == query.CustomerId)
            .WhereIf(query.From is not null, i => i.IssueDate >= query.From)
            .WhereIf(query.To is not null, i => i.IssueDate <= query.To)
            .WhereIf(query.OnlyWithBalance == true, i => i.Status == InvoiceStatus.Stamped && i.Balance > 0)
            .WhereIf(query.OnlyOverdue == true, i => i.Status == InvoiceStatus.Stamped && i.Balance > 0 && i.DueOn < today);

        if (request.Term is { } term)
        {
            invoices = invoices.Where(i => (i.Uuid != null && i.Uuid.ToLower().Contains(term)) || (i.CopadeNumber != null && i.CopadeNumber.ToLower().Contains(term))
                                           || db.Customers.Any(c => c.Id == i.CustomerId && c.Name.ToLower().Contains(term)));
        }

        return await invoices
            .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Folio)
            .Select(i => new InvoiceSummaryDto(i.Id, i.Series, i.Folio, i.Status, i.Type, i.CustomerId,
                db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.Name).FirstOrDefault() ?? string.Empty,
                db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.Rfc).FirstOrDefault() ?? string.Empty,
                i.IssueDate, i.DueOn, i.Total, i.Balance, i.Currency, i.Uuid, i.CopadeNumber, i.TicketIds.Count,
                i.Status == InvoiceStatus.Stamped && i.Balance > 0 && i.DueOn < today, i.Status == InvoiceStatus.Stamped && i.Balance > 0 && i.DueOn < today ? today.DayNumber - i.DueOn.DayNumber : 0))
            .ToPagedAsync(request, cancellationToken);
    }
}

public sealed record GetInvoiceQuery(Guid Id) : IQuery<InvoiceDetailDto>;

internal sealed class GetInvoiceQueryHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock, IFileService files) : IQueryHandler<GetInvoiceQuery, InvoiceDetailDto>
{
    public async Task<Result<InvoiceDetailDto>> Handle(GetInvoiceQuery query, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking().Visible(tenant).Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == query.Id, cancellationToken);
        return invoice is null ? InvoiceErrors.NotFound : await InvoiceDetails.BuildAsync(db, files, invoice, clock.Today, cancellationToken);
    }
}

internal static class InvoiceDetails
{
    public static async Task<InvoiceDetailDto> BuildAsync(IApplicationDbContext db, IFileService files, Invoice invoice, DateOnly today, CancellationToken cancellationToken)
    {
        var payments = await db.Payments.AsNoTracking()
            .SelectMany(p => p.Applications.Where(a => a.InvoiceId == invoice.Id).Select(a => new PaymentListItemDto(p.Id, p.PaidAt, a.Amount, a.Partiality, p.PaymentForm, p.Reference, p.ComplementStatus)))
            .ToListAsync(cancellationToken);
        var overdue = invoice.IsOverdue(today);

        return new InvoiceDetailDto(
            new InvoiceSummaryDto(invoice.Id, invoice.Series, invoice.Folio, invoice.Status, invoice.Type, invoice.CustomerId, invoice.Receiver.Name, invoice.Receiver.Rfc,
                invoice.IssueDate, invoice.DueOn, invoice.Total, invoice.Balance, invoice.Currency, invoice.Uuid, invoice.CopadeNumber, invoice.TicketIds.Count, overdue,
                overdue ? today.DayNumber - invoice.DueOn.DayNumber : 0),
            invoice.Receiver, invoice.Issuer, invoice.PaymentForm, invoice.PaymentMethod, invoice.ExchangeRate, invoice.Subtotal, invoice.Discount, invoice.Vat, invoice.PaidAmount,
            invoice.Lines.OrderBy(l => l.Order).Select(l => new InvoiceLineDto(l.Id, l.Order, l.TicketId, l.ServiceId, l.SatProductCode, l.SatUnitCode, l.UnitName, l.Description,
                l.Quantity, l.UnitPrice, l.Amount, l.Discount, l.TaxObject, l.VatRate, l.Vat)).ToList(),
            invoice.TicketIds, invoice.EstimateId, invoice.TripId, invoice.StampedAt, invoice.PacProvider,
            invoice.XmlFileId is { } xml ? files.GetUrl(xml) : null, invoice.PdfFileId is { } pdf ? files.GetUrl(pdf) : null,
            invoice.CancellationMotive, invoice.SubstitutionUuid, invoice.CancelledAt, invoice.Notes, payments.OrderBy(p => p.PaidAt).ToList());
    }
}

// ---- Drafts ---------------------------------------------------------------------------------------------

/// <summary>"La boleta firmada se convierte en factura en un clic": a draft from one or several tickets of the same customer.</summary>
public sealed record CreateInvoiceFromTicketsCommand(IReadOnlyList<Guid> TicketIds, string? PaymentForm, string? PaymentMethod, string? CfdiUse, string? CopadeNumber, Guid? EstimateId, string? Notes, bool Stamp)
    : ICommand<Guid>;

internal sealed class CreateInvoiceFromTicketsCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    ITenantClock clock,
    InvoiceStamping stamping) : ICommandHandler<CreateInvoiceFromTicketsCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateInvoiceFromTicketsCommand command, CancellationToken cancellationToken)
    {
        var ids = command.TicketIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return InvoiceErrors.NoLines;
        }

        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var tickets = await db.Tickets.Include(t => t.Lines).Where(t => ids.Contains(t.Id)).OrderBy(t => t.FolioNumber).ToListAsync(ct);
            if (tickets.Count != ids.Count)
            {
                return Result.Failure<Guid>(TicketErrors.NotFound);
            }

            if (tickets.Select(t => t.CustomerId).Distinct().Count() > 1)
            {
                return Result.Failure<Guid>(InvoiceErrors.MixedCustomers);
            }

            foreach (var ticket in tickets)
            {
                var check = ticket.EnsureInvoiceable(tenant.Tenant.Settings.RequireApprovalBeforeInvoice);
                if (check.IsFailure)
                {
                    return Result.Failure<Guid>(Error.Conflict(check.Error.Code, $"{ticket.Folio}: {check.Error.Description}"));
                }
            }

            var drafts = await db.Invoices.AsNoTracking().Where(i => i.Status == InvoiceStatus.Draft).Select(i => i.TicketIds).ToListAsync(ct);
            if (drafts.Any(d => d.Any(ids.Contains)))
            {
                return Result.Failure<Guid>(Error.Conflict("Invoices.TicketInDraft", "Alguna boleta ya está en otra factura en borrador."));
            }

            var customer = await db.Customers.AsNoTracking().FirstAsync(c => c.Id == tickets[0].CustomerId, ct);
            var serviceIds = tickets.SelectMany(t => t.Lines.Select(l => l.ServiceId)).Distinct().ToList();
            var services = await db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

            var lines = tickets.SelectMany(t => t.Lines.OrderBy(l => l.Order).Select(l => new InvoiceLineData(
                t.Id, l.ServiceId, services[l.ServiceId].SatProductCode, services[l.ServiceId].SatUnitCode, l.UnitName,
                $"{l.Description} · boleta {t.Folio} ({t.ServiceDate:dd/MM/yyyy})", l.Quantity, l.UnitPrice, 0, services[l.ServiceId].TaxObject, l.VatRate))).ToList();

            var method = command.PaymentMethod ?? customer.PaymentMethod;
            var created = Invoice.CreateDraft(tenant.Tenant.Fiscal.InvoiceSeries, new InvoiceDraftData(
                customer.Id,
                new InvoiceReceiver(customer.Rfc, customer.LegalName, customer.TaxRegime, customer.PostalCode, command.CfdiUse ?? customer.CfdiUse),
                CfdiType.Income,
                method == "PPD" ? "99" : command.PaymentForm ?? (customer.PaymentForm == "99" ? "03" : customer.PaymentForm),
                method,
                tickets[0].Currency,
                1,
                clock.Today,
                customer.CreditDays,
                lines,
                ids,
                command.EstimateId,
                null,
                command.CopadeNumber,
                command.Notes));

            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error);
            }

            db.Invoices.Add(created.Value);
            if (command.Stamp)
            {
                var stamped = await stamping.StampAsync(created.Value, null, ct);
                if (stamped.IsFailure)
                {
                    return Result.Failure<Guid>(stamped.Error);
                }
            }

            await db.SaveChangesAsync(ct);
            return Result.Success(created.Value.Id);
        }, cancellationToken);
    }
}

/// <summary>Manual invoice (services not documented with tickets, advances, adjustments).</summary>
public sealed record SaveInvoiceDraftCommand(Guid? Id, Guid CustomerId, string? CfdiUse, string PaymentForm, string PaymentMethod, string Currency, decimal ExchangeRate,
    DateOnly? IssueDate, IReadOnlyList<InvoiceLineData> Lines, string? CopadeNumber, string? Notes) : ICommand<Guid>;

internal sealed class SaveInvoiceDraftCommandValidator : AbstractValidator<SaveInvoiceDraftCommand>
{
    public SaveInvoiceDraftCommandValidator()
    {
        RuleFor(c => c.CustomerId).RequiredId("el cliente");
        RuleFor(c => c.Lines).NotEmpty().WithMessage("Agrega al menos un concepto.");
        RuleForEach(c => c.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
            l.RuleFor(x => x.SatProductCode).Matches("^\\d{8}$").WithMessage("Clave SAT de 8 dígitos.");
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

internal sealed class SaveInvoiceDraftCommandHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock) : ICommandHandler<SaveInvoiceDraftCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveInvoiceDraftCommand command, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == command.CustomerId, cancellationToken);
        if (customer is null)
        {
            return Domain.Common.CatalogErrors.ReferenceNotFound(CustomerErrors.Catalog);
        }

        var data = new InvoiceDraftData(customer.Id, new InvoiceReceiver(customer.Rfc, customer.LegalName, customer.TaxRegime, customer.PostalCode, command.CfdiUse ?? customer.CfdiUse),
            CfdiType.Income, command.PaymentForm, command.PaymentMethod, command.Currency, command.ExchangeRate, command.IssueDate ?? clock.Today, customer.CreditDays,
            command.Lines, [], null, null, command.CopadeNumber, command.Notes);

        if (command.Id is { } id)
        {
            var invoice = await db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (invoice is null)
            {
                return InvoiceErrors.NotFound;
            }

            // Lines that came from tickets keep their tickets.
            var updated = invoice.UpdateDraft(data with { TicketIds = invoice.TicketIds, EstimateId = invoice.EstimateId, TripId = invoice.TripId });
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            await db.SaveChangesAsync(cancellationToken);
            return invoice.Id;
        }

        var created = Invoice.CreateDraft(tenant.Tenant.Fiscal.InvoiceSeries, data);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Invoices.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value.Id;
    }
}

public sealed record StampInvoiceCommand(Guid Id) : ICommand<Guid>;

internal sealed class StampInvoiceCommandHandler(IApplicationDbContext db, InvoiceStamping stamping) : ICommandHandler<StampInvoiceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(StampInvoiceCommand command, CancellationToken cancellationToken) =>
        await db.ExecuteInTransactionAsync(async ct =>
        {
            var invoice = await db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == command.Id, ct);
            if (invoice is null)
            {
                return Result.Failure<Guid>(InvoiceErrors.NotFound);
            }

            var stamped = await stamping.StampAsync(invoice, null, ct);
            if (stamped.IsFailure)
            {
                return Result.Failure<Guid>(stamped.Error);
            }

            await db.SaveChangesAsync(ct);
            return Result.Success(invoice.Id);
        }, cancellationToken);
}

public sealed record DeleteInvoiceDraftCommand(Guid Id) : ICommand;

internal sealed class DeleteInvoiceDraftCommandHandler(IApplicationDbContext db) : ICommandHandler<DeleteInvoiceDraftCommand>
{
    public async Task<Result> Handle(DeleteInvoiceDraftCommand command, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return InvoiceErrors.NotDraft;
        }

        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Cancels a stamped invoice with the SAT motive; the tickets go back to "por facturar".</summary>
public sealed record CancelInvoiceCommand(Guid Id, string Motive, string? SubstitutionUuid) : ICommand;

internal sealed class CancelInvoiceCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    IPacGateway pac,
    ITenantClock clock,
    IAuditLog audit) : ICommandHandler<CancelInvoiceCommand>
{
    public async Task<Result> Handle(CancelInvoiceCommand command, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        var requested = invoice.RequestCancellation(command.Motive, command.SubstitutionUuid, clock.UtcNow);
        if (requested.IsFailure)
        {
            return requested;
        }

        var result = await pac.CancelAsync(tenant.Tenant.Fiscal, invoice.Uuid!, invoice.Receiver.Rfc, invoice.Total, command.Motive, command.SubstitutionUuid, cancellationToken);
        if (!result.Success)
        {
            return Error.Failure("Invoices.CancelFailed", $"El PAC rechazó la cancelación. {result.Error}".Trim());
        }

        if (result.Accepted)
        {
            invoice.ConfirmCancellation(clock.UtcNow);
            var tickets = await db.Tickets.Where(t => t.InvoiceId == invoice.Id).ToListAsync(cancellationToken);
            tickets.ForEach(t => t.ReleaseInvoice());
            if (invoice.EstimateId is { } estimateId)
            {
                (await db.Estimates.FirstOrDefaultAsync(e => e.Id == estimateId, cancellationToken))?.UnlinkInvoice();
            }
        }

        audit.Record(AuditAction.Cancelled, nameof(Invoice), invoice.Id, invoice.AuditReference, $"Motivo {command.Motive}", new { result.Status });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record GetInvoiceFileQuery(Guid Id, bool Xml) : IQuery<FileContent>;

internal sealed class GetInvoiceFileQueryHandler(IApplicationDbContext db, ITenantContext tenant, IFileService files, InvoicePdfService pdf) : IQueryHandler<GetInvoiceFileQuery, FileContent>
{
    public async Task<Result<FileContent>> Handle(GetInvoiceFileQuery query, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking().Visible(tenant).Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == query.Id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        var name = invoice.Folio is null ? $"{invoice.Series}-borrador" : $"{invoice.Series}-{invoice.Folio}";
        if (query.Xml)
        {
            return invoice.XmlFileId is { } xml && await files.ReadAllBytesAsync(xml, cancellationToken) is { } bytes
                ? new FileContent($"{name}.xml", FileContent.Xml, bytes)
                : FileErrors.NotFound;
        }

        if (invoice.PdfFileId is { } stored && await files.ReadAllBytesAsync(stored, cancellationToken) is { } pdfBytes)
        {
            return new FileContent($"{name}.pdf", FileContent.Pdf, pdfBytes);
        }

        var simulated = invoice.PacProvider == TenantFiscalProfile.SimulatedPac;
        return new FileContent($"{name}.pdf", FileContent.Pdf, await pdf.RenderAsync(invoice, simulated, cancellationToken));
    }
}

/// <summary>Tickets ready to invoice (signed or approved) grouped by customer: the "one click" list.</summary>
public sealed record GetInvoiceableTicketsQuery(Guid? CustomerId) : IQuery<IReadOnlyList<InvoiceableTicketDto>>;

public sealed record InvoiceableTicketDto(Guid Id, string Folio, DateOnly ServiceDate, Guid CustomerId, string? CustomerName, string? SiteName, TicketStatus Status, decimal Subtotal, decimal Total, bool InDraft);

internal sealed class GetInvoiceableTicketsQueryHandler(IApplicationDbContext db, ITenantContext tenant) : IQueryHandler<GetInvoiceableTicketsQuery, IReadOnlyList<InvoiceableTicketDto>>
{
    public async Task<Result<IReadOnlyList<InvoiceableTicketDto>>> Handle(GetInvoiceableTicketsQuery query, CancellationToken cancellationToken)
    {
        var requireApproval = tenant.Tenant.Settings.RequireApprovalBeforeInvoice;
        var drafts = (await db.Invoices.AsNoTracking().Where(i => i.Status == InvoiceStatus.Draft).Select(i => i.TicketIds).ToListAsync(cancellationToken)).SelectMany(x => x).ToHashSet();

        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => t.Status == TicketStatus.Approved || !requireApproval && t.Status == TicketStatus.Signed)
            .WhereIf(query.CustomerId is not null, t => t.CustomerId == query.CustomerId)
            .OrderBy(t => t.ServiceDate)
            .Select(t => new InvoiceableTicketDto(t.Id, t.Folio, t.ServiceDate, t.CustomerId, db.Customers.Where(c => c.Id == t.CustomerId).Select(c => c.Name).FirstOrDefault(),
                db.Sites.Where(s => s.Id == t.SiteId).Select(s => s.Name).FirstOrDefault(), t.Status, t.Subtotal, t.Total, false))
            .ToListAsync(cancellationToken);

        return tickets.Select(t => t with { InDraft = drafts.Contains(t.Id) }).ToList();
    }
}
