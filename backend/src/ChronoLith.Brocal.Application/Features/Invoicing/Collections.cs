using System.Globalization;
using System.Text;
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

// ---- Payments -------------------------------------------------------------------------------------------

public sealed record PaymentApplicationInput(Guid InvoiceId, decimal Amount);

public sealed record PaymentSummaryDto(Guid Id, Guid CustomerId, string? CustomerName, DateTimeOffset PaidAt, decimal Amount, string PaymentForm, string? Reference,
    PaymentComplementStatus ComplementStatus, string? ComplementUuid, int Invoices);

public sealed record SearchPaymentsQuery(Guid? CustomerId, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 25) : IQuery<PagedResult<PaymentSummaryDto>>;

internal sealed class SearchPaymentsQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<SearchPaymentsQuery, PagedResult<PaymentSummaryDto>>
{
    public async Task<Result<PagedResult<PaymentSummaryDto>>> Handle(SearchPaymentsQuery query, CancellationToken cancellationToken)
    {
        var from = query.From is null ? (DateTimeOffset?)null : clock.StartOfDay(query.From.Value);
        var to = query.To is null ? (DateTimeOffset?)null : clock.StartOfDay(query.To.Value.AddDays(1));
        return await db.Payments.AsNoTracking()
            .WhereIf(query.CustomerId is not null, p => p.CustomerId == query.CustomerId)
            .WhereIf(from is not null, p => p.PaidAt >= from)
            .WhereIf(to is not null, p => p.PaidAt < to)
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new PaymentSummaryDto(p.Id, p.CustomerId, db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).FirstOrDefault(), p.PaidAt, p.Amount,
                p.PaymentForm, p.Reference, p.ComplementStatus, p.ComplementUuid, p.Applications.Count))
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);
    }
}

/// <summary>Registers a payment of a customer applied to its stamped invoices (partial payments allowed).</summary>
public sealed record RegisterPaymentCommand(Guid CustomerId, DateTimeOffset PaidAt, string PaymentForm, string? Reference, string? Notes, IReadOnlyList<PaymentApplicationInput> Applications)
    : ICommand<Guid>;

internal sealed class RegisterPaymentCommandValidator : AbstractValidator<RegisterPaymentCommand>
{
    public RegisterPaymentCommandValidator()
    {
        RuleFor(c => c.CustomerId).RequiredId("el cliente");
        RuleFor(c => c.Applications).NotEmpty();
        RuleForEach(c => c.Applications).ChildRules(a => a.RuleFor(x => x.Amount).GreaterThan(0));
    }
}

internal sealed class RegisterPaymentCommandHandler(IApplicationDbContext db) : ICommandHandler<RegisterPaymentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterPaymentCommand command, CancellationToken cancellationToken)
    {
        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var ids = command.Applications.Select(a => a.InvoiceId).Distinct().ToList();
            var invoices = await db.Invoices.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            if (invoices.Count != ids.Count)
            {
                return Result.Failure<Guid>(InvoiceErrors.NotFound);
            }

            if (invoices.Values.Any(i => i.CustomerId != command.CustomerId))
            {
                return Result.Failure<Guid>(PaymentErrors.CustomerMismatch);
            }

            var applications = new List<PaymentApplicationData>();
            foreach (var input in command.Applications)
            {
                var invoice = invoices[input.InvoiceId];
                var partiality = await db.Payments.SelectMany(p => p.Applications).CountAsync(a => a.InvoiceId == invoice.Id, ct) + 1;
                applications.Add(new PaymentApplicationData(invoice.Id, input.Amount, partiality, invoice.Balance, invoice.PaymentMethod == "PPD"));

                var applied = invoice.ApplyPayment(input.Amount, command.PaidAt);
                if (applied.IsFailure)
                {
                    return Result.Failure<Guid>(Error.Validation(applied.Error.Code, $"{invoice.AuditReference}: {applied.Error.Description}"));
                }

                if (invoice.Balance == 0 && invoice.EstimateId is { } estimateId)
                {
                    (await db.Estimates.FirstOrDefaultAsync(e => e.Id == estimateId, ct))?.MarkPaid();
                }
            }

            var payment = Payment.Register(command.CustomerId, command.PaidAt, invoices.Values.First().Currency, command.PaymentForm, command.Reference, command.Notes, applications);
            if (payment.IsFailure)
            {
                return Result.Failure<Guid>(payment.Error);
            }

            db.Payments.Add(payment.Value);
            await db.SaveChangesAsync(ct);
            return Result.Success(payment.Value.Id);
        }, cancellationToken);
    }
}

