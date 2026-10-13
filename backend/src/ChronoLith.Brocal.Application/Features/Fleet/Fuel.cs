using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Automation;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Fleet;

public sealed record FuelLoadDto(
    Guid Id,
    Guid UnitId,
    string? UnitCode,
    DateTimeOffset LoadedAt,
    decimal Liters,
    decimal PricePerLiter,
    decimal Amount,
    decimal? Odometer,
    decimal? Hours,
    bool IsFullTank,
    string? Station,
    FuelPaymentMethod PaymentMethod,
    Guid? DriverId,
    string? DriverName,
    Guid? WorkOrderId,
    Guid? ReceiptFileId,
    string? ReceiptUrl,
    decimal? Efficiency,
    string? EfficiencyUnit,
    decimal? DeviationPercent,
    bool IsAnomaly,
    string? Notes,
    string? CreatedBy);

public sealed record SearchFuelLoadsQuery(Guid? UnitId, DateOnly? From, DateOnly? To, bool? OnlyAnomalies, int Page = 1, int PageSize = 50) : IQuery<PagedResult<FuelLoadDto>>;

internal sealed class SearchFuelLoadsQueryHandler(IApplicationDbContext db, ITenantClock clock, IFileService files) : IQueryHandler<SearchFuelLoadsQuery, PagedResult<FuelLoadDto>>
{
    public async Task<Result<PagedResult<FuelLoadDto>>> Handle(SearchFuelLoadsQuery query, CancellationToken cancellationToken)
    {
        var from = query.From is null ? (DateTimeOffset?)null : clock.StartOfDay(query.From.Value);
        var to = query.To is null ? (DateTimeOffset?)null : clock.StartOfDay(query.To.Value.AddDays(1));
        var loads = db.FuelLoads.AsNoTracking()
            .WhereIf(query.UnitId is not null, f => f.UnitId == query.UnitId)
            .WhereIf(from is not null, f => f.LoadedAt >= from)
            .WhereIf(to is not null, f => f.LoadedAt < to)
            .WhereIf(query.OnlyAnomalies == true, f => f.IsAnomaly)
            .OrderByDescending(f => f.LoadedAt);

        var page = await loads.Select(f => new FuelLoadDto(
            f.Id, f.UnitId, db.Units.Where(u => u.Id == f.UnitId).Select(u => u.Code).FirstOrDefault(), f.LoadedAt, f.Liters, f.PricePerLiter, f.Amount, f.Odometer, f.Hours,
            f.IsFullTank, f.Station, f.PaymentMethod, f.DriverId, db.Employees.Where(e => e.Id == f.DriverId).Select(e => e.Name).FirstOrDefault(), f.WorkOrderId,
            f.ReceiptFileId, null, f.Efficiency, f.EfficiencyUnit, f.DeviationPercent, f.IsAnomaly, f.Notes, f.CreatedBy))
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);

        var items = page.Items.Select(i => i.ReceiptFileId is { } file ? i with { ReceiptUrl = files.GetUrl(file) } : i).ToList();
        return page with { Items = items };
    }
}

public sealed record SaveFuelLoadCommand(Guid? Id, FuelLoadData Data) : ICommand<FuelLoadDto>;

internal sealed class SaveFuelLoadCommandValidator : AbstractValidator<SaveFuelLoadCommand>
{
    public SaveFuelLoadCommandValidator()
    {
        RuleFor(c => c.Data.UnitId).RequiredId("la unidad");
        RuleFor(c => c.Data.Liters).GreaterThan(0).LessThanOrEqualTo(5000);
        RuleFor(c => c.Data.PricePerLiter).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// Records a fuel load. The odometer/hour meter updates the unit; full-tank loads close a cycle whose efficiency is
/// compared with the expected one (anomalies notify the workshop and management).
/// </summary>
internal sealed class SaveFuelLoadCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    IUserContext user,
    IFileService files,
    INotificationPublisher notifications,
    IBusinessRules rules) : ICommandHandler<SaveFuelLoadCommand, FuelLoadDto>
{
    public async Task<Result<FuelLoadDto>> Handle(SaveFuelLoadCommand command, CancellationToken cancellationToken)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == command.Data.UnitId, cancellationToken);
        if (unit is null)
        {
            return CatalogErrors.ReferenceNotFound(UnitErrors.Catalog);
        }

        if (command.Data.IsFullTank && (unit.UsesOdometer && command.Data.Odometer is null && unit.MeterKind == MeterKind.Odometer || unit.MeterKind == MeterKind.HourMeter && command.Data.Hours is null))
        {
            return FuelErrors.MeterRequired;
        }

