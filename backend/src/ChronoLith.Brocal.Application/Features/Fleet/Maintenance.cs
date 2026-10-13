using System.Linq.Expressions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Automation;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Inventory;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Fleet;

// ---- Plans ----------------------------------------------------------------------------------------------

public sealed record MaintenancePlanUnitDto(Guid UnitId, string? UnitCode, DateOnly? LastServiceOn, decimal? LastOdometer, decimal? LastHours);

public sealed record MaintenancePlanDto(
    Guid Id,
    string Code,
    string Name,
    MaintenanceTrigger Trigger,
    decimal Interval,
    decimal WarningBefore,
    IReadOnlyList<string> Tasks,
    decimal? EstimatedCost,
    IReadOnlyList<MaintenancePlanUnitDto> Units,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal sealed class MaintenancePlanDataValidator : AbstractValidator<MaintenancePlanData>
{
    public MaintenancePlanDataValidator()
    {
        RuleFor(p => p.Code).BusinessCode();
        RuleFor(p => p.Name).RequiredName();
        RuleFor(p => p.Interval).GreaterThan(0);
        RuleFor(p => p.WarningBefore).GreaterThanOrEqualTo(0).LessThan(p => p.Interval).WithMessage("El aviso debe ser menor que el intervalo.");
    }
}

internal sealed class MaintenancePlanSpec(IApplicationDbContext db) : ICatalogSpec<MaintenancePlan, MaintenancePlanData, MaintenancePlanDto>
{
    public string Name => "MaintenancePlans";

    public Expression<Func<MaintenancePlan, MaintenancePlanDto>> Projection => p => new MaintenancePlanDto(
        p.Id, p.Code, p.Name, p.Trigger, p.Interval, p.WarningBefore, p.Tasks, p.EstimatedCost,
        p.Units.Select(u => new MaintenancePlanUnitDto(u.UnitId, db.Units.Where(x => x.Id == u.UnitId).Select(x => x.Code).FirstOrDefault(), u.LastServiceOn, u.LastOdometer, u.LastHours)).ToList(),
        p.Notes, p.IsActive, p.CreatedAt, p.UpdatedAt);

    public string CodeOf(MaintenancePlanData data) => data.Code;

    public Result<MaintenancePlan> Create(MaintenancePlanData data) => MaintenancePlan.Create(data);

    public Result Update(MaintenancePlan entity, MaintenancePlanData data) => entity.Update(data);

    public Task<Result> ValidateAsync(MaintenancePlanData data, MaintenancePlan? existing, CancellationToken cancellationToken) =>
        References.EnsureAllAsync<Unit>(db, data.Units?.Select(u => u.UnitId), UnitErrors.Catalog, cancellationToken);

    public Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        db.MaintenanceOrders.AnyAsync(o => o.PlanId == id, cancellationToken);
}

public sealed record MaintenanceDueDto(
    Guid PlanId,
    string PlanName,
    MaintenanceTrigger Trigger,
    Guid UnitId,
    string UnitCode,
    string UnitName,
    UnitStatus UnitStatus,
    decimal? NextAt,
    decimal? Remaining,
    DateOnly? NextOn,
    bool IsDue,
    bool IsOverdue,
    bool HasOpenOrder);

/// <summary>Preventive services of every unit with how much is left (km, hours or days).</summary>
public sealed record GetMaintenanceDueQuery(bool OnlyDue) : IQuery<IReadOnlyList<MaintenanceDueDto>>;

internal sealed class GetMaintenanceDueQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<GetMaintenanceDueQuery, IReadOnlyList<MaintenanceDueDto>>
{
    public async Task<Result<IReadOnlyList<MaintenanceDueDto>>> Handle(GetMaintenanceDueQuery query, CancellationToken cancellationToken)
    {
        var plans = await db.MaintenancePlans.AsNoTracking().Include(p => p.Units).Where(p => p.IsActive).ToListAsync(cancellationToken);
        var unitIds = plans.SelectMany(p => p.Units.Select(u => u.UnitId)).Distinct().ToList();
        var units = await db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id) && u.IsActive).ToDictionaryAsync(u => u.Id, cancellationToken);
        var openOrders = await db.MaintenanceOrders.AsNoTracking()
            .Where(o => o.Status == MaintenanceOrderStatus.Open || o.Status == MaintenanceOrderStatus.InProgress || o.Status == MaintenanceOrderStatus.WaitingParts)
            .Select(o => o.UnitId)
            .ToListAsync(cancellationToken);

        var result = new List<MaintenanceDueDto>();
        foreach (var plan in plans)
        {
            foreach (var tracking in plan.Units)
            {
                if (!units.TryGetValue(tracking.UnitId, out var unit) || plan.DueFor(unit, clock.Today) is not { } due)
                {
                    continue;
                }

                if (query.OnlyDue && !due.IsDue)
                {
                    continue;
                }

                result.Add(new MaintenanceDueDto(plan.Id, plan.Name, plan.Trigger, unit.Id, unit.Code, unit.Name, unit.Status, due.NextAt, due.Remaining, due.NextOn,
                    due.IsDue, due.IsOverdue, openOrders.Contains(unit.Id)));
            }
        }

        return result.OrderByDescending(r => r.IsOverdue).ThenByDescending(r => r.IsDue).ThenBy(r => r.Remaining).ToList();
    }
}

