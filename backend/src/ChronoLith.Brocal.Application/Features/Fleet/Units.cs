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
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Fleet;

public sealed record UnitDto(
    Guid Id,
    string Code,
    string Name,
    UnitType Type,
    UnitStatus Status,
    string? Brand,
    string? Model,
    int? Year,
    string? Plates,
    string? SerialNumber,
    MeterKind MeterKind,
    decimal CurrentOdometer,
    decimal CurrentHours,
    DateTimeOffset? MetersUpdatedAt,
    decimal? Capacity,
    string? CapacityUnit,
    string? FuelType,
    decimal? FuelTankLiters,
    decimal? ExpectedEfficiency,
    string? VehicleConfig,
    decimal? GrossWeightTons,
    string? SctPermitType,
    string? SctPermitNumber,
    string? InsurerName,
    string? InsurancePolicy,
    string? EnvironmentalInsurer,
    string? EnvironmentalPolicy,
    Guid? DefaultOperatorId,
    string? DefaultOperatorName,
    Guid? PhotoFileId,
    DateOnly? AcquiredOn,
    decimal? AcquisitionCost,
    decimal? LastLatitude,
    decimal? LastLongitude,
    DateTimeOffset? LastPositionAt,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal sealed class UnitDataValidator : AbstractValidator<UnitData>
{
    public UnitDataValidator()
    {
        RuleFor(u => u.Code).BusinessCode();
        RuleFor(u => u.Name).RequiredName();
        RuleFor(u => u.Year).InclusiveBetween(1950, 2100).When(u => u.Year is not null);
        RuleFor(u => u.Plates).MaximumLength(20);
        RuleFor(u => u.ExpectedEfficiency).GreaterThan(0).When(u => u.ExpectedEfficiency is not null);
    }
}

internal sealed class UnitSpec(IApplicationDbContext db, IQuotaGuard quotas) : ICatalogSpec<Unit, UnitData, UnitDto>
{
    public string Name => UnitErrors.Catalog;

    public Expression<Func<Unit, UnitDto>> Projection => u => new UnitDto(
        u.Id, u.Code, u.Name, u.Type, u.Status, u.Brand, u.Model, u.Year, u.Plates, u.SerialNumber, u.MeterKind, u.CurrentOdometer, u.CurrentHours,
        u.MetersUpdatedAt, u.Capacity, u.CapacityUnit, u.FuelType, u.FuelTankLiters, u.ExpectedEfficiency, u.VehicleConfig, u.GrossWeightTons,
        u.SctPermitType, u.SctPermitNumber, u.InsurerName, u.InsurancePolicy, u.EnvironmentalInsurer, u.EnvironmentalPolicy, u.DefaultOperatorId,
        db.Employees.Where(e => e.Id == u.DefaultOperatorId).Select(e => e.Name).FirstOrDefault(),
        u.PhotoFileId, u.AcquiredOn, u.AcquisitionCost, u.LastLatitude, u.LastLongitude, u.LastPositionAt, u.Notes, u.IsActive, u.CreatedAt, u.UpdatedAt);

    public Expression<Func<Unit, string?>>? LookupExtra => u => u.Plates;

    public string CodeOf(UnitData data) => data.Code;

    public Result<Unit> Create(UnitData data) => Unit.Create(data);

    public Result Update(Unit entity, UnitData data) => entity.Update(data);

    public async Task<Result> ValidateAsync(UnitData data, Unit? existing, CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            var quota = await quotas.EnsureCanAddUnitAsync(cancellationToken);
            if (quota.IsFailure)
            {
                return quota;
            }
        }

        return await References.EnsureAsync<Employee>(db, data.DefaultOperatorId, EmployeeErrors.Catalog, cancellationToken);
    }

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Tickets.AnyAsync(t => t.UnitIds.Contains(id), cancellationToken)
        || await db.WorkOrders.AnyAsync(w => w.UnitIds.Contains(id), cancellationToken)
        || await db.FuelLoads.AnyAsync(f => f.UnitId == id, cancellationToken)
        || await db.MaintenanceOrders.AnyAsync(m => m.UnitId == id, cancellationToken);

    public IQueryable<Unit> Filter(IQueryable<Unit> query, IReadOnlyDictionary<string, string?> filters)
    {
        if (filters.TryGetValue("type", out var type) && Enum.TryParse<UnitType>(type, true, out var unitType))
        {
            query = query.Where(u => u.Type == unitType);
        }

        if (filters.TryGetValue("status", out var status) && Enum.TryParse<UnitStatus>(status, true, out var unitStatus))
        {
            query = query.Where(u => u.Status == unitStatus);
        }

        return query;
    }
}

