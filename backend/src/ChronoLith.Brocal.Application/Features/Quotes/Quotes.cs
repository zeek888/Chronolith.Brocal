using System.Globalization;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Automation;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Quotes;

public sealed record QuoteSummaryDto(Guid Id, string Folio, QuoteStatus Status, Guid CustomerId, string? CustomerName, string? Subject, DateOnly IssuedOn, DateOnly ValidUntil,
    decimal Total, string Currency, Guid? WorkOrderId, bool IsExpired);

public sealed record QuoteLineDto(Guid Id, int Order, Guid ServiceId, string Description, decimal Quantity, string UnitName, decimal UnitPrice, decimal DiscountPercent,
    decimal Amount, decimal VatRate, decimal Vat);

public sealed record QuoteDetailDto(QuoteSummaryDto Summary, Guid? ContractId, Guid? SiteId, Guid? ServiceTypeId, string? Terms, string? Notes, IReadOnlyList<QuoteLineDto> Lines,
    decimal Subtotal, decimal Discount, decimal Vat, DateTimeOffset? SentAt, DateTimeOffset? AcceptedAt, string? RejectionReason, string? WorkOrderFolio);

public sealed record SearchQuotesQuery(string? Search, QuoteStatus? Status, Guid? CustomerId, int Page = 1, int PageSize = 25) : IQuery<PagedResult<QuoteSummaryDto>>;

internal sealed class SearchQuotesQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<SearchQuotesQuery, PagedResult<QuoteSummaryDto>>
{
    public async Task<Result<PagedResult<QuoteSummaryDto>>> Handle(SearchQuotesQuery query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var request = new PageRequest(query.Search, query.Page, query.PageSize);
        var quotes = db.Quotes.AsNoTracking()
            .WhereIf(query.Status is not null, q => q.Status == query.Status)
            .WhereIf(query.CustomerId is not null, q => q.CustomerId == query.CustomerId);
        if (request.Term is { } term)
        {
            quotes = quotes.Where(q => q.Folio.ToLower().Contains(term) || (q.Subject != null && q.Subject.ToLower().Contains(term))
                                       || db.Customers.Any(c => c.Id == q.CustomerId && c.Name.ToLower().Contains(term)));
        }

        return await quotes.OrderByDescending(q => q.FolioNumber)
            .Select(q => new QuoteSummaryDto(q.Id, q.Folio, q.Status, q.CustomerId, db.Customers.Where(c => c.Id == q.CustomerId).Select(c => c.Name).FirstOrDefault(), q.Subject,
                q.IssuedOn, q.ValidUntil, q.Total, q.Currency, q.WorkOrderId, (q.Status == QuoteStatus.Draft || q.Status == QuoteStatus.Sent) && q.ValidUntil < today))
            .ToPagedAsync(request, cancellationToken);
    }
}

public sealed record GetQuoteQuery(Guid Id) : IQuery<QuoteDetailDto>;

internal sealed class GetQuoteQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<GetQuoteQuery, QuoteDetailDto>
{
    public async Task<Result<QuoteDetailDto>> Handle(GetQuoteQuery query, CancellationToken cancellationToken)
    {
        var quote = await db.Quotes.AsNoTracking().Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == query.Id, cancellationToken);
        if (quote is null)
        {
            return QuoteErrors.NotFound;
        }

        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == quote.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
        var order = quote.WorkOrderId is null ? null : await db.WorkOrders.AsNoTracking().Where(w => w.Id == quote.WorkOrderId).Select(w => w.Folio).FirstOrDefaultAsync(cancellationToken);
        return new QuoteDetailDto(
            new QuoteSummaryDto(quote.Id, quote.Folio, quote.Status, quote.CustomerId, customer, quote.Subject, quote.IssuedOn, quote.ValidUntil, quote.Total, quote.Currency,
                quote.WorkOrderId, quote.IsExpired(clock.Today)),
            quote.ContractId, quote.SiteId, quote.ServiceTypeId, quote.Terms, quote.Notes,
            quote.Lines.OrderBy(l => l.Order).Select(l => new QuoteLineDto(l.Id, l.Order, l.ServiceId, l.Description, l.Quantity, l.UnitName, l.UnitPrice, l.DiscountPercent,
                l.Amount, l.VatRate, l.Vat)).ToList(),
            quote.Subtotal, quote.Discount, quote.Vat, quote.SentAt, quote.AcceptedAt, quote.RejectionReason, order);
    }
}

public sealed record SaveQuoteCommand(Guid? Id, QuoteData Data) : ICommand<Guid>;

internal sealed class SaveQuoteCommandValidator : AbstractValidator<SaveQuoteCommand>
{
    public SaveQuoteCommandValidator()
    {
        RuleFor(c => c.Data.CustomerId).RequiredId("el cliente");
        RuleFor(c => c.Data.Lines).NotEmpty().WithMessage("Agrega al menos un concepto.");
        RuleFor(c => c.Data.ValidUntil).GreaterThanOrEqualTo(c => c.Data.IssuedOn);
    }
}

