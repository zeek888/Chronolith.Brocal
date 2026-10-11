using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.People;

public enum EmployeeCategory
{
    Operator = 0,
    Helper = 1,
    Supervisor = 2,
    Technician = 3,
    Driver = 4,
    Hse = 5,
    Office = 6,
    Other = 7
}

public sealed record EmployeeData(
    string Code,
    string FirstName,
    string LastName,
    string? Curp,
    string? Rfc,
    string? Nss,
    string? Position,
    EmployeeCategory Category,
    DateOnly? HiredOn,
    string? Phone,
    string? Email,
    string? EmergencyContact,
    string? BloodType,
    string? LicenseNumber,
    string? LicenseType,
    DateOnly? LicenseExpiresOn,
    decimal? HourlyCost,
    Guid? PhotoFileId,
    string? Notes);

/// <summary>Expediente de personal: datos de identificación, licencia federal y costo por hora (costeo de boletas).</summary>
public sealed class Employee : CatalogAggregate
{
    private Employee()
    {
    }

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public string? Curp { get; private set; }

    public string? Rfc { get; private set; }

    /// <summary>Número de seguridad social (IMSS).</summary>
    public string? Nss { get; private set; }

    public string? Position { get; private set; }

    public EmployeeCategory Category { get; private set; }

    public DateOnly? HiredOn { get; private set; }