// ---- Workshop orders ------------------------------------------------------------------------------------

public sealed record MaintenanceOrderSummaryDto(
    Guid Id,
    string Folio,
    Guid UnitId,
    string? UnitCode,
    string? UnitName,
    MaintenanceType Type,
    MaintenanceOrderStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    string? ReportedFailure,
    decimal TotalCost);

public sealed record MaintenancePartDto(Guid Id, Guid? ItemId, Guid? WarehouseId, string Description, decimal Quantity, decimal UnitCost, decimal Amount, bool Consumed);

public sealed record MaintenanceOrderDetailDto(
    MaintenanceOrderSummaryDto Summary,
    Guid? PlanId,
    string? PlanName,
    string? Diagnosis,
    string? WorkPerformed,
    IReadOnlyList<MaintenanceTask> Tasks,
    IReadOnlyList<MaintenancePartDto> Parts,
    decimal LaborHours,
    decimal LaborCostPerHour,
    string? Technicians,
    string? ExternalProvider,
    string? ExternalInvoice,
    decimal ExternalCost,
    decimal PartsCost,
    decimal LaborCost,
    decimal? OdometerAtService,
    decimal? HoursAtService,
    double DowntimeHours,
    string? Notes,
    string? CancellationReason);

public sealed record SearchMaintenanceOrdersQuery(string? Search, MaintenanceOrderStatus? Status, Guid? UnitId, MaintenanceType? Type, int Page = 1, int PageSize = 25)
    : IQuery<PagedResult<MaintenanceOrderSummaryDto>>;

internal sealed class SearchMaintenanceOrdersQueryHandler(IApplicationDbContext db) : IQueryHandler<SearchMaintenanceOrdersQuery, PagedResult<MaintenanceOrderSummaryDto>>
{
    public async Task<Result<PagedResult<MaintenanceOrderSummaryDto>>> Handle(SearchMaintenanceOrdersQuery query, CancellationToken cancellationToken)
    {
        var request = new PageRequest(query.Search, query.Page, query.PageSize);
        var orders = db.MaintenanceOrders.AsNoTracking()
            .WhereIf(query.Status is not null, o => o.Status == query.Status)
            .WhereIf(query.UnitId is not null, o => o.UnitId == query.UnitId)
            .WhereIf(query.Type is not null, o => o.Type == query.Type);

        if (request.Term is { } term)
        {
            orders = orders.Where(o => o.Folio.ToLower().Contains(term) || (o.ReportedFailure != null && o.ReportedFailure.ToLower().Contains(term))
                                       || db.Units.Any(u => u.Id == o.UnitId && u.Code.ToLower().Contains(term)));
        }

        return await orders.OrderByDescending(o => o.OpenedAt).Select(MaintenanceProjection.Summary(db)).ToPagedAsync(request, cancellationToken);
    }
}

internal static class MaintenanceProjection
{
    public static Expression<Func<MaintenanceOrder, MaintenanceOrderSummaryDto>> Summary(IApplicationDbContext db) => o => new MaintenanceOrderSummaryDto(
        o.Id, o.Folio, o.UnitId,
        db.Units.Where(u => u.Id == o.UnitId).Select(u => u.Code).FirstOrDefault(),
        db.Units.Where(u => u.Id == o.UnitId).Select(u => u.Name).FirstOrDefault(),
        o.Type, o.Status, o.OpenedAt, o.ClosedAt, o.ReportedFailure, o.TotalCost);
}

