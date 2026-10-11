using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.Fiscal;

public enum PaymentComplementStatus
{
    /// <summary>Every invoice paid is PUE: no payment complement is needed.</summary>
    NotRequired = 0,
    Pending = 1,
    Stamped = 2
}

/// <summary>Part of a payment applied to one invoice (parcialidad).</summary>
public sealed class PaymentApplication : TenantEntity
{
    private PaymentApplication()
    {
    }

    internal PaymentApplication(Guid invoiceId, decimal amount, int partiality, decimal previousBalance)
    {
        InvoiceId = invoiceId;
        Amount = amount;
        Partiality = partiality;
        PreviousBalance = previousBalance;
        RemainingBalance = Money.Round(previousBalance - amount);
    }

    public Guid PaymentId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public decimal Amount { get; private set; }

    public int Partiality { get; private set; }

    public decimal PreviousBalance { get; private set; }

    public decimal RemainingBalance { get; private set; }
}

public sealed record PaymentApplicationData(Guid InvoiceId, decimal Amount, int Partiality, decimal PreviousBalance, bool InvoiceIsPpd);

/// <summary>
/// Cobro recibido de un cliente, aplicado a una o varias facturas. Payments of PPD invoices require a
/// "complemento de recepción de pagos" (CFDI tipo P), stamped by the PAC.
/// </summary>
public sealed class Payment : TenantAggregateRoot
{
    private readonly List<PaymentApplication> _applications = [];

    private Payment()
    {
    }

    public Guid CustomerId { get; private set; }

    public DateTimeOffset PaidAt { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "MXN";

    /// <summary>c_FormaPago (03 transferencia, 02 cheque…).</summary>
    public string PaymentForm { get; private set; } = "03";

    public string? Reference { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyList<PaymentApplication> Applications => _applications;

    public PaymentComplementStatus ComplementStatus { get; private set; }

    public string? ComplementSeries { get; private set; }

    public long? ComplementFolio { get; private set; }

    public string? ComplementUuid { get; private set; }

    public Guid? ComplementXmlFileId { get; private set; }

    public DateTimeOffset? ComplementStampedAt { get; private set; }

    public static Result<Payment> Register(Guid customerId, DateTimeOffset paidAt, string currency, string paymentForm, string? reference, string? notes, IReadOnlyList<PaymentApplicationData> applications)
    {
        if (applications.Count == 0 || applications.Any(a => a.Amount <= 0))
        {
            return PaymentErrors.NoApplications;
        }

        if (string.IsNullOrWhiteSpace(paymentForm) || paymentForm == "99")
        {
            return PaymentErrors.InvalidForm;
        }

        var payment = new Payment
        {
            CustomerId = customerId,
            PaidAt = paidAt,
            Currency = currency,
            PaymentForm = paymentForm.Trim(),
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            ComplementStatus = applications.Any(a => a.InvoiceIsPpd) ? PaymentComplementStatus.Pending : PaymentComplementStatus.NotRequired
        };

        payment._applications.AddRange(applications.Select(a => new PaymentApplication(a.InvoiceId, Money.Round(a.Amount), a.Partiality, a.PreviousBalance)));
        payment.Amount = Money.Round(payment._applications.Sum(a => a.Amount));
        return payment;
    }

    public Result MarkComplementStamped(string series, long folio, string uuid, Guid xmlFileId, DateTimeOffset at)
    {
        if (ComplementStatus != PaymentComplementStatus.Pending)
        {
            return PaymentErrors.ComplementNotPending;
        }

        ComplementSeries = series;
        ComplementFolio = folio;
        ComplementUuid = uuid.ToUpperInvariant();
        ComplementXmlFileId = xmlFileId;
        ComplementStampedAt = at;
        ComplementStatus = PaymentComplementStatus.Stamped;
        return Result.Success();
    }
}

public static class PaymentErrors
{
    public static readonly Error NotFound = Error.NotFound("Payments.NotFound", "El cobro no existe.");

    public static readonly Error NoApplications = Error.Validation("Payments.NoApplications", "Aplica el cobro al menos a una factura con importe mayor a cero.");

    public static readonly Error InvalidForm = Error.Validation("Payments.InvalidForm", "Indica la forma de pago real (03 transferencia, 02 cheque…).");

    public static readonly Error ComplementNotPending = Error.Conflict("Payments.ComplementNotPending", "El cobro no requiere complemento o ya fue timbrado.");

    public static readonly Error ComplementStamped = Error.Conflict("Payments.ComplementStamped", "El complemento de pago ya fue timbrado; no se puede eliminar el cobro.");

    public static readonly Error CustomerMismatch = Error.Validation("Payments.CustomerMismatch", "Las facturas deben ser del cliente del cobro.");
}

public enum EstimateStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    CopadeAssigned = 3,
    Invoiced = 4,
    Paid = 5,
    Rejected = 6
}

public sealed record EstimateData(
    Guid CustomerId,
    Guid? ContractId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Description,
    IReadOnlyList<Guid>? TicketIds,
    decimal Amount,
    string? Notes);

/// <summary>
/// Estimación de un periodo (Pemex): se integra con boletas, se envía, se aprueba y recibe su número COPADE;
/// después se factura y se cobra. The whole chain is tracked to know where each peso is stuck.
/// </summary>
public sealed class Estimate : TenantAggregateRoot, IAuditReference
{
    private List<Guid> _ticketIds = [];

