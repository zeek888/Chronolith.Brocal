using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.Fiscal;

public enum InvoiceStatus
{
    Draft = 0,
    Stamped = 1,
    CancellationPending = 2,
    Cancelled = 3
}

/// <summary>Tipo de comprobante: I (ingreso) o T (traslado, Carta Porte sin cobro).</summary>
public enum CfdiType
{
    Income = 0,
    Transfer = 1
}

/// <summary>Concepto del CFDI (with the SAT codes frozen at the moment of invoicing).</summary>
public sealed class InvoiceLine : TenantEntity
{
    private InvoiceLine()
    {
    }

    internal InvoiceLine(int order, InvoiceLineData data)
    {
        Order = order;
        TicketId = data.TicketId;
        ServiceId = data.ServiceId;
        SatProductCode = data.SatProductCode.Trim();
        SatUnitCode = data.SatUnitCode.Trim().ToUpperInvariant();
        UnitName = data.UnitName.Trim();
        Description = data.Description.Trim();
        Quantity = data.Quantity;
        UnitPrice = data.UnitPrice;
        Amount = Money.Round(data.Quantity * data.UnitPrice);
        Discount = Money.Round(data.Discount);
        TaxObject = string.IsNullOrWhiteSpace(data.TaxObject) ? "02" : data.TaxObject;
        VatRate = data.VatRate;
        Vat = TaxObject == "02" ? Money.Round((Amount - Discount) * VatRate) : 0;
    }

    public Guid InvoiceId { get; private set; }

    public int Order { get; private set; }

    public Guid? TicketId { get; private set; }

    public Guid? ServiceId { get; private set; }

    public string SatProductCode { get; private set; } = null!;

    public string SatUnitCode { get; private set; } = null!;

    public string UnitName { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal Amount { get; private set; }

    public decimal Discount { get; private set; }

    public string TaxObject { get; private set; } = "02";

    public decimal VatRate { get; private set; }

    public decimal Vat { get; private set; }
}

public sealed record InvoiceLineData(
    Guid? TicketId,
    Guid? ServiceId,
    string SatProductCode,
    string SatUnitCode,
    string UnitName,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    string? TaxObject,
    decimal VatRate);

/// <summary>Receiver data copied from the customer when the invoice is created (it must not change afterwards).</summary>
public sealed record InvoiceReceiver(string Rfc, string Name, string? TaxRegime, string? PostalCode, string CfdiUse);

/// <summary>Issuer data copied from the company fiscal profile when stamping.</summary>
public sealed record InvoiceIssuer(string Rfc, string Name, string TaxRegime, string ExpeditionPostalCode);

public sealed record InvoiceDraftData(
    Guid CustomerId,
    InvoiceReceiver Receiver,
    CfdiType Type,
    string PaymentForm,
    string PaymentMethod,
    string Currency,
    decimal ExchangeRate,
    DateOnly IssueDate,
    int CreditDays,
    IReadOnlyList<InvoiceLineData> Lines,
    IReadOnlyList<Guid>? TicketIds,
    Guid? EstimateId,
    Guid? TripId,
    string? CopadeNumber,
    string? Notes);

public sealed record InvoiceStampedEvent(Guid InvoiceId) : IDomainEvent;

/// <summary>
/// Factura CFDI 4.0. It is prepared as a draft (from signed tickets or manually), stamped by the PAC (UUID, XML) and
/// can be cancelled with the SAT motive. Payments reduce its balance (receivables).
/// </summary>
public sealed class Invoice : TenantAggregateRoot, IAuditReference
{
    private readonly List<InvoiceLine> _lines = [];
    private List<Guid> _ticketIds = [];

    private Invoice()
    {
    }

    public string Series { get; private set; } = null!;

    /// <summary>Internal consecutive number, assigned when stamped (drafts have none).</summary>
    public long? Folio { get; private set; }

    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;

    public CfdiType Type { get; private set; }

    public Guid CustomerId { get; private set; }

    public InvoiceReceiver Receiver { get; private set; } = null!;

    public InvoiceIssuer? Issuer { get; private set; }

    /// <summary>c_FormaPago (99 when PPD).</summary>
    public string PaymentForm { get; private set; } = "99";

    /// <summary>PUE or PPD.</summary>
    public string PaymentMethod { get; private set; } = "PPD";

    public string Currency { get; private set; } = "MXN";

    public decimal ExchangeRate { get; private set; } = 1;

    public DateOnly IssueDate { get; private set; }