    public DateOnly? TerminatedOn { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? EmergencyContact { get; private set; }

    public string? BloodType { get; private set; }

    /// <summary>Licencia federal de conducir (operadores de autotanque, Carta Porte).</summary>
    public string? LicenseNumber { get; private set; }

    public string? LicenseType { get; private set; }

    public DateOnly? LicenseExpiresOn { get; private set; }

    /// <summary>Loaded cost per hour used to cost tickets with attendance.</summary>
    public decimal? HourlyCost { get; private set; }

    public Guid? PhotoFileId { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public static Result<Employee> Create(EmployeeData data)
    {
        var employee = new Employee();
        var result = employee.Update(data);
        return result.IsFailure ? result.Error : employee;
    }

    public Result Update(EmployeeData data)
    {
        if (string.IsNullOrWhiteSpace(data.FirstName) || string.IsNullOrWhiteSpace(data.LastName))
        {
            return EmployeeErrors.NameRequired;
        }

        var identity = SetIdentity(data.Code, $"{data.FirstName.Trim()} {data.LastName.Trim()}", data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        var curp = string.IsNullOrWhiteSpace(data.Curp) ? null : data.Curp.Trim().ToUpperInvariant();
        if (curp is not null && !MexicanIds.IsValidCurp(curp))
        {
            return EmployeeErrors.InvalidCurp;
        }

        var rfc = string.IsNullOrWhiteSpace(data.Rfc) ? null : MexicanIds.NormalizeRfc(data.Rfc);
        if (rfc is not null && !MexicanIds.IsValidRfc(rfc))
        {
            return EmployeeErrors.InvalidRfc;
        }

        var email = string.IsNullOrWhiteSpace(data.Email) ? null : TenantMembership.NormalizeEmail(data.Email);
        if (!string.IsNullOrWhiteSpace(data.Email) && email is null)
        {
            return MembershipErrors.InvalidEmail;
        }

        if (data.HourlyCost is < 0)
        {
            return EmployeeErrors.InvalidCost;
        }

        FirstName = data.FirstName.Trim();
        LastName = data.LastName.Trim();
        Curp = curp;
        Rfc = rfc;
        Nss = Clean(data.Nss);
        Position = Clean(data.Position);
        Category = data.Category;
        HiredOn = data.HiredOn;
        Phone = Clean(data.Phone);
        Email = email;
        EmergencyContact = Clean(data.EmergencyContact);
        BloodType = Clean(data.BloodType)?.ToUpperInvariant();
        LicenseNumber = Clean(data.LicenseNumber)?.ToUpperInvariant();
        LicenseType = Clean(data.LicenseType)?.ToUpperInvariant();
        LicenseExpiresOn = data.LicenseExpiresOn;
        HourlyCost = data.HourlyCost;
        PhotoFileId = data.PhotoFileId;
        return Result.Success();
    }

    public void Terminate(DateOnly on)
    {
        TerminatedOn = on;
        Deactivate();
    }

    public void Rehire()
    {
        TerminatedOn = null;
        Activate();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class EmployeeErrors
{
    public const string Catalog = "Employees";

    public static readonly Error NameRequired = Error.Validation("Employees.NameRequired", "Nombre y apellidos son obligatorios.");

    public static readonly Error InvalidCurp = Error.Validation("Employees.InvalidCurp", "La CURP no tiene un formato válido.");

    public static readonly Error InvalidRfc = Error.Validation("Employees.InvalidRfc", "El RFC no tiene un formato válido.");

    public static readonly Error InvalidCost = Error.Validation("Employees.InvalidCost", "El costo por hora no puede ser negativo.");
}

/// <summary>Cuadrilla: leader, members and the units it usually works with.</summary>
public sealed class Crew : CatalogAggregate
{
    private List<Guid> _memberIds = [];
    private List<Guid> _unitIds = [];

    private Crew()
    {
    }

    public Guid? LeaderId { get; private set; }

    public string? Shift { get; private set; }

    public IReadOnlyList<Guid> MemberIds => _memberIds;

    public IReadOnlyList<Guid> UnitIds => _unitIds;

    public static Result<Crew> Create(CrewData data)
    {
        var crew = new Crew();
        var result = crew.Update(data);
        return result.IsFailure ? result.Error : crew;
    }

    public Result Update(CrewData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        var members = (data.MemberIds ?? []).Distinct().ToList();
        if (data.LeaderId is not null && !members.Contains(data.LeaderId.Value))
        {
            members.Insert(0, data.LeaderId.Value);
        }

        LeaderId = data.LeaderId;
        Shift = string.IsNullOrWhiteSpace(data.Shift) ? null : data.Shift.Trim();
        _memberIds = members;
        _unitIds = (data.UnitIds ?? []).Distinct().ToList();
        return Result.Success();
    }
}

public sealed record CrewData(string Code, string Name, Guid? LeaderId, string? Shift, IReadOnlyList<Guid>? MemberIds, IReadOnlyList<Guid>? UnitIds, string? Notes);

public enum AttendanceStatus
{
    Present = 0,
    Absent = 1,
    Rest = 2,
    SickLeave = 3,
    Vacation = 4,
    Travel = 5,
    Training = 6
}

public sealed record AttendanceData(
    Guid EmployeeId,
    DateOnly Date,
    AttendanceStatus Status,
    Guid? CrewId,
    Guid? WorkOrderId,
    Guid? TicketId,
    Guid? SiteId,
    DateTimeOffset? CheckInAt,
    DateTimeOffset? CheckOutAt,
    decimal? Hours,
    decimal? OvertimeHours,
    GeoPoint? Location,
    string? Notes);

/// <summary>
/// Asistencia de campo: who worked, where (order, ticket, site) and how many hours. Input for payroll and for the
/// real cost of each ticket. One record per employee and day.
/// </summary>
public sealed class AttendanceRecord : TenantAggregateRoot
{
    private AttendanceRecord()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    public AttendanceStatus Status { get; private set; }

    public Guid? CrewId { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public Guid? TicketId { get; private set; }

    public Guid? SiteId { get; private set; }

    public DateTimeOffset? CheckInAt { get; private set; }

    public DateTimeOffset? CheckOutAt { get; private set; }

    public decimal Hours { get; private set; }

    public decimal OvertimeHours { get; private set; }

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    public string? Notes { get; private set; }

    public static Result<AttendanceRecord> Create(AttendanceData data)
    {
        var record = new AttendanceRecord { EmployeeId = data.EmployeeId, Date = data.Date };
        var result = record.Update(data);
        return result.IsFailure ? result.Error : record;
    }

    public Result Update(AttendanceData data)
    {
        if (data.CheckOutAt is not null && data.CheckInAt is not null && data.CheckOutAt < data.CheckInAt)
        {
            return AttendanceErrors.InvalidTimes;
        }

        var hours = data.Hours ?? (data.CheckInAt is not null && data.CheckOutAt is not null
            ? Math.Round((decimal)(data.CheckOutAt.Value - data.CheckInAt.Value).TotalHours, 2)
            : 0);

        if (hours is < 0 or > 24 || data.OvertimeHours is < 0 or > 24)
        {
            return AttendanceErrors.InvalidHours;
        }

        Status = data.Status;
        CrewId = data.CrewId;
        WorkOrderId = data.WorkOrderId;
        TicketId = data.TicketId;
        SiteId = data.SiteId;
        CheckInAt = data.CheckInAt;
        CheckOutAt = data.CheckOutAt;
        Hours = data.Status == AttendanceStatus.Present || data.Status == AttendanceStatus.Travel || data.Status == AttendanceStatus.Training ? hours : 0;
        OvertimeHours = data.OvertimeHours ?? 0;
        Latitude = data.Location is { IsValid: true } ? data.Location.Latitude : Latitude;
        Longitude = data.Location is { IsValid: true } ? data.Location.Longitude : Longitude;
        Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();
        return Result.Success();
    }
}

public static class AttendanceErrors
{
    public static readonly Error NotFound = Error.NotFound("Attendance.NotFound", "El registro de asistencia no existe.");

    public static readonly Error InvalidTimes = Error.Validation("Attendance.InvalidTimes", "La salida no puede ser anterior a la entrada.");

    public static readonly Error InvalidHours = Error.Validation("Attendance.InvalidHours", "Las horas van de 0 a 24.");
}

public enum ExpenseCategory
{
    Meals = 0,
    Lodging = 1,
    Tolls = 2,
    Fuel = 3,
    Transport = 4,
    Materials = 5,
    Other = 6
}

public enum ExpenseStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Paid = 4
}

/// <summary>Gasto comprobado con la foto del ticket o el CFDI del proveedor.</summary>
public sealed class ExpenseItem : TenantEntity
{
    private ExpenseItem()
    {
    }

    internal ExpenseItem(ExpenseItemData data)
    {
        Date = data.Date;
        Category = data.Category;
        Description = data.Description.Trim();
        Vendor = string.IsNullOrWhiteSpace(data.Vendor) ? null : data.Vendor.Trim();
        Amount = Money.Round(data.Amount);
        HasInvoice = data.HasInvoice;
        InvoiceUuid = string.IsNullOrWhiteSpace(data.InvoiceUuid) ? null : data.InvoiceUuid.Trim().ToUpperInvariant();
        ReceiptFileId = data.ReceiptFileId;
    }

    public Guid ReportId { get; private set; }

    public DateOnly Date { get; private set; }

    public ExpenseCategory Category { get; private set; }

    public string Description { get; private set; } = null!;

    public string? Vendor { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Backed by a CFDI (deductible) instead of a simple receipt.</summary>
    public bool HasInvoice { get; private set; }

    public string? InvoiceUuid { get; private set; }

    public Guid? ReceiptFileId { get; private set; }
}

public sealed record ExpenseItemData(
    DateOnly Date,
    ExpenseCategory Category,
    string Description,
    string? Vendor,
    decimal Amount,
    bool HasInvoice,
    string? InvoiceUuid,
    Guid? ReceiptFileId);

public sealed record ExpenseReportData(
    Guid EmployeeId,
    Guid? WorkOrderId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Purpose,
    decimal AdvanceAmount,
    IReadOnlyList<ExpenseItemData>? Items);

/// <summary>Comprobación de viáticos: advance given to the employee, receipts and balance to return or reimburse.</summary>
public sealed class ExpenseReport : TenantAggregateRoot, IAuditReference
{
    private readonly List<ExpenseItem> _items = [];

    private ExpenseReport()
    {
    }

    public string Folio { get; private set; } = null!;

    public long FolioNumber { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public string Purpose { get; private set; } = null!;

    public decimal AdvanceAmount { get; private set; }

    public IReadOnlyList<ExpenseItem> Items => _items;

    public decimal Total { get; private set; }

    /// <summary>Positive: the employee returns money; negative: the company reimburses.</summary>
    public decimal Balance { get; private set; }

    public ExpenseStatus Status { get; private set; } = ExpenseStatus.Draft;

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? ReviewedBy { get; private set; }

    public string? RejectionReason { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public string AuditReference => Folio;

    public static Result<ExpenseReport> Create(string folio, long folioNumber, ExpenseReportData data)
    {
        var report = new ExpenseReport { Folio = folio, FolioNumber = folioNumber };
        var result = report.Apply(data);
        return result.IsFailure ? result.Error : report;
    }

    public Result Update(ExpenseReportData data)
    {
        if (Status is not (ExpenseStatus.Draft or ExpenseStatus.Rejected))
        {
            return ExpenseErrors.NotEditable;
        }

        return Apply(data);
    }

    public Result Submit(DateTimeOffset at)
    {
        if (Status is not (ExpenseStatus.Draft or ExpenseStatus.Rejected))
        {
            return ExpenseErrors.InvalidTransition;
        }

        if (_items.Count == 0)
        {
            return ExpenseErrors.NoItems;
        }

        Status = ExpenseStatus.Submitted;
        SubmittedAt = at;
        RejectionReason = null;
        return Result.Success();
    }

    public Result Approve(string by, DateTimeOffset at)
    {
        if (Status != ExpenseStatus.Submitted)
        {
            return ExpenseErrors.InvalidTransition;
        }

        Status = ExpenseStatus.Approved;
        ReviewedBy = by;
        ReviewedAt = at;
        return Result.Success();
    }

    public Result Reject(string by, DateTimeOffset at, string reason)
    {
        if (Status != ExpenseStatus.Submitted)
        {
            return ExpenseErrors.InvalidTransition;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return ExpenseErrors.ReasonRequired;
        }

        Status = ExpenseStatus.Rejected;
        ReviewedBy = by;
        ReviewedAt = at;
        RejectionReason = reason.Trim();
        return Result.Success();
    }

    public Result MarkPaid(DateTimeOffset at)
    {
        if (Status != ExpenseStatus.Approved)
        {
            return ExpenseErrors.InvalidTransition;
        }

        Status = ExpenseStatus.Paid;
        PaidAt = at;
        return Result.Success();
    }

    private Result Apply(ExpenseReportData data)
    {
        if (data.PeriodEnd < data.PeriodStart)
        {
            return ExpenseErrors.InvalidPeriod;
        }

        if (string.IsNullOrWhiteSpace(data.Purpose) || data.Purpose.Trim().Length > 500)
        {
            return ExpenseErrors.PurposeRequired;
        }

        var items = data.Items ?? [];
        if (data.AdvanceAmount < 0 || items.Any(i => i.Amount <= 0 || string.IsNullOrWhiteSpace(i.Description)))
        {
            return ExpenseErrors.InvalidAmount;
        }

        EmployeeId = data.EmployeeId;
        WorkOrderId = data.WorkOrderId;
        PeriodStart = data.PeriodStart;
        PeriodEnd = data.PeriodEnd;
        Purpose = data.Purpose.Trim();
        AdvanceAmount = Money.Round(data.AdvanceAmount);
        _items.Clear();
        _items.AddRange(items.Select(i => new ExpenseItem(i)));
        Total = Money.Round(_items.Sum(i => i.Amount));
        Balance = AdvanceAmount - Total;
        return Result.Success();
    }
}

public static class ExpenseErrors
{
    public static readonly Error ReasonRequired = Error.Validation("Expenses.ReasonRequired", "Indica el motivo.");

    public static readonly Error NotFound = Error.NotFound("Expenses.NotFound", "La comprobación no existe.");

    public static readonly Error NotEditable = Error.Conflict("Expenses.NotEditable", "La comprobación ya fue enviada.");

    public static readonly Error InvalidTransition = Error.Conflict("Expenses.InvalidTransition", "Cambio de estatus no permitido.");

    public static readonly Error NoItems = Error.Validation("Expenses.NoItems", "Agrega al menos un gasto.");

    public static readonly Error InvalidPeriod = Error.Validation("Expenses.InvalidPeriod", "El fin del periodo no puede ser anterior al inicio.");

    public static readonly Error PurposeRequired = Error.Validation("Expenses.PurposeRequired", "Indica el motivo del viaje o comisión.");

    public static readonly Error InvalidAmount = Error.Validation("Expenses.InvalidAmount", "Cada gasto necesita descripción e importe mayor a cero; el anticipo no puede ser negativo.");

    public static readonly Error NotOwner = Error.Forbidden("Expenses.NotOwner", "Solo puedes comprobar tus propios gastos.");
}
