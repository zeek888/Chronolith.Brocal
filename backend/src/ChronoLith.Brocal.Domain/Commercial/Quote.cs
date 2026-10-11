using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.Commercial;

public enum QuoteStatus
{
    Draft = 0,
    Sent = 1,
    Accepted = 2,
    Rejected = 3,
    Expired = 4,
    Converted = 5
}

public sealed class QuoteLine : TenantEntity
{
    private QuoteLine()
    {
    }

    internal QuoteLine(int order, QuoteLineData data)
    {
        Order = order;
        ServiceId = data.ServiceId;
        Description = data.Description.Trim();
        Quantity = data.Quantity;
        UnitName = data.UnitName.Trim();
        UnitPrice = data.UnitPrice;
        DiscountPercent = data.DiscountPercent;
        Gross = Money.Round(data.Quantity * data.UnitPrice);
        Discount = Money.Round(Gross * data.DiscountPercent / 100);
        Amount = Gross - Discount;
        VatRate = data.VatRate;
        Vat = Money.Round(Amount * data.VatRate);
    }

    public Guid QuoteId { get; private set; }

    public int Order { get; private set; }

    public Guid ServiceId { get; private set; }

    public string Description { get; private set; } = null!;

    public decimal Quantity { get; private set; }

    public string UnitName { get; private set; } = null!;

    public decimal UnitPrice { get; private set; }

    public decimal DiscountPercent { get; private set; }

    public decimal Gross { get; private set; }

    public decimal Discount { get; private set; }

    public decimal Amount { get; private set; }

    public decimal VatRate { get; private set; }

    public decimal Vat { get; private set; }
}

public sealed record QuoteLineData(Guid ServiceId, string Description, decimal Quantity, string UnitName, decimal UnitPrice, decimal DiscountPercent, decimal VatRate);

public sealed record QuoteData(
    Guid CustomerId,
    Guid? ContractId,
    Guid? SiteId,
    Guid? ServiceTypeId,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    string Currency,
    string? Subject,
    string? Terms,
    IReadOnlyList<QuoteLineData> Lines,
    string? Notes);

public sealed record QuoteAcceptedEvent(Guid QuoteId) : IDomainEvent;

/// <summary>Cotización armada desde el tarifario; al aceptarse se convierte en orden de trabajo.</summary>
public sealed class Quote : TenantAggregateRoot, IAuditReference
{
    private readonly List<QuoteLine> _lines = [];

    private Quote()
    {
    }

    public string Folio { get; private set; } = null!;

    public long FolioNumber { get; private set; }

    public QuoteStatus Status { get; private set; } = QuoteStatus.Draft;

    public Guid CustomerId { get; private set; }

    public Guid? ContractId { get; private set; }

    public Guid? SiteId { get; private set; }

    public Guid? ServiceTypeId { get; private set; }

    public DateOnly IssuedOn { get; private set; }

    public DateOnly ValidUntil { get; private set; }

    public string Currency { get; private set; } = "MXN";

    public string? Subject { get; private set; }