        FuelLoad load;
        if (command.Id is { } id)
        {
            var existing = await db.FuelLoads.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
            if (existing is null)
            {
                return FuelErrors.NotFound;
            }

            if (!tenant.Has(Permissions.FuelManage) && existing.CreatedBy != user.DisplayName)
            {
                return Error.Forbidden("Fuel.NotOwner", "Solo puedes editar tus propias cargas.");
            }

            var updated = existing.Update(command.Data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            load = existing;
        }
        else
        {
            var created = FuelLoad.Create(command.Data);
            if (created.IsFailure)
            {
                return created.Error;
            }

            load = created.Value;
            db.FuelLoads.Add(load);
        }

        if (command.Data.Odometer is not null || command.Data.Hours is not null)
        {
            var odometer = command.Data.Odometer is { } km && km > unit.CurrentOdometer ? km : (decimal?)null;
            var hours = command.Data.Hours is { } h && h > unit.CurrentHours ? h : (decimal?)null;
            if (odometer is not null || hours is not null)
            {
                unit.RecordMeters(odometer, hours, command.Data.LoadedAt);
                db.UnitMeterReadings.Add(new UnitMeterReading(unit.Id, command.Data.LoadedAt, odometer, hours, MeterSource.Fuel, load.Id, "Carga de combustible", user.DisplayName));
            }
        }

        await EvaluateAsync(unit, load, cancellationToken);

        var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.FuelLoad, load.Id, $"{unit.Code} {load.LoadedAt:dd/MM/yyyy}", DocumentFacts.Fuel(load)),
            AutomationTrigger.Save, cancellationToken);
        if (checks.IsFailure)
        {
            return checks.Error;
        }

        if (load.IsAnomaly)
        {
            await notifications.PublishAsync(new NotificationMessage(
                NotificationEvent.FuelAnomaly,
                NotificationSeverity.Warning,
                $"Rendimiento fuera de rango en {unit.Code}",
                $"{unit.Name}: {load.Efficiency} {load.EfficiencyUnit} contra {unit.ExpectedEfficiency} esperado ({load.DeviationPercent}%).",
                $"combustible/cargas?unitId={unit.Id}",
                DeduplicationKey: $"fuel:{load.Id}"), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new FuelLoadDto(load.Id, unit.Id, unit.Code, load.LoadedAt, load.Liters, load.PricePerLiter, load.Amount, load.Odometer, load.Hours, load.IsFullTank, load.Station,
            load.PaymentMethod, load.DriverId, null, load.WorkOrderId, load.ReceiptFileId, load.ReceiptFileId is { } file ? files.GetUrl(file) : null,
            load.Efficiency, load.EfficiencyUnit, load.DeviationPercent, load.IsAnomaly, load.Notes, load.CreatedBy);
    }

    private async Task EvaluateAsync(Unit unit, FuelLoad load, CancellationToken cancellationToken)
    {
        if (!load.IsFullTank)
        {
            load.Evaluate(unit, null, 0, tenant.Tenant.Settings.FuelEfficiencyTolerancePercent);
            return;
        }

        var previousFull = await db.FuelLoads.AsNoTracking()
            .Where(f => f.UnitId == unit.Id && f.Id != load.Id && f.IsFullTank && f.LoadedAt < load.LoadedAt)
            .OrderByDescending(f => f.LoadedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var litersSince = previousFull is null
            ? 0
            : await db.FuelLoads.AsNoTracking()
                .Where(f => f.UnitId == unit.Id && f.Id != load.Id && f.LoadedAt > previousFull.LoadedAt && f.LoadedAt < load.LoadedAt)
                .SumAsync(f => (decimal?)f.Liters, cancellationToken) ?? 0;

        load.Evaluate(unit, previousFull, litersSince + load.Liters, tenant.Tenant.Settings.FuelEfficiencyTolerancePercent);
    }
}

public sealed record DeleteFuelLoadCommand(Guid Id) : ICommand;

internal sealed class DeleteFuelLoadCommandHandler(IApplicationDbContext db, IAuditLog audit) : ICommandHandler<DeleteFuelLoadCommand>
{
    public async Task<Result> Handle(DeleteFuelLoadCommand command, CancellationToken cancellationToken)
    {
        var load = await db.FuelLoads.FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken);
        if (load is null)
        {
            return FuelErrors.NotFound;
        }

        db.FuelLoads.Remove(load);
        audit.Record(AuditAction.Deleted, nameof(FuelLoad), load.Id, $"{load.Liters} l · {load.LoadedAt:dd/MM/yyyy}");
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record FuelEfficiencyRow(
    Guid UnitId,
    string UnitCode,
    string UnitName,
    MeterKind MeterKind,
    decimal Liters,
    decimal Amount,
    decimal? Distance,
    decimal? HoursWorked,
    decimal? Efficiency,
    string? EfficiencyUnit,
    decimal? Expected,
    decimal? DeviationPercent,
    int Loads,
    int Anomalies,
    decimal? CostPerKm,
    decimal? Revenue);

/// <summary>
/// Liters and cost per unit against kilometers or hours worked in the period (where money usually leaks):
/// efficiency of the period, deviation and revenue of the unit's tickets for comparison.
/// </summary>
public sealed record GetFuelEfficiencyQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<FuelEfficiencyRow>>;

internal sealed class GetFuelEfficiencyQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<GetFuelEfficiencyQuery, IReadOnlyList<FuelEfficiencyRow>>
{
    public async Task<Result<IReadOnlyList<FuelEfficiencyRow>>> Handle(GetFuelEfficiencyQuery query, CancellationToken cancellationToken)
    {
        var from = clock.StartOfDay(query.From);
        var to = clock.StartOfDay(query.To.AddDays(1));
        var loads = await db.FuelLoads.AsNoTracking().Where(f => f.LoadedAt >= from && f.LoadedAt < to).OrderBy(f => f.LoadedAt).ToListAsync(cancellationToken);
        var unitIds = loads.Select(l => l.UnitId).Distinct().ToList();
        var units = await db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);
        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => t.ServiceDate >= query.From && t.ServiceDate <= query.To && t.Status != TicketStatus.Draft && t.Status != TicketStatus.Cancelled)
            .Select(t => new { t.UnitIds, t.Subtotal })
            .ToListAsync(cancellationToken);

        var rows = new List<FuelEfficiencyRow>();
        foreach (var group in loads.GroupBy(l => l.UnitId))
        {
            if (!units.TryGetValue(group.Key, out var unit))
            {
                continue;
            }

            var list = group.ToList();
            var liters = list.Sum(l => l.Liters);
            decimal? distance = null, hours = null, efficiency = null, deviation = null;
            string? efficiencyUnit = null;

            var odometers = list.Where(l => l.Odometer is not null).ToList();
            var meters = list.Where(l => l.Hours is not null).ToList();
            if (unit.UsesOdometer && odometers.Count >= 2)
            {
                distance = odometers[^1].Odometer - odometers[0].Odometer;
                var litersAfterFirst = list.Where(l => l.LoadedAt > odometers[0].LoadedAt).Sum(l => l.Liters);
                if (distance > 0 && litersAfterFirst > 0)
                {
                    efficiency = Math.Round(distance.Value / litersAfterFirst, 2);
                    efficiencyUnit = "km/l";
                    deviation = unit.ExpectedEfficiency is > 0 ? Math.Round((efficiency.Value - unit.ExpectedEfficiency.Value) / unit.ExpectedEfficiency.Value * 100, 1) : null;
                }
            }
            else if (unit.UsesHourMeter && meters.Count >= 2)
            {
                hours = meters[^1].Hours - meters[0].Hours;
                var litersAfterFirst = list.Where(l => l.LoadedAt > meters[0].LoadedAt).Sum(l => l.Liters);
                if (hours > 0)
                {
                    efficiency = Math.Round(litersAfterFirst / hours.Value, 2);
                    efficiencyUnit = "l/h";
                    deviation = unit.ExpectedEfficiency is > 0 ? Math.Round((unit.ExpectedEfficiency.Value - efficiency.Value) / unit.ExpectedEfficiency.Value * 100, 1) : null;
                }
            }

            var amount = list.Sum(l => l.Amount);
            var revenue = tickets.Where(t => t.UnitIds.Contains(unit.Id)).Sum(t => t.Subtotal / Math.Max(1, t.UnitIds.Count));
            rows.Add(new FuelEfficiencyRow(unit.Id, unit.Code, unit.Name, unit.MeterKind, liters, amount, distance, hours, efficiency, efficiencyUnit, unit.ExpectedEfficiency,
                deviation, list.Count, list.Count(l => l.IsAnomaly), distance is > 0 ? Math.Round(amount / distance.Value, 2) : null, Math.Round(revenue, 2)));
        }

        return rows.OrderBy(r => r.DeviationPercent ?? 0).ThenBy(r => r.UnitCode).ToList();
    }
}