    public DateOnly DueOn { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public IReadOnlyList<Guid> TicketIds => _ticketIds;

    public Guid? EstimateId { get; private set; }

    public Guid? TripId { get; private set; }

    public string? CopadeNumber { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal Discount { get; private set; }

    public decimal Vat { get; private set; }

    public decimal Total { get; private set; }

    public string? Uuid { get; private set; }

    public DateTimeOffset? StampedAt { get; private set; }

    public string? PacProvider { get; private set; }

    public Guid? XmlFileId { get; private set; }

    public Guid? PdfFileId { get; private set; }

    /// <summary>Motivo de cancelación SAT (01 con relación, 02 sin relación, 03 no se llevó a cabo, 04 nominativa global).</summary>
    public string? CancellationMotive { get; private set; }

    public string? SubstitutionUuid { get; private set; }

    public DateTimeOffset? CancellationRequestedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public decimal PaidAmount { get; private set; }

    public decimal Balance { get; private set; }

    public DateTimeOffset? LastPaymentAt { get; private set; }

    public string? Notes { get; private set; }

    public string AuditReference => Folio is null ? $"{Series}-borrador" : $"{Series}-{Folio}";

    public bool IsOverdue(DateOnly today) => Status == InvoiceStatus.Stamped && Balance > 0 && DueOn < today;

    public static Result<Invoice> CreateDraft(string series, InvoiceDraftData data)
    {
        var invoice = new Invoice { Series = series };
        var result = invoice.Apply(data);
        return result.IsFailure ? result.Error : invoice;
    }

    public Result UpdateDraft(InvoiceDraftData data)
    {
        if (Status != InvoiceStatus.Draft)
        {
            return InvoiceErrors.NotDraft;
        }

        return Apply(data);
    }

    public Result MarkStamped(long folio, InvoiceIssuer issuer, string uuid, DateTimeOffset stampedAt, string pacProvider, Guid xmlFileId)
    {
        if (Status != InvoiceStatus.Draft)
        {
            return InvoiceErrors.NotDraft;
        }

        Folio = folio;
        Issuer = issuer;
        Uuid = uuid.ToUpperInvariant();
        StampedAt = stampedAt;
        PacProvider = pacProvider;
        XmlFileId = xmlFileId;
        Status = InvoiceStatus.Stamped;
        Balance = Type == CfdiType.Transfer ? 0 : Total;
        Raise(new InvoiceStampedEvent(Id));
        return Result.Success();
    }

    public void AttachPdf(Guid fileId) => PdfFileId = fileId;

    public Result RequestCancellation(string motive, string? substitutionUuid, DateTimeOffset at)
    {
        if (Status != InvoiceStatus.Stamped)
        {
            return InvoiceErrors.NotStamped;
        }

        if (motive is not ("01" or "02" or "03" or "04"))
        {
            return InvoiceErrors.InvalidMotive;
        }

        if (motive == "01" && string.IsNullOrWhiteSpace(substitutionUuid))
        {
            return InvoiceErrors.SubstitutionRequired;
        }

        if (PaidAmount > 0)
        {
            return InvoiceErrors.HasPayments;
        }

        CancellationMotive = motive;
        SubstitutionUuid = string.IsNullOrWhiteSpace(substitutionUuid) ? null : substitutionUuid.Trim().ToUpperInvariant();
        CancellationRequestedAt = at;
        Status = InvoiceStatus.CancellationPending;
        return Result.Success();
    }

    public void ConfirmCancellation(DateTimeOffset at)
    {
        Status = InvoiceStatus.Cancelled;
        CancelledAt = at;
        Balance = 0;
    }

    /// <summary>The SAT rejected the cancellation (the receiver did not accept it).</summary>
    public void RejectCancellation()
    {
        if (Status == InvoiceStatus.CancellationPending)
        {
            Status = InvoiceStatus.Stamped;
        }
    }

    public Result ApplyPayment(decimal amount, DateTimeOffset at)
    {
        if (Status != InvoiceStatus.Stamped || Type != CfdiType.Income)
        {
            return InvoiceErrors.NotStamped;
        }

        if (amount <= 0 || amount > Balance)
        {
            return InvoiceErrors.PaymentExceedsBalance(Balance);
        }

        PaidAmount = Money.Round(PaidAmount + amount);
        Balance = Money.Round(Total - PaidAmount);
        LastPaymentAt = at;
        return Result.Success();
    }

    public void RevertPayment(decimal amount)
    {
        PaidAmount = Math.Max(0, Money.Round(PaidAmount - amount));
        Balance = Money.Round(Total - PaidAmount);
    }

    public void SetCopadeNumber(string? copade) => CopadeNumber = string.IsNullOrWhiteSpace(copade) ? null : copade.Trim();

    private Result Apply(InvoiceDraftData data)
    {
        if (data.Lines.Count == 0)
        {
            return InvoiceErrors.NoLines;
        }

        if (data.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || l.Discount < 0 || string.IsNullOrWhiteSpace(l.Description)))
        {
            return InvoiceErrors.InvalidLine;
        }

        if (data.PaymentMethod is not ("PUE" or "PPD"))
        {
            return InvoiceErrors.InvalidPaymentMethod;
        }

        if (data.PaymentMethod == "PPD" && data.PaymentForm != "99")
        {
            return InvoiceErrors.PpdRequires99;
        }

        if (data.Currency is not ("MXN" or "USD") || data.ExchangeRate <= 0 || data.Currency == "MXN" && data.ExchangeRate != 1)
        {
            return InvoiceErrors.InvalidCurrency;
        }

        if (!MexicanIds.IsValidRfc(data.Receiver.Rfc))
        {
            return InvoiceErrors.InvalidReceiver;
        }

        if (data.CreditDays is < 0 or > 365)
        {
            return InvoiceErrors.InvalidCreditDays;
        }

        CustomerId = data.CustomerId;
        Receiver = data.Receiver;
        Type = data.Type;
        PaymentForm = data.PaymentForm;
        PaymentMethod = data.PaymentMethod;
        Currency = data.Currency;
        ExchangeRate = data.ExchangeRate;
        IssueDate = data.IssueDate;
        DueOn = data.IssueDate.AddDays(data.PaymentMethod == "PUE" ? 0 : data.CreditDays);
        _ticketIds = (data.TicketIds ?? []).Distinct().ToList();
        EstimateId = data.EstimateId;
        TripId = data.TripId;
        CopadeNumber = string.IsNullOrWhiteSpace(data.CopadeNumber) ? null : data.CopadeNumber.Trim();
        Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();

        _lines.Clear();
        var order = 1;
        _lines.AddRange(data.Lines.Select(l => new InvoiceLine(order++, l)));

        Subtotal = Money.Round(_lines.Sum(l => l.Amount));
        Discount = Money.Round(_lines.Sum(l => l.Discount));
        Vat = Money.Round(_lines.Sum(l => l.Vat));
        Total = Type == CfdiType.Transfer ? 0 : Subtotal - Discount + Vat;
        Balance = 0;
        return Result.Success();
    }
}

public static class InvoiceErrors
{
    public static readonly Error NotFound = Error.NotFound("Invoices.NotFound", "La factura no existe.");