internal sealed class SaveQuoteCommandHandler(IApplicationDbContext db, ITenantContext tenant, IFolioService folios, IBusinessRules rules) : ICommandHandler<SaveQuoteCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveQuoteCommand command, CancellationToken cancellationToken)
    {
        var references = Result.FirstFailure(
            await References.EnsureAsync<Customer>(db, command.Data.CustomerId, CustomerErrors.Catalog, cancellationToken),
            await References.EnsureAsync<Site>(db, command.Data.SiteId, SiteErrors.Catalog, cancellationToken),
            await References.EnsureAsync<ServiceType>(db, command.Data.ServiceTypeId, ServiceTypeErrors.Catalog, cancellationToken),
            await References.EnsureAllAsync<Service>(db, command.Data.Lines.Select(l => l.ServiceId), ServiceErrors.Catalog, cancellationToken));
        if (references.IsFailure)
        {
            return references.Error;
        }

        if (command.Id is { } id)
        {
            var quote = await db.Quotes.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
            if (quote is null)
            {
                return QuoteErrors.NotFound;
            }

            var updated = quote.Update(command.Data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.Quote, quote.Id, quote.Folio, DocumentFacts.Quote(quote)), AutomationTrigger.Save, cancellationToken);
            if (checks.IsFailure)
            {
                return checks.Error;
            }

            await db.SaveChangesAsync(cancellationToken);
            return quote.Id;
        }

        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var number = await folios.NextAsync(FolioKeys.Quote, ct);
            var created = Quote.Create(FolioSequence.Format(tenant.Tenant.Settings.QuotePrefix, number), number, command.Data);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error);
            }

            var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.Quote, created.Value.Id, created.Value.Folio, DocumentFacts.Quote(created.Value)), AutomationTrigger.Save, ct);
            if (checks.IsFailure)
            {
                return Result.Failure<Guid>(checks.Error);
            }

            db.Quotes.Add(created.Value);
            await db.SaveChangesAsync(ct);
            return Result.Success(created.Value.Id);
        }, cancellationToken);
    }
}

public enum QuoteAction
{
    Send = 0,
    Accept = 1,
    Reject = 2
}

public sealed record ChangeQuoteStatusCommand(Guid Id, QuoteAction Action, string? Reason) : ICommand;

internal sealed class ChangeQuoteStatusCommandHandler(
    IApplicationDbContext db,
    ITenantClock clock,
    INotificationPublisher notifications,
    IAuditLog audit,
    IBusinessRules rules,
    IWorkflowEngine workflows) : ICommandHandler<ChangeQuoteStatusCommand>
{
    public async Task<Result> Handle(ChangeQuoteStatusCommand command, CancellationToken cancellationToken)
    {
        var quote = await db.Quotes.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == command.Id, cancellationToken);
        if (quote is null)
        {
            return QuoteErrors.NotFound;
        }

        // Sending it (or accepting it without having sent it) is the guarded moment: rules and approval workflow.
        if (command.Action == QuoteAction.Send || command.Action == QuoteAction.Accept && quote.Status == QuoteStatus.Draft)
        {
            var facts = DocumentFacts.Quote(quote);
            var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.Quote, quote.Id, quote.Folio, facts), AutomationTrigger.Send, cancellationToken);
            if (checks.IsFailure)
            {
                return checks;
            }

            var gate = await workflows.GateAsync(new WorkflowSubject(AutomationDocument.Quote, quote.Id, quote.Folio, facts), GatePersistence.Isolated, cancellationToken);
            if (!gate.Passed)
            {
                return gate.ToError();
            }
        }

        var result = command.Action switch
        {
            QuoteAction.Send => quote.MarkSent(clock.UtcNow),
            QuoteAction.Accept => quote.Accept(clock.UtcNow, clock.Today),
            _ => quote.Reject(command.Reason ?? string.Empty)
        };

        if (result.IsFailure)
        {
            return result;
        }

        if (command.Action == QuoteAction.Accept)
        {
            await notifications.PublishAsync(new NotificationMessage(NotificationEvent.QuoteAccepted, NotificationSeverity.Success,
                $"Cotización {quote.Folio} aceptada", $"{quote.Subject ?? "Cotización"} por {quote.Total:N2} {quote.Currency}. Conviértela en orden de trabajo.", $"cotizaciones/{quote.Id}"),
                cancellationToken);
        }

        if (command.Action == QuoteAction.Reject)
        {
            await workflows.CancelPendingAsync(AutomationDocument.Quote, quote.Id, "La cotización fue rechazada por el cliente.", cancellationToken);
        }

        audit.Record(AuditAction.StatusChanged, nameof(Quote), quote.Id, $"{quote.Folio} → {quote.Status}", command.Reason);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Converts an accepted quote into a work order with its services as planned quantities.</summary>