public sealed record GetMaintenanceOrderQuery(Guid Id) : IQuery<MaintenanceOrderDetailDto>;

internal sealed class GetMaintenanceOrderQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<GetMaintenanceOrderQuery, MaintenanceOrderDetailDto>
{
    public async Task<Result<MaintenanceOrderDetailDto>> Handle(GetMaintenanceOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await db.MaintenanceOrders.AsNoTracking().Include(o => o.Parts).FirstOrDefaultAsync(o => o.Id == query.Id, cancellationToken);
        if (order is null)
        {
            return MaintenanceErrors.NotFound;
        }

        var summary = await db.MaintenanceOrders.AsNoTracking().Where(o => o.Id == order.Id).Select(MaintenanceProjection.Summary(db)).FirstAsync(cancellationToken);
        var plan = order.PlanId is null ? null : await db.MaintenancePlans.AsNoTracking().Where(p => p.Id == order.PlanId).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);

        return new MaintenanceOrderDetailDto(summary, order.PlanId, plan, order.Diagnosis, order.WorkPerformed, order.Tasks,
            order.Parts.Select(p => new MaintenancePartDto(p.Id, p.ItemId, p.WarehouseId, p.Description, p.Quantity, p.UnitCost, p.Amount, p.MovementId is not null)).ToList(),
            order.LaborHours, order.LaborCostPerHour, order.Technicians, order.ExternalProvider, order.ExternalInvoice, order.ExternalCost, order.PartsCost, order.LaborCost,
            order.OdometerAtService, order.HoursAtService, Math.Round(order.DowntimeHours(clock.UtcNow), 1), order.Notes, order.CancellationReason);
    }
}

/// <summary>Opens (or updates) a workshop order. Opening it sends the unit to the workshop.</summary>
public sealed record SaveMaintenanceOrderCommand(Guid? Id, MaintenanceOrderData Data) : ICommand<Guid>;

internal sealed class SaveMaintenanceOrderCommandValidator : AbstractValidator<SaveMaintenanceOrderCommand>
{
    public SaveMaintenanceOrderCommandValidator()
    {
        RuleFor(c => c.Data.UnitId).RequiredId("la unidad");
        RuleFor(c => c.Data.LaborHours).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Data.ExternalCost).GreaterThanOrEqualTo(0);
    }
}

internal sealed class SaveMaintenanceOrderCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    IFolioService folios,
    IUserContext user,
    ITenantClock clock,
    DocumentFacts facts,
    IBusinessRules rules) : ICommandHandler<SaveMaintenanceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveMaintenanceOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.Data.PlanId is not null && !await db.MaintenancePlans.AnyAsync(p => p.Id == command.Data.PlanId, cancellationToken))
        {
            return MaintenanceErrors.PlanNotFound;
        }

        var items = command.Data.Parts?.Where(p => p.ItemId is not null).Select(p => p.ItemId!.Value).Distinct().ToList() ?? [];
        if (items.Count > 0 && await db.InventoryItems.CountAsync(i => items.Contains(i.Id), cancellationToken) != items.Count)
        {
            return CatalogErrors.ReferenceNotFound("InventoryItems");
        }

        if (command.Id is { } id)
        {
            var existing = await db.MaintenanceOrders.Include(o => o.Parts).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
            if (existing is null)
            {
                return MaintenanceErrors.NotFound;
            }

            var updated = existing.Update(command.Data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.MaintenanceOrder, existing.Id, existing.Folio, facts.Maintenance(existing)), AutomationTrigger.Save, cancellationToken);
            if (checks.IsFailure)
            {
                return checks.Error;
            }

            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == command.Data.UnitId, cancellationToken);
        if (unit is null)
        {
            return CatalogErrors.ReferenceNotFound(UnitErrors.Catalog);
        }

        if (await db.MaintenanceOrders.AnyAsync(o => o.UnitId == unit.Id && (o.Status == MaintenanceOrderStatus.Open || o.Status == MaintenanceOrderStatus.InProgress || o.Status == MaintenanceOrderStatus.WaitingParts), cancellationToken))
        {
            return MaintenanceErrors.AlreadyOpen;
        }

        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var number = await folios.NextAsync(FolioKeys.Maintenance, ct);
            var opened = MaintenanceOrder.Open(FolioSequence.Format(tenant.Tenant.Settings.MaintenancePrefix, number), number, command.Data);
            if (opened.IsFailure)
            {
                return Result.Failure<Guid>(opened.Error);
            }

            var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.MaintenanceOrder, opened.Value.Id, opened.Value.Folio, facts.Maintenance(opened.Value)), AutomationTrigger.Save, ct);
            if (checks.IsFailure)
            {
                return Result.Failure<Guid>(checks.Error);
            }

            var previous = unit.Status;
            if (previous != UnitStatus.Workshop)
            {
                unit.ChangeStatus(UnitStatus.Workshop);
                db.UnitStatusChanges.Add(new UnitStatusChange(unit.Id, clock.UtcNow, previous, UnitStatus.Workshop, $"Orden de taller {opened.Value.Folio}", user.DisplayName));
            }

            db.MaintenanceOrders.Add(opened.Value);
            await db.SaveChangesAsync(ct);
            return Result.Success(opened.Value.Id);
        }, cancellationToken);
    }
}