    private Estimate()
    {
    }

    public string Folio { get; private set; } = null!;

    public long FolioNumber { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid? ContractId { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public string Description { get; private set; } = null!;

    public IReadOnlyList<Guid> TicketIds => _ticketIds;

    /// <summary>Amount before VAT.</summary>
    public decimal Amount { get; private set; }

    public EstimateStatus Status { get; private set; } = EstimateStatus.Draft;

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public string? CopadeNumber { get; private set; }

    public DateTimeOffset? CopadeAt { get; private set; }

    public Guid? InvoiceId { get; private set; }

    public string? RejectionReason { get; private set; }

    public string? Notes { get; private set; }

    public string AuditReference => Folio;

    public static Result<Estimate> Create(string folio, long folioNumber, EstimateData data)
    {
        var estimate = new Estimate { Folio = folio, FolioNumber = folioNumber };
        var result = estimate.Apply(data);
        return result.IsFailure ? result.Error : estimate;
    }

    public Result Update(EstimateData data)
    {
        if (Status is not (EstimateStatus.Draft or EstimateStatus.Rejected))
        {
            return EstimateErrors.NotEditable;
        }

        return Apply(data);
    }

    public Result Submit(DateTimeOffset at) => Move(EstimateStatus.Submitted, [EstimateStatus.Draft, EstimateStatus.Rejected], () => SubmittedAt = at);

    public Result Approve(DateTimeOffset at) => Move(EstimateStatus.Approved, [EstimateStatus.Submitted], () => ApprovedAt = at);

    public Result Reject(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return EstimateErrors.ReasonRequired;
        }

        return Move(EstimateStatus.Rejected, [EstimateStatus.Submitted, EstimateStatus.Approved], () => RejectionReason = reason.Trim());
    }

    public Result AssignCopade(string copadeNumber, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(copadeNumber) || copadeNumber.Trim().Length > 40)
        {
            return EstimateErrors.CopadeRequired;
        }

        return Move(EstimateStatus.CopadeAssigned, [EstimateStatus.Approved, EstimateStatus.Submitted], () =>
        {
            CopadeNumber = copadeNumber.Trim();
            CopadeAt = at;
            ApprovedAt ??= at;
        });
    }

    public Result LinkInvoice(Guid invoiceId) => Move(EstimateStatus.Invoiced, [EstimateStatus.CopadeAssigned, EstimateStatus.Approved], () => InvoiceId = invoiceId);

    public void UnlinkInvoice()
    {
        if (Status == EstimateStatus.Invoiced)
        {
            InvoiceId = null;
            Status = CopadeNumber is null ? EstimateStatus.Approved : EstimateStatus.CopadeAssigned;
        }
    }

    public void MarkPaid()
    {
        if (Status == EstimateStatus.Invoiced)
        {
            Status = EstimateStatus.Paid;
        }
    }