    public static readonly Error NotDraft = Error.Conflict("Invoices.NotDraft", "La factura ya fue timbrada.");

    public static readonly Error NotStamped = Error.Conflict("Invoices.NotStamped", "La factura no está timbrada.");

    public static readonly Error NoLines = Error.Validation("Invoices.NoLines", "La factura necesita al menos un concepto.");

    public static readonly Error InvalidLine = Error.Validation("Invoices.InvalidLine", "Cada concepto necesita descripción, cantidad mayor a cero y precio.");

    public static readonly Error InvalidPaymentMethod = Error.Validation("Invoices.InvalidPaymentMethod", "El método de pago es PUE o PPD.");

    public static readonly Error PpdRequires99 = Error.Validation("Invoices.PpdRequires99", "Con método PPD la forma de pago debe ser 99 (por definir).");

    public static readonly Error InvalidCurrency = Error.Validation("Invoices.InvalidCurrency", "Moneda MXN (tipo de cambio 1) o USD con tipo de cambio.");

    public static readonly Error InvalidReceiver = Error.Validation("Invoices.InvalidReceiver", "El RFC del receptor no es válido.");

    public static readonly Error InvalidCreditDays = Error.Validation("Invoices.InvalidCreditDays", "Los días de crédito van de 0 a 365.");

    public static readonly Error IncompleteReceiver = Error.Validation(
        "Invoices.IncompleteReceiver", "Para CFDI 4.0 el cliente necesita régimen fiscal y código postal del domicilio fiscal.");

    public static readonly Error IssuerIncomplete = Error.Validation(
        "Invoices.IssuerIncomplete", "Completa los datos fiscales de la empresa (RFC, razón social, régimen y código postal) antes de timbrar.");

    public static readonly Error InvalidMotive = Error.Validation("Invoices.InvalidMotive", "El motivo de cancelación es 01, 02, 03 o 04.");

    public static readonly Error SubstitutionRequired = Error.Validation("Invoices.SubstitutionRequired", "El motivo 01 requiere el UUID de la factura que sustituye.");

    public static readonly Error HasPayments = Error.Conflict("Invoices.HasPayments", "La factura tiene pagos aplicados: cancélalos primero.");

    public static readonly Error MixedCustomers = Error.Validation("Invoices.MixedCustomers", "Todas las boletas deben ser del mismo cliente.");

    public static readonly Error StampFailed = Error.Failure("Invoices.StampFailed", "El PAC rechazó el timbrado.");

    public static Error PaymentExceedsBalance(decimal balance) =>
        Error.Validation("Invoices.PaymentExceedsBalance", $"El importe debe ser mayor a cero y no mayor al saldo ({balance:N2}).");
}