public sealed record DeletePaymentCommand(Guid Id) : ICommand;

internal sealed class DeletePaymentCommandHandler(IApplicationDbContext db, IAuditLog audit) : ICommandHandler<DeletePaymentCommand>
{
    public async Task<Result> Handle(DeletePaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.Include(p => p.Applications).FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (payment is null)
        {
            return PaymentErrors.NotFound;
        }

        if (payment.ComplementStatus == PaymentComplementStatus.Stamped)
        {
            return PaymentErrors.ComplementStamped;
        }

        var ids = payment.Applications.Select(a => a.InvoiceId).ToList();
        var invoices = await db.Invoices.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        foreach (var application in payment.Applications)
        {
            invoices.GetValueOrDefault(application.InvoiceId)?.RevertPayment(application.Amount);
        }

        db.Payments.Remove(payment);
        audit.Record(AuditAction.Deleted, nameof(Payment), payment.Id, $"{payment.Amount:N2} {payment.Reference}");
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Complemento de recepción de pagos (CFDI tipo P) for payments of PPD invoices.</summary>
public sealed record StampPaymentComplementCommand(Guid Id) : ICommand<string>;

internal sealed class StampPaymentComplementCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    IFolioService folios,
    IPacGateway pac,
    IFileService files,
    ITenantClock clock,
    IAuditLog audit) : ICommandHandler<StampPaymentComplementCommand, string>
{
    public async Task<Result<string>> Handle(StampPaymentComplementCommand command, CancellationToken cancellationToken)
    {
        var fiscal = tenant.Tenant.Fiscal;
        if (!fiscal.IsComplete)
        {
            return InvoiceErrors.IssuerIncomplete;
        }

        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var payment = await db.Payments.Include(p => p.Applications).FirstOrDefaultAsync(p => p.Id == command.Id, ct);
            if (payment is null)
            {
                return Result.Failure<string>(PaymentErrors.NotFound);
            }

            if (payment.ComplementStatus != PaymentComplementStatus.Pending)
            {
                return Result.Failure<string>(PaymentErrors.ComplementNotPending);
            }

            var ids = payment.Applications.Select(a => a.InvoiceId).ToList();
            var invoices = await db.Invoices.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            var customer = await db.Customers.AsNoTracking().FirstAsync(c => c.Id == payment.CustomerId, ct);
            if (customer.TaxRegime is null || customer.PostalCode is null)
            {
                return Result.Failure<string>(InvoiceErrors.IncompleteReceiver);
            }

            var documents = payment.Applications.Select(a =>
            {
                var invoice = invoices[a.InvoiceId];
                var ratio = invoice.Total == 0 ? 0 : a.Amount / invoice.Total;
                var vat = Math.Round(invoice.Vat * ratio, 2);
                return new CfdiPaymentDocument(invoice.Uuid!, invoice.Series, invoice.Folio?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, invoice.Currency, a.Partiality,
                    a.PreviousBalance, a.Amount, a.RemainingBalance, a.Amount - vat, vat);
            }).ToList();

            var folio = await folios.NextAsync(FolioKeys.Payment(fiscal.PaymentSeries), ct);
            var now = clock.LocalNow;
            var stamp = await pac.StampAsync(fiscal, new CfdiDocument("P", fiscal.PaymentSeries, folio.ToString(CultureInfo.InvariantCulture), new DateTimeOffset(now.DateTime, TimeSpan.Zero),
                null, null, "XXX", 1, 0, 0, 0, 0, fiscal.PostalCode!,
                new CfdiParty(fiscal.Rfc!, fiscal.LegalName!, fiscal.TaxRegime!, fiscal.PostalCode, null),
                new CfdiParty(customer.Rfc, customer.LegalName, customer.TaxRegime, customer.PostalCode, "CP01"),
                [new CfdiConcept("84111506", 1, "ACT", "Actividad", "Pago", 0, 0, 0, "01", 0, 0)],
                null,
                new CfdiPayment(payment.PaidAt, payment.PaymentForm, payment.Currency, payment.Amount, documents)), ct);

            if (!stamp.Success || stamp.Uuid is null || stamp.Xml is null)
            {
                return Result.Failure<string>(Error.Failure("Payments.StampFailed", $"El PAC rechazó el complemento de pago. {stamp.Error}".Trim()));
            }

            var xml = await files.SaveBytesAsync(Encoding.UTF8.GetBytes(stamp.Xml), $"{fiscal.PaymentSeries}-{folio}_{stamp.Uuid}.xml", FileContent.Xml, FileCategory.InvoiceXml, ct, nameof(Payment), payment.Id);
            if (xml.IsFailure)
            {
                return Result.Failure<string>(xml.Error);
            }

            var marked = payment.MarkComplementStamped(fiscal.PaymentSeries, folio, stamp.Uuid, xml.Value.Id, stamp.StampedAt ?? clock.UtcNow);
            if (marked.IsFailure)
            {
                return Result.Failure<string>(marked.Error);
            }

            audit.Record(AuditAction.Stamped, nameof(Payment), payment.Id, $"{fiscal.PaymentSeries}-{folio}", null, new { stamp.Uuid, stamp.Simulated });
            await db.SaveChangesAsync(ct);
            return Result.Success(stamp.Uuid);
        }, cancellationToken);
    }
}