public sealed record ConvertQuoteCommand(Guid Id, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd, Guid? SiteId, Guid? ServiceTypeId) : ICommand<Guid>;

internal sealed class ConvertQuoteCommandHandler(IApplicationDbContext db, ITenantContext tenant, IFolioService folios) : ICommandHandler<ConvertQuoteCommand, Guid>
{
    public async Task<Result<Guid>> Handle(ConvertQuoteCommand command, CancellationToken cancellationToken)
    {
        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var quote = await db.Quotes.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == command.Id, ct);
            if (quote is null)
            {
                return Result.Failure<Guid>(QuoteErrors.NotFound);
            }

            var siteId = command.SiteId ?? quote.SiteId;
            var serviceTypeId = command.ServiceTypeId ?? quote.ServiceTypeId;
            if (siteId is null || serviceTypeId is null)
            {
                return Result.Failure<Guid>(QuoteErrors.SiteRequired);
            }

            var number = await folios.NextAsync(FolioKeys.WorkOrder, ct);
            var order = WorkOrder.Create(FolioSequence.Format(tenant.Tenant.Settings.WorkOrderPrefix, number), number, new WorkOrderData(
                quote.CustomerId, quote.ContractId, siteId.Value, serviceTypeId.Value, quote.Subject ?? $"Servicio cotizado {quote.Folio}", WorkOrderPriority.Normal,
                command.ScheduledStart, command.ScheduledEnd, [], null, [],
                quote.Lines.Select(l => new PlannedService(l.ServiceId, l.Quantity)).ToList(), quote.Terms, $"Cotización {quote.Folio}"), quote.Id);
            if (order.IsFailure)
            {
                return Result.Failure<Guid>(order.Error);
            }

            var converted = quote.MarkConverted(order.Value.Id);
            if (converted.IsFailure)
            {
                return Result.Failure<Guid>(converted.Error);
            }

            db.WorkOrders.Add(order.Value);
            await db.SaveChangesAsync(ct);
            return Result.Success(order.Value.Id);
        }, cancellationToken);
    }
}

public sealed record GetQuotePdfQuery(Guid Id) : IQuery<FileContent>;

internal sealed class GetQuotePdfQueryHandler(IApplicationDbContext db, IPdfRenderer renderer, IDocumentBrandingProvider branding) : IQueryHandler<GetQuotePdfQuery, FileContent>
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public async Task<Result<FileContent>> Handle(GetQuotePdfQuery query, CancellationToken cancellationToken)
    {
        var quote = await db.Quotes.AsNoTracking().Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == query.Id, cancellationToken);
        if (quote is null)
        {
            return QuoteErrors.NotFound;
        }

        var customer = await db.Customers.AsNoTracking().FirstAsync(c => c.Id == quote.CustomerId, cancellationToken);
        var site = quote.SiteId is null ? null : await db.Sites.AsNoTracking().Where(s => s.Id == quote.SiteId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken);
        var totals = new List<DocumentField> { new("Subtotal", quote.Subtotal.ToString("C2", Mx)) };
        if (quote.Discount > 0)
        {
            totals.Add(new("Descuento", quote.Discount.ToString("C2", Mx)));
        }

        totals.Add(new("IVA", quote.Vat.ToString("C2", Mx)));
        totals.Add(new($"Total {quote.Currency}", quote.Total.ToString("C2", Mx)));

        var document = new BusinessDocument(
            "Cotización",
            quote.Folio,
            null,
            quote.IssuedOn.ToString("dd 'de' MMMM 'de' yyyy", Mx),
            [("Cliente", [new("Cliente", customer.Name), new("RFC", customer.Rfc), new("Atención", customer.Contacts.FirstOrDefault()?.Name), new("Locación", site),
                new("Asunto", quote.Subject), new("Vigencia", quote.ValidUntil.ToString("dd/MM/yyyy", Mx))])],
            [new DocumentTable("Conceptos", ["Concepto", "Cantidad", "Unidad", "P. unitario", "Desc.", "Importe"],
                quote.Lines.OrderBy(l => l.Order).Select(l => (IReadOnlyList<string>)
                [
                    l.Description, l.Quantity.ToString("#,##0.###", Mx), l.UnitName, l.UnitPrice.ToString("C2", Mx), l.DiscountPercent == 0 ? "" : $"{l.DiscountPercent:0.#}%",
                    l.Amount.ToString("C2", Mx)
                ]).ToList(), [false, true, false, true, true, true])],
            new DocumentTotals(totals),
            quote.Terms is null ? [] : [("Términos y condiciones", quote.Terms)],
            [],
            [new DocumentSignature("Atentamente", null, null, null, null)],
            quote.Status == QuoteStatus.Rejected ? "RECHAZADA" : quote.Status == QuoteStatus.Expired ? "VENCIDA" : null);

        return new FileContent($"Cotizacion_{quote.Folio}.pdf", FileContent.Pdf, renderer.Render(document, await branding.GetAsync(cancellationToken)));
    }
}