public sealed record ChangeMaintenanceStatusCommand(Guid Id, MaintenanceOrderStatus Status) : ICommand;

internal sealed class ChangeMaintenanceStatusCommandHandler(IApplicationDbContext db) : ICommandHandler<ChangeMaintenanceStatusCommand>
{
    public async Task<Result> Handle(ChangeMaintenanceStatusCommand command, CancellationToken cancellationToken)
    {
        var order = await db.MaintenanceOrders.FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
        if (order is null)
        {
            return MaintenanceErrors.NotFound;
        }

        var result = order.ChangeStatus(command.Status);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>
/// Closes a workshop order: consumes the parts from inventory, records the meter reading, restarts the preventive
/// plan and releases the unit ("unidad liberada de taller" notification).
/// </summary>
public sealed record CloseMaintenanceOrderCommand(Guid Id, DateTimeOffset? ClosedAt, decimal? Odometer, decimal? Hours, string WorkPerformed, bool ReleaseUnit = true) : ICommand;

internal sealed class CloseMaintenanceOrderCommandHandler(
    IApplicationDbContext db,
    IUserContext user,
    ITenantClock clock,
    ITenantContext tenant,
    INotificationPublisher notifications,
    IAuditLog audit,
    DocumentFacts facts,
    IBusinessRules rules,
    IWorkflowEngine workflows) : ICommandHandler<CloseMaintenanceOrderCommand>
{
    public async Task<Result> Handle(CloseMaintenanceOrderCommand command, CancellationToken cancellationToken)
    {
        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var order = await db.MaintenanceOrders.Include(o => o.Parts).FirstOrDefaultAsync(o => o.Id == command.Id, ct);
            if (order is null)
            {
                return MaintenanceErrors.NotFound;
            }

            // Closing is the guarded moment: rules and approval workflow (a held close answers 202).
            if (order.IsOpen)
            {
                var values = facts.Maintenance(order);
                var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.MaintenanceOrder, order.Id, order.Folio, values), AutomationTrigger.Close, ct);
                if (checks.IsFailure)
                {
                    return checks;
                }

                var gate = await workflows.GateAsync(new WorkflowSubject(AutomationDocument.MaintenanceOrder, order.Id, order.Folio, values), GatePersistence.Isolated, ct);
                if (!gate.Passed)
                {
                    return Result.Failure(gate.ToError());
                }
            }

            var unit = await db.Units.FirstAsync(u => u.Id == order.UnitId, ct);
            var closedAt = command.ClosedAt ?? clock.UtcNow;
            var closed = order.Close(closedAt, command.Odometer, command.Hours, command.WorkPerformed);
            if (closed.IsFailure)
            {
                return closed;
            }

            if (command.Odometer is not null || command.Hours is not null)
            {
                var meters = unit.RecordMeters(command.Odometer, command.Hours, closedAt);
                if (meters.IsFailure)
                {
                    return meters;
                }

                db.UnitMeterReadings.Add(new UnitMeterReading(unit.Id, closedAt, command.Odometer, command.Hours, MeterSource.Maintenance, order.Id, order.Folio, user.DisplayName));
            }

            if (tenant.IsModuleEnabled(BrocalModules.Inventory))
            {
                foreach (var part in order.Parts.Where(p => p.ItemId is not null && p.MovementId is null))
                {
                    var consumed = await ConsumeAsync(order, part, closedAt, ct);
                    if (consumed.IsFailure)
                    {
                        return Result.Failure(consumed.Error);
                    }

                    order.PartConsumed(part.Id, consumed.Value);
                }
            }

            if (order.PlanId is { } planId)
            {
                var plan = await db.MaintenancePlans.Include(p => p.Units).FirstOrDefaultAsync(p => p.Id == planId, ct);
                plan?.RegisterService(unit.Id, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(closedAt, clock.TimeZone).DateTime), unit.CurrentOdometer, unit.CurrentHours);
            }