// ---- Receivables aging ----------------------------------------------------------------------------------

public sealed record AgingRowDto(Guid CustomerId, string CustomerName, decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total, int Invoices,
    DateOnly? OldestDueOn, DateOnly? NextPromise);

public sealed record AgingDto(DateOnly AsOf, IReadOnlyList<AgingRowDto> Rows, AgingRowDto Totals);

/// <summary>Antigüedad de cartera by customer: not due, 1-30, 31-60, 61-90 and more than 90 days overdue.</summary>
public sealed record GetAgingQuery : IQuery<AgingDto>;

internal sealed class GetAgingQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<GetAgingQuery, AgingDto>
{
    public async Task<Result<AgingDto>> Handle(GetAgingQuery query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var open = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Stamped && i.Balance > 0)
            .Select(i => new { i.CustomerId, i.Balance, i.DueOn })
            .ToListAsync(cancellationToken);
        var customers = await CatalogNames.LoadAsync<Customer>(db, open.Select(i => i.CustomerId), cancellationToken);
        var promises = await db.CollectionNotes.AsNoTracking().Where(n => n.PromisedPaymentOn != null && n.PromisedPaymentOn >= today)
            .GroupBy(n => n.CustomerId).Select(g => new { CustomerId = g.Key, Next = g.Min(n => n.PromisedPaymentOn) }).ToDictionaryAsync(x => x.CustomerId, x => x.Next, cancellationToken);

        var rows = open.GroupBy(i => i.CustomerId).Select(g =>
        {
            decimal Bucket(int min, int max) => g.Where(i => today.DayNumber - i.DueOn.DayNumber >= min && today.DayNumber - i.DueOn.DayNumber <= max).Sum(i => i.Balance);
            return new AgingRowDto(g.Key, customers.NameOf(g.Key) ?? "—",
                g.Where(i => i.DueOn >= today).Sum(i => i.Balance), Bucket(1, 30), Bucket(31, 60), Bucket(61, 90), Bucket(91, int.MaxValue),
                g.Sum(i => i.Balance), g.Count(), g.Min(i => (DateOnly?)i.DueOn), promises.GetValueOrDefault(g.Key));
        }).OrderByDescending(r => r.Over90 + r.Days61To90).ThenByDescending(r => r.Total).ToList();

        var totals = new AgingRowDto(Guid.Empty, "Total", rows.Sum(r => r.Current), rows.Sum(r => r.Days1To30), rows.Sum(r => r.Days31To60), rows.Sum(r => r.Days61To90),
            rows.Sum(r => r.Over90), rows.Sum(r => r.Total), rows.Sum(r => r.Invoices), rows.Min(r => r.OldestDueOn), null);
        return new AgingDto(today, rows, totals);
    }
}

// ---- Collection notes -----------------------------------------------------------------------------------

public sealed record CollectionNoteDto(Guid Id, Guid CustomerId, string? CustomerName, Guid? InvoiceId, string? InvoiceReference, Guid? EstimateId, DateTimeOffset ContactedAt,
    ContactChannel Channel, string? ContactName, string Comment, DateOnly? PromisedPaymentOn, DateOnly? NextActionOn, string? CreatedBy);

public sealed record SearchCollectionNotesQuery(Guid? CustomerId, Guid? InvoiceId, int Page = 1, int PageSize = 50) : IQuery<PagedResult<CollectionNoteDto>>;

