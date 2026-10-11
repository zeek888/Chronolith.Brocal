using System.Linq.Expressions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.People;

// ---- Employees ------------------------------------------------------------------------------------------

public sealed record EmployeeDto(
    Guid Id, string Code, string Name, string FirstName, string LastName, string? Curp, string? Rfc, string? Nss, string? Position, EmployeeCategory Category,
    DateOnly? HiredOn, DateOnly? TerminatedOn, string? Phone, string? Email, string? EmergencyContact, string? BloodType, string? LicenseNumber, string? LicenseType,
    DateOnly? LicenseExpiresOn, decimal? HourlyCost, Guid? PhotoFileId, bool HasUser, string? Notes, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

internal sealed class EmployeeDataValidator : AbstractValidator<EmployeeData>
{
    public EmployeeDataValidator()
    {
        RuleFor(e => e.Code).BusinessCode();
        RuleFor(e => e.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(e => e.LastName).NotEmpty().MaximumLength(100);
        RuleFor(e => e.Rfc).Rfc();
        RuleFor(e => e.Email).Email().When(e => !string.IsNullOrWhiteSpace(e.Email));
        RuleFor(e => e.HourlyCost).GreaterThanOrEqualTo(0).When(e => e.HourlyCost is not null);
    }
}

internal sealed class EmployeeSpec(IApplicationDbContext db, IQuotaGuard quotas) : ICatalogSpec<Employee, EmployeeData, EmployeeDto>
{
    public string Name => EmployeeErrors.Catalog;

    public Expression<Func<Employee, EmployeeDto>> Projection => e => new EmployeeDto(
        e.Id, e.Code, e.Name, e.FirstName, e.LastName, e.Curp, e.Rfc, e.Nss, e.Position, e.Category, e.HiredOn, e.TerminatedOn, e.Phone, e.Email, e.EmergencyContact,
        e.BloodType, e.LicenseNumber, e.LicenseType, e.LicenseExpiresOn, e.HourlyCost, e.PhotoFileId,
        db.Memberships.Any(m => m.EmployeeId == e.Id), e.Notes, e.IsActive, e.CreatedAt, e.UpdatedAt);

    public Expression<Func<Employee, string?>>? LookupExtra => e => e.Position;

    public string CodeOf(EmployeeData data) => data.Code;

    public Result<Employee> Create(EmployeeData data) => Employee.Create(data);

    public Result Update(Employee entity, EmployeeData data) => entity.Update(data);

    public async Task<Result> ValidateAsync(EmployeeData data, Employee? existing, CancellationToken cancellationToken) =>
        existing is null ? await quotas.EnsureCanAddEmployeeAsync(cancellationToken) : Result.Success();

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Tickets.AnyAsync(t => t.EmployeeIds.Contains(id), cancellationToken)
        || await db.Attendance.AnyAsync(a => a.EmployeeId == id, cancellationToken)
        || await db.ExpenseReports.AnyAsync(r => r.EmployeeId == id, cancellationToken)
        || await db.ComplianceDocuments.AnyAsync(d => d.Subject == DocumentSubject.Employee && d.SubjectId == id, cancellationToken);

    public IQueryable<Employee> Filter(IQueryable<Employee> query, IReadOnlyDictionary<string, string?> filters) =>
        filters.TryGetValue("category", out var category) && Enum.TryParse<EmployeeCategory>(category, true, out var value)
            ? query.Where(e => e.Category == value)
            : query;
}

// ---- Crews ----------------------------------------------------------------------------------------------

public sealed record CrewDto(
    Guid Id, string Code, string Name, Guid? LeaderId, string? LeaderName, string? Shift, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> UnitIds,
    int MemberCount, string? Notes, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

internal sealed class CrewDataValidator : AbstractValidator<CrewData>
{
    public CrewDataValidator()
    {
        RuleFor(c => c.Code).BusinessCode();
        RuleFor(c => c.Name).RequiredName();
    }
}

internal sealed class CrewSpec(IApplicationDbContext db) : ICatalogSpec<Crew, CrewData, CrewDto>
{
    public string Name => "Crews";

    public Expression<Func<Crew, CrewDto>> Projection => c => new CrewDto(
        c.Id, c.Code, c.Name, c.LeaderId, db.Employees.Where(e => e.Id == c.LeaderId).Select(e => e.Name).FirstOrDefault(), c.Shift, c.MemberIds, c.UnitIds,
        c.MemberIds.Count, c.Notes, c.IsActive, c.CreatedAt, c.UpdatedAt);

    public string CodeOf(CrewData data) => data.Code;

    public Result<Crew> Create(CrewData data) => Crew.Create(data);

    public Result Update(Crew entity, CrewData data) => entity.Update(data);

    public async Task<Result> ValidateAsync(CrewData data, Crew? existing, CancellationToken cancellationToken) => Result.FirstFailure(
        await References.EnsureAllAsync<Employee>(db, (data.MemberIds ?? []).Concat(data.LeaderId is null ? [] : [data.LeaderId.Value]), EmployeeErrors.Catalog, cancellationToken),
        await References.EnsureAllAsync<Unit>(db, data.UnitIds, UnitErrors.Catalog, cancellationToken));

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.WorkOrders.AnyAsync(w => w.CrewId == id, cancellationToken) || await db.Tickets.AnyAsync(t => t.CrewId == id, cancellationToken)
        || await db.Attendance.AnyAsync(a => a.CrewId == id, cancellationToken);
}

// ---- Attendance -----------------------------------------------------------------------------------------

public sealed record AttendanceRowDto(
    Guid? Id, Guid EmployeeId, string EmployeeCode, string EmployeeName, string? Position, DateOnly Date, AttendanceStatus? Status, Guid? CrewId, Guid? WorkOrderId,
    Guid? TicketId, Guid? SiteId, DateTimeOffset? CheckInAt, DateTimeOffset? CheckOutAt, decimal Hours, decimal OvertimeHours, string? Notes);

/// <summary>Daily sheet: every active employee (or the members of a crew) with their record of the day, if any.</summary>
public sealed record GetAttendanceSheetQuery(DateOnly Date, Guid? CrewId) : IQuery<IReadOnlyList<AttendanceRowDto>>;

internal sealed class GetAttendanceSheetQueryHandler(IApplicationDbContext db) : IQueryHandler<GetAttendanceSheetQuery, IReadOnlyList<AttendanceRowDto>>
{
    public async Task<Result<IReadOnlyList<AttendanceRowDto>>> Handle(GetAttendanceSheetQuery query, CancellationToken cancellationToken)
    {
        var employees = db.Employees.AsNoTracking().Where(e => e.IsActive);
        if (query.CrewId is not null)
        {
            var crew = await db.Crews.AsNoTracking().FirstOrDefaultAsync(c => c.Id == query.CrewId, cancellationToken);
            var members = crew?.MemberIds.ToList() ?? [];
            employees = employees.Where(e => members.Contains(e.Id));
        }

        var people = await employees.OrderBy(e => e.Name).ToListAsync(cancellationToken);
        var ids = people.Select(p => p.Id).ToList();
        var records = await db.Attendance.AsNoTracking().Where(a => a.Date == query.Date && ids.Contains(a.EmployeeId)).ToDictionaryAsync(a => a.EmployeeId, cancellationToken);

        return people.Select(p => records.TryGetValue(p.Id, out var r)
            ? new AttendanceRowDto(r.Id, p.Id, p.Code, p.Name, p.Position, r.Date, r.Status, r.CrewId, r.WorkOrderId, r.TicketId, r.SiteId, r.CheckInAt, r.CheckOutAt, r.Hours, r.OvertimeHours, r.Notes)
            : new AttendanceRowDto(null, p.Id, p.Code, p.Name, p.Position, query.Date, null, query.CrewId, null, null, null, null, null, 0, 0, null)).ToList();
    }
}

/// <summary>Saves the records of a day in one batch (upsert per employee and date).</summary>
public sealed record SaveAttendanceCommand(IReadOnlyList<AttendanceData> Records) : ICommand<int>;

internal sealed class SaveAttendanceCommandHandler(IApplicationDbContext db, ITenantContext tenant) : ICommandHandler<SaveAttendanceCommand, int>
{
    public async Task<Result<int>> Handle(SaveAttendanceCommand command, CancellationToken cancellationToken)
    {
        if (command.Records.Count is 0 or > 500)
        {
            return Error.Validation("Attendance.InvalidBatch", "Envía entre 1 y 500 registros.");
        }

        var employeeIds = command.Records.Select(r => r.EmployeeId).Distinct().ToList();
        if (await db.Employees.CountAsync(e => employeeIds.Contains(e.Id), cancellationToken) != employeeIds.Count)
        {
            return CatalogErrors.ReferenceNotFound(EmployeeErrors.Catalog);
        }

        // Field staff without crews.manage/attendance can only check themselves in.
        if (!tenant.HasAny(Permissions.CrewsAttendance, Permissions.CrewsManage) && employeeIds.Any(id => id != tenant.Membership?.EmployeeId))
        {
            return Error.Forbidden("Attendance.Forbidden", "Solo puedes registrar tu propia asistencia.");
        }

        var dates = command.Records.Select(r => r.Date).Distinct().ToList();
        var existing = await db.Attendance.Where(a => employeeIds.Contains(a.EmployeeId) && dates.Contains(a.Date)).ToListAsync(cancellationToken);
        foreach (var data in command.Records)
        {
            var record = existing.FirstOrDefault(a => a.EmployeeId == data.EmployeeId && a.Date == data.Date);
            if (record is not null)
            {
                var updated = record.Update(data);
                if (updated.IsFailure)
                {
                    return updated.Error;
                }

                continue;
            }

            var created = AttendanceRecord.Create(data);
            if (created.IsFailure)
            {
                return created.Error;
            }

            db.Attendance.Add(created.Value);
            existing.Add(created.Value);
        }

        await db.SaveChangesAsync(cancellationToken);
        return command.Records.Count;
    }
}

public sealed record PayrollRowDto(Guid EmployeeId, string Code, string Name, string? Position, int DaysWorked, decimal Hours, decimal OvertimeHours, int Absences, int Rest, int Leave, decimal? HourlyCost, decimal? Cost);

/// <summary>Insumo para nómina: days and hours worked per employee in a period, with the loaded cost.</summary>
public sealed record GetPayrollSummaryQuery(DateOnly From, DateOnly To, Guid? CrewId) : IQuery<IReadOnlyList<PayrollRowDto>>;

internal sealed class GetPayrollSummaryQueryHandler(IApplicationDbContext db) : IQueryHandler<GetPayrollSummaryQuery, IReadOnlyList<PayrollRowDto>>
{
    public async Task<Result<IReadOnlyList<PayrollRowDto>>> Handle(GetPayrollSummaryQuery query, CancellationToken cancellationToken)
    {
        if (query.To < query.From || query.To.DayNumber - query.From.DayNumber > 62)
        {
            return Error.Validation("Attendance.InvalidPeriod", "El periodo va de 1 a 62 días.");
        }

        var records = await db.Attendance.AsNoTracking()
            .Where(a => a.Date >= query.From && a.Date <= query.To)
            .WhereIf(query.CrewId is not null, a => a.CrewId == query.CrewId)
            .ToListAsync(cancellationToken);
        var employeeIds = records.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, cancellationToken);

        return records.GroupBy(r => r.EmployeeId).Where(g => employees.ContainsKey(g.Key)).Select(g =>
        {
            var employee = employees[g.Key];
            var hours = g.Sum(r => r.Hours);
            var overtime = g.Sum(r => r.OvertimeHours);
            return new PayrollRowDto(employee.Id, employee.Code, employee.Name, employee.Position,
                g.Count(r => r.Status is AttendanceStatus.Present or AttendanceStatus.Travel or AttendanceStatus.Training), hours, overtime,
                g.Count(r => r.Status == AttendanceStatus.Absent), g.Count(r => r.Status == AttendanceStatus.Rest),
                g.Count(r => r.Status is AttendanceStatus.SickLeave or AttendanceStatus.Vacation), employee.HourlyCost,
                employee.HourlyCost is { } cost ? Money.Round((hours + overtime * 2) * cost) : null);
        }).OrderBy(r => r.Name).ToList();
    }
}

public sealed record TicketCostDto(Guid TicketId, string Folio, decimal Revenue, decimal LaborHours, decimal LaborCost, decimal Margin, IReadOnlyList<PayrollRowDto> People);

/// <summary>Costeo real de una boleta: hours of the attendance records linked to the ticket (or its date and personnel).</summary>
public sealed record GetTicketCostQuery(Guid TicketId) : IQuery<TicketCostDto>;

internal sealed class GetTicketCostQueryHandler(IApplicationDbContext db) : IQueryHandler<GetTicketCostQuery, TicketCostDto>
{
    public async Task<Result<TicketCostDto>> Handle(GetTicketCostQuery query, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == query.TicketId, cancellationToken);
        if (ticket is null)
        {
            return TicketErrors.NotFound;
        }

        var people = ticket.EmployeeIds.ToList();
        var records = await db.Attendance.AsNoTracking()
            .Where(a => a.TicketId == ticket.Id || a.TicketId == null && a.Date == ticket.ServiceDate && people.Contains(a.EmployeeId))
            .ToListAsync(cancellationToken);
        var employeeIds = records.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, cancellationToken);

        var rows = records.GroupBy(r => r.EmployeeId).Where(g => employees.ContainsKey(g.Key)).Select(g =>
        {
            var employee = employees[g.Key];
            var hours = g.Sum(r => r.Hours);
            var overtime = g.Sum(r => r.OvertimeHours);
            return new PayrollRowDto(employee.Id, employee.Code, employee.Name, employee.Position, g.Count(), hours, overtime, 0, 0, 0, employee.HourlyCost,
                employee.HourlyCost is { } cost ? Money.Round((hours + overtime * 2) * cost) : 0);
        }).ToList();

        var laborCost = rows.Sum(r => r.Cost ?? 0);
        return new TicketCostDto(ticket.Id, ticket.Folio, ticket.Subtotal, rows.Sum(r => r.Hours + r.OvertimeHours), laborCost, ticket.Subtotal - laborCost, rows);
    }
}