    private Result Move(EstimateStatus to, EstimateStatus[] from, Action apply)
    {
        if (!from.Contains(Status))
        {
            return EstimateErrors.InvalidTransition;
        }

        apply();
        Status = to;
        return Result.Success();
    }

    private Result Apply(EstimateData data)
    {
        if (data.PeriodEnd < data.PeriodStart)
        {
            return EstimateErrors.InvalidPeriod;
        }

        if (string.IsNullOrWhiteSpace(data.Description) || data.Description.Trim().Length > 500)
        {
            return EstimateErrors.DescriptionRequired;
        }

        if (data.Amount < 0)
        {
            return EstimateErrors.InvalidAmount;
        }

        CustomerId = data.CustomerId;
        ContractId = data.ContractId;
        PeriodStart = data.PeriodStart;
        PeriodEnd = data.PeriodEnd;
        Description = data.Description.Trim();
        _ticketIds = (data.TicketIds ?? []).Distinct().ToList();
        Amount = Money.Round(data.Amount);
        Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();
        return Result.Success();
    }
}

public static class EstimateErrors
{
    public static readonly Error NotFound = Error.NotFound("Estimates.NotFound", "La estimación no existe.");

    public static readonly Error NotEditable = Error.Conflict("Estimates.NotEditable", "La estimación ya fue enviada.");

    public static readonly Error InvalidTransition = Error.Conflict("Estimates.InvalidTransition", "Cambio de estatus no permitido.");

    public static readonly Error InvalidPeriod = Error.Validation("Estimates.InvalidPeriod", "El fin del periodo no puede ser anterior al inicio.");

    public static readonly Error DescriptionRequired = Error.Validation("Estimates.DescriptionRequired", "Describe la estimación (máximo 500 caracteres).");

    public static readonly Error InvalidAmount = Error.Validation("Estimates.InvalidAmount", "El importe no puede ser negativo.");

    public static readonly Error ReasonRequired = Error.Validation("Estimates.ReasonRequired", "Indica el motivo del rechazo.");

    public static readonly Error CopadeRequired = Error.Validation("Estimates.CopadeRequired", "Captura el número de COPADE (máximo 40 caracteres).");
}

public enum ContactChannel
{
    Phone = 0,
    Email = 1,
    WhatsApp = 2,
    Visit = 3,
    Portal = 4
}

public sealed record CollectionNoteData(
    Guid CustomerId,
    Guid? InvoiceId,
    Guid? EstimateId,
    DateTimeOffset ContactedAt,
    ContactChannel Channel,
    string? ContactName,
    string Comment,
    DateOnly? PromisedPaymentOn,
    DateOnly? NextActionOn);

/// <summary>Bitácora de cobranza: llamadas, correos y promesas de pago.</summary>
public sealed class CollectionNote : TenantAggregateRoot
{
    private CollectionNote()
    {
    }

    public Guid CustomerId { get; private set; }

    public Guid? InvoiceId { get; private set; }

    public Guid? EstimateId { get; private set; }

    public DateTimeOffset ContactedAt { get; private set; }

    public ContactChannel Channel { get; private set; }

    public string? ContactName { get; private set; }

    public string Comment { get; private set; } = null!;

    public DateOnly? PromisedPaymentOn { get; private set; }

    public DateOnly? NextActionOn { get; private set; }

    public static Result<CollectionNote> Create(CollectionNoteData data)
    {
        if (string.IsNullOrWhiteSpace(data.Comment) || data.Comment.Trim().Length > 2000)
        {
            return Error.Validation("Collections.CommentRequired", "Escribe el comentario del seguimiento (máximo 2000 caracteres).");
        }

        return new CollectionNote
        {
            CustomerId = data.CustomerId,
            InvoiceId = data.InvoiceId,
            EstimateId = data.EstimateId,
            ContactedAt = data.ContactedAt,
            Channel = data.Channel,
            ContactName = string.IsNullOrWhiteSpace(data.ContactName) ? null : data.ContactName.Trim(),
            Comment = data.Comment.Trim(),
            PromisedPaymentOn = data.PromisedPaymentOn,
            NextActionOn = data.NextActionOn
        };
    }
}