// ---- Meters, status and history -------------------------------------------------------------------------

public sealed record RecordMetersCommand(Guid UnitId, decimal? Odometer, decimal? Hours, DateTimeOffset? RecordedAt, bool IsCorrection, string? Notes) : ICommand;

internal sealed class RecordMetersCommandHandler(IApplicationDbContext db, IUserContext user, ITenantClock clock, ITenantContext tenant) : ICommandHandler<RecordMetersCommand>
{
    public async Task<Result> Handle(RecordMetersCommand command, CancellationToken cancellationToken)
    {
        if (command.Odometer is null && command.Hours is null)
        {
            return Error.Validation("Units.MetersRequired", "Captura el kilometraje o el horómetro.");
        }

        if (command.IsCorrection && !tenant.Has(Domain.Platform.Permissions.FleetManage))
        {
            return Error.Forbidden("Units.CorrectionForbidden", "Solo quien administra la flota puede corregir lecturas.");
        }

        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == command.UnitId, cancellationToken);
        if (unit is null)
        {
            return CatalogErrors.NotFound(UnitErrors.Catalog);
        }

        var at = command.RecordedAt ?? clock.UtcNow;
        var result = unit.RecordMeters(command.Odometer, command.Hours, at, command.IsCorrection);
        if (result.IsFailure)
        {
            return result;
        }

        db.UnitMeterReadings.Add(new UnitMeterReading(unit.Id, at, command.Odometer, command.Hours, MeterSource.Manual, null,
            command.IsCorrection ? $"Corrección. {command.Notes}".Trim() : command.Notes, user.DisplayName));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record ChangeUnitStatusCommand(Guid UnitId, UnitStatus Status, string? Reason) : ICommand;

internal sealed class ChangeUnitStatusCommandHandler(IApplicationDbContext db, IUserContext user, ITenantClock clock) : ICommandHandler<ChangeUnitStatusCommand>
{
    public async Task<Result> Handle(ChangeUnitStatusCommand command, CancellationToken cancellationToken)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == command.UnitId, cancellationToken);
        if (unit is null)
        {
            return CatalogErrors.NotFound(UnitErrors.Catalog);
        }

        if (unit.Status == command.Status)
        {
            return Result.Success();
        }

        if (unit.Status == UnitStatus.Workshop && await db.MaintenanceOrders.AnyAsync(
                o => o.UnitId == unit.Id && (o.Status == MaintenanceOrderStatus.Open || o.Status == MaintenanceOrderStatus.InProgress || o.Status == MaintenanceOrderStatus.WaitingParts),
                cancellationToken))
        {
            return Error.Conflict("Units.OpenWorkshopOrder", "La unidad tiene una orden de taller abierta: ciérrala para liberarla.");
        }

        var previous = unit.Status;
        var result = unit.ChangeStatus(command.Status);
        if (result.IsFailure)
        {
            return result;
        }