internal sealed class SearchCollectionNotesQueryHandler(IApplicationDbContext db) : IQueryHandler<SearchCollectionNotesQuery, PagedResult<CollectionNoteDto>>
{
    public async Task<Result<PagedResult<CollectionNoteDto>>> Handle(SearchCollectionNotesQuery query, CancellationToken cancellationToken) =>
        await db.CollectionNotes.AsNoTracking()
            .WhereIf(query.CustomerId is not null, n => n.CustomerId == query.CustomerId)
            .WhereIf(query.InvoiceId is not null, n => n.InvoiceId == query.InvoiceId)
            .OrderByDescending(n => n.ContactedAt)
            .Select(n => new CollectionNoteDto(n.Id, n.CustomerId, db.Customers.Where(c => c.Id == n.CustomerId).Select(c => c.Name).FirstOrDefault(), n.InvoiceId,
                db.Invoices.Where(i => i.Id == n.InvoiceId).Select(i => i.Series + "-" + i.Folio).FirstOrDefault(), n.EstimateId, n.ContactedAt, n.Channel, n.ContactName,
                n.Comment, n.PromisedPaymentOn, n.NextActionOn, n.CreatedBy))
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);
}

public sealed record AddCollectionNoteCommand(CollectionNoteData Data) : ICommand<Guid>;

internal sealed class AddCollectionNoteCommandHandler(IApplicationDbContext db) : ICommandHandler<AddCollectionNoteCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddCollectionNoteCommand command, CancellationToken cancellationToken)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == command.Data.CustomerId, cancellationToken))
        {
            return Domain.Common.CatalogErrors.ReferenceNotFound(CustomerErrors.Catalog);
        }

        var note = CollectionNote.Create(command.Data);
        if (note.IsFailure)
        {
            return note.Error;
        }

        db.CollectionNotes.Add(note.Value);
        await db.SaveChangesAsync(cancellationToken);
        return note.Value.Id;
    }
}

// ---- Estimates / COPADE ---------------------------------------------------------------------------------

public sealed record EstimateDto(
    Guid Id, string Folio, Guid CustomerId, string? CustomerName, Guid? ContractId, string? ContractName, DateOnly PeriodStart, DateOnly PeriodEnd, string Description,
    IReadOnlyList<Guid> TicketIds, decimal Amount, EstimateStatus Status, DateTimeOffset? SubmittedAt, DateTimeOffset? ApprovedAt, string? CopadeNumber, DateTimeOffset? CopadeAt,
    Guid? InvoiceId, string? InvoiceReference, string? RejectionReason, string? Notes, int DaysInStatus);

public sealed record SearchEstimatesQuery(EstimateStatus? Status, Guid? CustomerId, int Page = 1, int PageSize = 25) : IQuery<PagedResult<EstimateDto>>;

internal sealed class SearchEstimatesQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<SearchEstimatesQuery, PagedResult<EstimateDto>>
{
    public async Task<Result<PagedResult<EstimateDto>>> Handle(SearchEstimatesQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var page = await db.Estimates.AsNoTracking()
            .WhereIf(query.Status is not null, e => e.Status == query.Status)
            .WhereIf(query.CustomerId is not null, e => e.CustomerId == query.CustomerId)
            .OrderByDescending(e => e.FolioNumber)
            .Select(e => new EstimateDto(e.Id, e.Folio, e.CustomerId, db.Customers.Where(c => c.Id == e.CustomerId).Select(c => c.Name).FirstOrDefault(), e.ContractId,
                db.Contracts.Where(c => c.Id == e.ContractId).Select(c => c.Name).FirstOrDefault(), e.PeriodStart, e.PeriodEnd, e.Description, e.TicketIds, e.Amount, e.Status,
                e.SubmittedAt, e.ApprovedAt, e.CopadeNumber, e.CopadeAt, e.InvoiceId, db.Invoices.Where(i => i.Id == e.InvoiceId).Select(i => i.Series + "-" + i.Folio).FirstOrDefault(),
                e.RejectionReason, e.Notes, 0))
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);

        // Days stuck in the current step (where is the money?).
        var items = page.Items.Select(e => e with
        {
            DaysInStatus = (int)(now - (e.CopadeAt ?? e.ApprovedAt ?? e.SubmittedAt ?? now)).TotalDays
        }).ToList();
        return page with { Items = items };
    }
}