    public string? Terms { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyList<QuoteLine> Lines => _lines;

    public decimal Subtotal { get; private set; }

    public decimal Discount { get; private set; }

    public decimal Vat { get; private set; }

    public decimal Total { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public string? RejectionReason { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public string AuditReference => Folio;

    public bool IsExpired(DateOnly today) => Status is QuoteStatus.Draft or QuoteStatus.Sent && ValidUntil < today;

    public static Result<Quote> Create(string folio, long folioNumber, QuoteData data)
    {
        var quote = new Quote { Folio = folio, FolioNumber = folioNumber };
        var result = quote.Apply(data);
        return result.IsFailure ? result.Error : quote;
    }

    public Result Update(QuoteData data)
    {
        if (Status is not (QuoteStatus.Draft or QuoteStatus.Sent))
        {
            return QuoteErrors.NotEditable;
        }

        return Apply(data);
    }

    public Result MarkSent(DateTimeOffset at)
    {
        if (Status is not (QuoteStatus.Draft or QuoteStatus.Sent))
        {
            return QuoteErrors.InvalidTransition;
        }

        Status = QuoteStatus.Sent;
        SentAt = at;
        return Result.Success();
    }

    public Result Accept(DateTimeOffset at, DateOnly today)
    {
        if (Status is not (QuoteStatus.Draft or QuoteStatus.Sent))
        {
            return QuoteErrors.InvalidTransition;
        }

        if (ValidUntil < today)
        {
            return QuoteErrors.Expired;
        }

        Status = QuoteStatus.Accepted;
        AcceptedAt = at;
        Raise(new QuoteAcceptedEvent(Id));
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status is not (QuoteStatus.Draft or QuoteStatus.Sent))
        {
            return QuoteErrors.InvalidTransition;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return QuoteErrors.ReasonRequired;
        }

        Status = QuoteStatus.Rejected;
        RejectionReason = reason.Trim();
        return Result.Success();
    }

    public void Expire()
    {
        if (Status is QuoteStatus.Draft or QuoteStatus.Sent)
        {
            Status = QuoteStatus.Expired;
        }
    }

    public Result MarkConverted(Guid workOrderId)
    {
        if (Status != QuoteStatus.Accepted)
        {
            return QuoteErrors.MustBeAccepted;
        }

        Status = QuoteStatus.Converted;
        WorkOrderId = workOrderId;
        return Result.Success();
    }

    private Result Apply(QuoteData data)
    {
        if (data.ValidUntil < data.IssuedOn)
        {
            return QuoteErrors.InvalidValidity;
        }

        if (data.Lines.Count == 0)
        {
            return QuoteErrors.NoLines;
        }

        if (data.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || l.DiscountPercent is < 0 or > 100 || string.IsNullOrWhiteSpace(l.Description)))
        {
            return QuoteErrors.InvalidLine;
        }

        if (data.Currency is not ("MXN" or "USD"))
        {
            return ContractErrors.InvalidCurrency;
        }

        CustomerId = data.CustomerId;
        ContractId = data.ContractId;
        SiteId = data.SiteId;
        ServiceTypeId = data.ServiceTypeId;
        IssuedOn = data.IssuedOn;
        ValidUntil = data.ValidUntil;
        Currency = data.Currency;
        Subject = string.IsNullOrWhiteSpace(data.Subject) ? null : data.Subject.Trim();
        Terms = string.IsNullOrWhiteSpace(data.Terms) ? null : data.Terms.Trim();
        Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();

        _lines.Clear();
        var order = 1;
        _lines.AddRange(data.Lines.Select(l => new QuoteLine(order++, l)));
        Subtotal = Money.Round(_lines.Sum(l => l.Gross));
        Discount = Money.Round(_lines.Sum(l => l.Discount));
        Vat = Money.Round(_lines.Sum(l => l.Vat));
        Total = Subtotal - Discount + Vat;
        return Result.Success();
    }
}

public static class QuoteErrors
{
    public static readonly Error NotFound = Error.NotFound("Quotes.NotFound", "La cotización no existe.");

    public static readonly Error NotEditable = Error.Conflict("Quotes.NotEditable", "La cotización ya fue aceptada, rechazada o convertida.");

    public static readonly Error InvalidTransition = Error.Conflict("Quotes.InvalidTransition", "Cambio de estatus no permitido.");

    public static readonly Error InvalidValidity = Error.Validation("Quotes.InvalidValidity", "La vigencia no puede terminar antes de la fecha de emisión.");

    public static readonly Error NoLines = Error.Validation("Quotes.NoLines", "Agrega al menos un concepto.");

    public static readonly Error InvalidLine = Error.Validation("Quotes.InvalidLine", "Cada concepto necesita descripción, cantidad mayor a cero, precio y descuento de 0 a 100%.");

    public static readonly Error Expired = Error.Conflict("Quotes.Expired", "La cotización ya venció.");

    public static readonly Error ReasonRequired = Error.Validation("Quotes.ReasonRequired", "Indica el motivo del rechazo.");

    public static readonly Error MustBeAccepted = Error.Conflict("Quotes.MustBeAccepted", "Solo una cotización aceptada se convierte en orden de trabajo.");

    public static readonly Error SiteRequired = Error.Validation("Quotes.SiteRequired", "Para convertirla en orden indica la locación y el tipo de servicio.");
}