        db.UnitStatusChanges.Add(new UnitStatusChange(unit.Id, clock.UtcNow, previous, command.Status, command.Reason, user.DisplayName));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record UnitHistoryItem(DateTimeOffset At, string Kind, string Title, string? Detail, string? By, string? Link);

public sealed record UnitOverviewDto(
    UnitDto Unit,
    IReadOnlyList<UnitHistoryItem> History,
    IReadOnlyList<KpiDto> Kpis,
    IReadOnlyList<UnitDocumentStatusDto> Documents);

public sealed record UnitDocumentStatusDto(Guid DocumentTypeId, string DocumentType, string? Number, DateOnly? ExpiresOn, ComplianceStatus Status, int? DaysLeft);

/// <summary>Historial por unidad: meter readings, status changes, tickets, workshop orders and fuel loads.</summary>
public sealed record GetUnitOverviewQuery(Guid UnitId, int Days = 120) : IQuery<UnitOverviewDto>;

internal sealed class GetUnitOverviewQueryHandler(IApplicationDbContext db, CatalogService<Unit, UnitData, UnitDto> units, ITenantClock clock, ITenantContext tenant)
    : IQueryHandler<GetUnitOverviewQuery, UnitOverviewDto>
{
    public async Task<Result<UnitOverviewDto>> Handle(GetUnitOverviewQuery query, CancellationToken cancellationToken)
    {
        var unit = await units.GetAsync(query.UnitId, cancellationToken);
        if (unit.IsFailure)
        {
            return unit.Error;
        }

        var since = clock.UtcNow.AddDays(-Math.Clamp(query.Days, 7, 730));
        var sinceDate = DateOnly.FromDateTime(since.UtcDateTime);
        var history = new List<UnitHistoryItem>();

        var readings = await db.UnitMeterReadings.AsNoTracking().Where(r => r.UnitId == query.UnitId && r.RecordedAt >= since).OrderByDescending(r => r.RecordedAt).Take(200).ToListAsync(cancellationToken);
        history.AddRange(readings.Select(r => new UnitHistoryItem(r.RecordedAt, "meter", "Lectura registrada",
            string.Join(" · ", new[] { r.Odometer is null ? null : $"{r.Odometer:N0} km", r.Hours is null ? null : $"{r.Hours:N1} h", r.Notes }.Where(s => s is not null)), r.RecordedBy, null)));

        var changes = await db.UnitStatusChanges.AsNoTracking().Where(c => c.UnitId == query.UnitId && c.ChangedAt >= since).ToListAsync(cancellationToken);
        history.AddRange(changes.Select(c => new UnitHistoryItem(c.ChangedAt, "status", $"Estatus: {c.From} → {c.To}", c.Reason, c.ChangedBy, null)));

        var tickets = await db.Tickets.AsNoTracking().Where(t => t.UnitIds.Contains(query.UnitId) && t.ServiceDate >= sinceDate && t.Status != TicketStatus.Cancelled)
            .Select(t => new { t.Id, t.Folio, t.ServiceDate, t.Total, t.CreatedAt, t.CapturedByName }).ToListAsync(cancellationToken);
        history.AddRange(tickets.Select(t => new UnitHistoryItem(t.CreatedAt, "ticket", $"Boleta {t.Folio}", $"{t.ServiceDate:dd/MM/yyyy} · {t.Total:C2}", t.CapturedByName, $"boletas/{t.Id}")));

        var maintenance = await db.MaintenanceOrders.AsNoTracking().Where(o => o.UnitId == query.UnitId && o.OpenedAt >= since).ToListAsync(cancellationToken);
        history.AddRange(maintenance.Select(o => new UnitHistoryItem(o.OpenedAt, "maintenance", $"Orden de taller {o.Folio} ({o.Type})",
            $"{o.ReportedFailure ?? o.WorkPerformed} · costo {o.TotalCost:C2}", o.CreatedBy, $"mantenimiento/ordenes/{o.Id}")));

        var fuel = await db.FuelLoads.AsNoTracking().Where(f => f.UnitId == query.UnitId && f.LoadedAt >= since).ToListAsync(cancellationToken);
        history.AddRange(fuel.Select(f => new UnitHistoryItem(f.LoadedAt, "fuel", $"Carga de {f.Liters:N0} l",
            $"{f.Amount:C2}{(f.Efficiency is null ? string.Empty : $" · {f.Efficiency} {f.EfficiencyUnit}")}{(f.IsAnomaly ? " · rendimiento fuera de rango" : string.Empty)}", f.CreatedBy, null)));

        var documents = await db.ComplianceDocuments.AsNoTracking().Where(d => d.Subject == DocumentSubject.Unit && d.SubjectId == query.UnitId).ToListAsync(cancellationToken);
        var types = await db.DocumentTypes.AsNoTracking().Where(t => t.AppliesTo == DocumentSubject.Unit && t.IsActive).ToListAsync(cancellationToken);
        var today = clock.Today;
        var warning = tenant.Tenant.Settings.ExpiryWarningDays;
        var documentStatus = types
            .Select(type =>
            {
                var latest = documents.Where(d => d.DocumentTypeId == type.Id).OrderByDescending(d => d.ExpiresOn ?? DateOnly.MaxValue).FirstOrDefault();
                if (latest is null)
                {
                    return type.IsMandatory ? new UnitDocumentStatusDto(type.Id, type.Name, null, null, ComplianceStatus.Missing, null) : null;
                }

                return new UnitDocumentStatusDto(type.Id, type.Name, latest.Number, latest.ExpiresOn,
                    latest.StatusOn(today, type.WarningDays ?? warning), ComplianceRules.DaysLeft(latest.ExpiresOn, today));
            })
            .OfType<UnitDocumentStatusDto>()
            .ToList();

        var revenue = await db.Tickets.AsNoTracking().Where(t => t.UnitIds.Contains(query.UnitId) && t.ServiceDate >= sinceDate && t.Status != TicketStatus.Draft && t.Status != TicketStatus.Cancelled)
            .SumAsync(t => (decimal?)t.Subtotal, cancellationToken) ?? 0;

        var kpis = new List<KpiDto>
        {
            new("Boletas en el periodo", tickets.Count),
            new("Ingreso atribuido", revenue),
            new("Costo de taller", maintenance.Sum(m => m.TotalCost)),
            new("Litros de combustible", fuel.Sum(f => f.Liters)),
            new("Costo de combustible", fuel.Sum(f => f.Amount))
        };

        return new UnitOverviewDto(unit.Value, history.OrderByDescending(h => h.At).Take(300).ToList(), kpis, documentStatus);
    }
}

// ---- GPS positions (integrations) -----------------------------------------------------------------------

public sealed record PositionInput(string UnitCode, DateTimeOffset RecordedAt, decimal Latitude, decimal Longitude, decimal? SpeedKmh, decimal? Odometer);

public sealed record IngestPositionsResult(int Accepted, IReadOnlyList<string> UnknownUnits);

/// <summary>Positions pushed by the fleet GPS provider (API key with scope gps.write). Odometer readings update the unit.</summary>
public sealed record IngestPositionsCommand(string Source, IReadOnlyList<PositionInput> Positions) : ICommand<IngestPositionsResult>;

internal sealed class IngestPositionsCommandHandler(IApplicationDbContext db) : ICommandHandler<IngestPositionsCommand, IngestPositionsResult>
{
    public async Task<Result<IngestPositionsResult>> Handle(IngestPositionsCommand command, CancellationToken cancellationToken)
    {
        if (command.Positions.Count is 0 or > 1000)
        {
            return Error.Validation("Gps.InvalidBatch", "Envía entre 1 y 1000 posiciones por solicitud.");
        }

        var codes = command.Positions.Select(p => Codes.Normalize(p.UnitCode)).Distinct().ToList();
        var units = await db.Units.Where(u => codes.Contains(u.Code)).ToDictionaryAsync(u => u.Code, cancellationToken);
        var unknown = codes.Where(c => !units.ContainsKey(c)).ToList();
        var accepted = 0;

        foreach (var position in command.Positions.OrderBy(p => p.RecordedAt))
        {
            if (!units.TryGetValue(Codes.Normalize(position.UnitCode), out var unit) || position.Latitude is < -90 or > 90 || position.Longitude is < -180 or > 180)
            {
                continue;
            }

            db.UnitPositions.Add(new UnitPosition(unit.Id, position.RecordedAt, position.Latitude, position.Longitude, position.SpeedKmh, position.Odometer, command.Source));
            unit.RecordPosition(position.Latitude, position.Longitude, position.RecordedAt);
            if (position.Odometer is { } odometer && unit.UsesOdometer && odometer > unit.CurrentOdometer)
            {
                unit.RecordMeters(odometer, null, position.RecordedAt);
            }

            accepted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new IngestPositionsResult(accepted, unknown);
    }
}