public sealed record SaveEstimateCommand(Guid? Id, EstimateData Data) : ICommand<Guid>;

internal sealed class SaveEstimateCommandHandler(IApplicationDbContext db, ITenantContext tenant, IFolioService folios) : ICommandHandler<SaveEstimateCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveEstimateCommand command, CancellationToken cancellationToken)
    {
        var data = command.Data;
        var tickets = await db.Tickets.AsNoTracking().Where(t => (data.TicketIds ?? new List<Guid>()).Contains(t.Id)).ToListAsync(cancellationToken);
        if (tickets.Any(t => t.CustomerId != data.CustomerId))
        {
            return InvoiceErrors.MixedCustomers;
        }

        if (data.Amount == 0 && tickets.Count > 0)
        {
            data = data with { Amount = tickets.Sum(t => t.Subtotal) };
        }

        if (command.Id is { } id)
        {
            var estimate = await db.Estimates.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (estimate is null)
            {
                return EstimateErrors.NotFound;
            }

            var updated = estimate.Update(data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            await db.SaveChangesAsync(cancellationToken);
            return estimate.Id;
        }

        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var number = await folios.NextAsync(FolioKeys.Estimate, ct);
            var created = Estimate.Create(FolioSequence.Format(tenant.Tenant.Settings.EstimatePrefix, number), number, data);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error);
            }

            db.Estimates.Add(created.Value);
            await db.SaveChangesAsync(ct);
            return Result.Success(created.Value.Id);
        }, cancellationToken);
    }
}

public enum EstimateAction
{
    Submit = 0,
    Approve = 1,
    Reject = 2,
    AssignCopade = 3
}

public sealed record ChangeEstimateStatusCommand(Guid Id, EstimateAction Action, string? Reason, string? CopadeNumber) : ICommand;

internal sealed class ChangeEstimateStatusCommandHandler(IApplicationDbContext db, ITenantClock clock, IAuditLog audit) : ICommandHandler<ChangeEstimateStatusCommand>
{
    public async Task<Result> Handle(ChangeEstimateStatusCommand command, CancellationToken cancellationToken)
    {
        var estimate = await db.Estimates.FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken);
        if (estimate is null)
        {
            return EstimateErrors.NotFound;
        }

        var now = clock.UtcNow;
        var result = command.Action switch
        {
            EstimateAction.Submit => estimate.Submit(now),
            EstimateAction.Approve => estimate.Approve(now),
            EstimateAction.Reject => estimate.Reject(command.Reason ?? string.Empty),
            _ => estimate.AssignCopade(command.CopadeNumber ?? string.Empty, now)
        };

        if (result.IsFailure)
        {
            return result;
        }

        if (command.Action == EstimateAction.AssignCopade && estimate.InvoiceId is { } invoiceId)
        {
            (await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken))?.SetCopadeNumber(estimate.CopadeNumber);
        }

        audit.Record(AuditAction.StatusChanged, nameof(Estimate), estimate.Id, $"{estimate.Folio} → {estimate.Status}", command.Reason);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Invoice draft from an estimate (its tickets, with the COPADE number).</summary>
public sealed record InvoiceEstimateCommand(Guid Id, bool Stamp) : ICommand<Guid>;

internal sealed class InvoiceEstimateCommandHandler(IApplicationDbContext db, ICommandHandler<CreateInvoiceFromTicketsCommand, Guid> invoices) : ICommandHandler<InvoiceEstimateCommand, Guid>
{
    public async Task<Result<Guid>> Handle(InvoiceEstimateCommand command, CancellationToken cancellationToken)
    {
        var estimate = await db.Estimates.AsNoTracking().FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken);
        if (estimate is null)
        {
            return EstimateErrors.NotFound;
        }

        if (estimate.Status is not (EstimateStatus.Approved or EstimateStatus.CopadeAssigned))
        {
            return EstimateErrors.InvalidTransition;
        }

        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => estimate.TicketIds.Contains(t.Id) && (t.Status == TicketStatus.Signed || t.Status == TicketStatus.Approved))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        return await invoices.Handle(new CreateInvoiceFromTicketsCommand(tickets, null, null, null, estimate.CopadeNumber, estimate.Id,
            $"Estimación {estimate.Folio} ({estimate.PeriodStart:dd/MM/yyyy} - {estimate.PeriodEnd:dd/MM/yyyy})", command.Stamp), cancellationToken);
    }
}
