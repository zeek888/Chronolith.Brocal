using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;

namespace ChronoLith.Brocal.Domain.Compliance;

/// <summary>What a document type applies to.</summary>
public enum DocumentSubject
{
    Unit = 0,
    Employee = 1
}

/// <summary>
/// Kind of permit, certification or course (permiso SCT, póliza RC, dictamen ASEA, DC-3, H2S, examen médico, SSPA…)
/// with its default validity and how early to warn before it expires.
/// </summary>
public sealed class DocumentType : CatalogAggregate
{
    private DocumentType()
    {
    }

    public DocumentSubject AppliesTo { get; private set; }

    /// <summary>Issuing authority or body (SCT, SEMARNAT, ASEA, STPS, Pemex, aseguradora…).</summary>
    public string? Authority { get; private set; }

    /// <summary>Default validity used to suggest the expiry date.</summary>
    public int? ValidityMonths { get; private set; }

    /// <summary>Overrides the company warning window for this type.</summary>
    public int? WarningDays { get; private set; }

    /// <summary>Required for every active unit or employee (shows as missing when absent).</summary>
    public bool IsMandatory { get; private set; }

    public static Result<DocumentType> Create(DocumentTypeData data)
    {
        var type = new DocumentType();
        var result = type.Update(data);
        return result.IsFailure ? result.Error : type;
    }

    public Result Update(DocumentTypeData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if (data.ValidityMonths is < 1 or > 240 || data.WarningDays is < 1 or > 365)
        {
            return ComplianceErrors.InvalidValidity;
        }

        AppliesTo = data.AppliesTo;
        Authority = string.IsNullOrWhiteSpace(data.Authority) ? null : data.Authority.Trim();
        ValidityMonths = data.ValidityMonths;
        WarningDays = data.WarningDays;
        IsMandatory = data.IsMandatory;
        return Result.Success();
    }
}

public sealed record DocumentTypeData(
    string Code,
    string Name,
    DocumentSubject AppliesTo,
    string? Authority,
    int? ValidityMonths,
    int? WarningDays,
    bool IsMandatory,
    string? Notes);

/// <summary>
/// A permit or certification of a unit, or a course/exam/document of an employee, with its file and expiry.
/// The traffic light is computed against the company warning window (or the one of its type).
/// </summary>
public sealed class ComplianceDocument : TenantAggregateRoot
{
    private ComplianceDocument()
    {
    }

    public DocumentSubject Subject { get; private set; }

    /// <summary>Unit or employee id.</summary>
    public Guid SubjectId { get; private set; }

    public Guid DocumentTypeId { get; private set; }

    /// <summary>Number of the permit, policy or DC-3 folio.</summary>
    public string? Number { get; private set; }

    /// <summary>Issuer, insurer or instructor.</summary>
    public string? Issuer { get; private set; }

    public DateOnly? IssuedOn { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>Duration of the course in hours (DC-3).</summary>
    public decimal? Hours { get; private set; }

    public Guid? FileId { get; private set; }

    public string? Notes { get; private set; }

    public static Result<ComplianceDocument> Create(DocumentSubject subject, Guid subjectId, ComplianceDocumentData data)
    {
        var document = new ComplianceDocument { Subject = subject, SubjectId = subjectId };
        var result = document.Update(data);
        return result.IsFailure ? result.Error : document;
    }

    public Result Update(ComplianceDocumentData data)
    {
        if (data.ExpiresOn is not null && data.IssuedOn is not null && data.ExpiresOn < data.IssuedOn)
        {
            return ComplianceErrors.InvalidDates;
        }

        if (data.Hours is < 0 or > 10000)
        {
            return ComplianceErrors.InvalidHours;
        }

        DocumentTypeId = data.DocumentTypeId;
        Number = string.IsNullOrWhiteSpace(data.Number) ? null : data.Number.Trim();
        Issuer = string.IsNullOrWhiteSpace(data.Issuer) ? null : data.Issuer.Trim();
        IssuedOn = data.IssuedOn;
        ExpiresOn = data.ExpiresOn;
        Hours = data.Hours;
        FileId = data.FileId;
        Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();
        return Result.Success();
    }

    public ComplianceStatus StatusOn(DateOnly today, int warningDays) => ComplianceRules.Evaluate(ExpiresOn, today, warningDays);
}

public sealed record ComplianceDocumentData(
    Guid DocumentTypeId,
    string? Number,
    string? Issuer,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    decimal? Hours,
    Guid? FileId,
    string? Notes);

/// <summary>
/// Documents a customer or contract demands from the personnel and units that enter its sites (Pemex SSPA
/// requirements, for instance). It drives the compliance matrix and the document package.
/// </summary>
public sealed class ComplianceRequirement : CatalogAggregate
{
    private List<Guid> _employeeDocumentTypeIds = [];
    private List<Guid> _unitDocumentTypeIds = [];

    private ComplianceRequirement()
    {
    }

    public Guid? CustomerId { get; private set; }

    public Guid? ContractId { get; private set; }

    public IReadOnlyList<Guid> EmployeeDocumentTypeIds => _employeeDocumentTypeIds;

    public IReadOnlyList<Guid> UnitDocumentTypeIds => _unitDocumentTypeIds;

    public static Result<ComplianceRequirement> Create(ComplianceRequirementData data)
    {
        var requirement = new ComplianceRequirement();
        var result = requirement.Update(data);
        return result.IsFailure ? result.Error : requirement;
    }

    public Result Update(ComplianceRequirementData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if ((data.EmployeeDocumentTypeIds?.Count ?? 0) + (data.UnitDocumentTypeIds?.Count ?? 0) == 0)
        {
            return ComplianceErrors.EmptyRequirement;
        }

        CustomerId = data.CustomerId;
        ContractId = data.ContractId;
        _employeeDocumentTypeIds = (data.EmployeeDocumentTypeIds ?? []).Distinct().ToList();
        _unitDocumentTypeIds = (data.UnitDocumentTypeIds ?? []).Distinct().ToList();
        return Result.Success();
    }
}

public sealed record ComplianceRequirementData(
    string Code,
    string Name,
    Guid? CustomerId,
    Guid? ContractId,
    IReadOnlyList<Guid>? EmployeeDocumentTypeIds,
    IReadOnlyList<Guid>? UnitDocumentTypeIds,
    string? Notes);

public static class ComplianceErrors
{
    public static readonly Error NotFound = Error.NotFound("Compliance.NotFound", "El documento no existe.");

    public static readonly Error InvalidDates = Error.Validation("Compliance.InvalidDates", "El vencimiento no puede ser anterior a la emisión.");

    public static readonly Error InvalidHours = Error.Validation("Compliance.InvalidHours", "Las horas del curso no son válidas.");

    public static readonly Error InvalidValidity = Error.Validation("Compliance.InvalidValidity", "Vigencia de 1 a 240 meses y aviso de 1 a 365 días.");

    public static readonly Error WrongSubject = Error.Validation("Compliance.WrongSubject", "El tipo de documento no aplica a este registro.");

    public static readonly Error EmptyRequirement = Error.Validation("Compliance.EmptyRequirement", "Selecciona al menos un tipo de documento.");
}