            if (command.ReleaseUnit && unit.Status == UnitStatus.Workshop)
            {
                unit.ChangeStatus(UnitStatus.Available, order.Id);
                db.UnitStatusChanges.Add(new UnitStatusChange(unit.Id, closedAt, UnitStatus.Workshop, UnitStatus.Available, $"Cierre de {order.Folio}", user.DisplayName));
                await notifications.PublishAsync(new NotificationMessage(
                    NotificationEvent.UnitReleased,
                    NotificationSeverity.Success,
                    $"Unidad {unit.Code} liberada de taller",
                    $"{unit.Name} quedó disponible tras la orden {order.Folio}: {order.WorkPerformed}",
                    $"flota/unidades/{unit.Id}"), ct);
            }

            audit.Record(AuditAction.StatusChanged, nameof(MaintenanceOrder), order.Id, $"{order.Folio} cerrada", null, new { order.TotalCost });
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    private async Task<Result<Guid>> ConsumeAsync(MaintenanceOrder order, MaintenancePart part, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var warehouseId = part.WarehouseId ?? await db.StockLevels.Where(s => s.ItemId == part.ItemId && s.Quantity >= part.Quantity)
            .Select(s => (Guid?)s.WarehouseId).FirstOrDefaultAsync(cancellationToken);
        if (warehouseId is null)
        {
            return InventoryErrors.InsufficientStock(0);
        }

        var stock = await db.StockLevels.FirstOrDefaultAsync(s => s.ItemId == part.ItemId && s.WarehouseId == warehouseId, cancellationToken);
        if (stock is null)
        {
            return InventoryErrors.InsufficientStock(0);
        }

        var removed = stock.Remove(part.Quantity);
        if (removed.IsFailure)
        {
            return Error.Conflict(removed.Error.Code, $"{part.Description}: {removed.Error.Description}");
        }

        var movement = InventoryMovement.Create(new MovementData(MovementType.Maintenance, part.ItemId!.Value, warehouseId.Value, null, part.Quantity, part.UnitCost, at,
            order.Folio, order.Id, null, null, order.UnitId, null), part.UnitCost);
        if (movement.IsFailure)
        {
            return movement.Error;
        }

        db.InventoryMovements.Add(movement.Value);
        return movement.Value.Id;
    }
}

public sealed record CancelMaintenanceOrderCommand(Guid Id, string Reason) : ICommand;

internal sealed class CancelMaintenanceOrderCommandHandler(IApplicationDbContext db, IUserContext user, ITenantClock clock, IWorkflowEngine workflows) : ICommandHandler<CancelMaintenanceOrderCommand>
{
    public async Task<Result> Handle(CancelMaintenanceOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.MaintenanceOrders.FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
        if (order is null)
        {
            return MaintenanceErrors.NotFound;
        }

        var result = order.Cancel(command.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        var unit = await db.Units.FirstAsync(u => u.Id == order.UnitId, cancellationToken);
        if (unit.Status == UnitStatus.Workshop)
        {
            unit.ChangeStatus(UnitStatus.Available, order.Id);
            db.UnitStatusChanges.Add(new UnitStatusChange(unit.Id, clock.UtcNow, UnitStatus.Workshop, UnitStatus.Available, $"Cancelación de {order.Folio}", user.DisplayName));
        }

        await workflows.CancelPendingAsync(AutomationDocument.MaintenanceOrder, order.Id, "La orden de taller se canceló.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
