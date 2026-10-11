using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.Fleet;

public enum MaintenanceTrigger
{
    Kilometers = 0,
    Hours = 1,
    Days = 2
}

/// <summary>Last service of a unit covered by a plan (the starting point of the next one).</summary>
public sealed class MaintenancePlanUnit : TenantEntity
{
    private MaintenancePlanUnit()
    {
    }

    internal MaintenancePlanUnit(Guid unitId, DateOnly? lastServiceOn, decimal? lastOdometer, decimal? lastHours)
    {
        UnitId = unitId;
        LastServiceOn = lastServiceOn;
        LastOdometer = lastOdometer;
        LastHours = lastHours;
    }

    public Guid PlanId { get; private set; }

    public Guid UnitId { get; private set; }

    public DateOnly? LastServiceOn { get; private set; }

    public decimal? LastOdometer { get; private set; }

    public decimal? LastHours { get; private set; }

    internal void Serviced(DateOnly on, decimal odometer, decimal hours)
    {
        LastServiceOn = on;
        LastOdometer = odometer;
        LastHours = hours;
    }
}

public sealed record MaintenanceDue(Guid PlanId, Guid UnitId, decimal? NextAt, decimal? Remaining, DateOnly? NextOn, bool IsDue, bool IsOverdue);

public sealed record MaintenancePlanUnitData(Guid UnitId, DateOnly? LastServiceOn, decimal? LastOdometer, decimal? LastHours);

public sealed record MaintenancePlanData(
    string Code,
    string Name,
    MaintenanceTrigger Trigger,
    decimal Interval,
    decimal WarningBefore,
    IReadOnlyList<string>? Tasks,
    decimal? EstimatedCost,
    IReadOnlyList<MaintenancePlanUnitData>? Units,
    string? Notes);

/// <summary>
/// Preventive program (every 10,000 km, 250 h or 90 days) with its checklist of tasks, applied to a set of units.
/// </summary>
public sealed class MaintenancePlan : CatalogAggregate
{
    private readonly List<MaintenancePlanUnit> _units = [];
    private List<string> _tasks = [];

    private MaintenancePlan()
    {
    }

    public MaintenanceTrigger Trigger { get; private set; }

    public decimal Interval { get; private set; }

    /// <summary>How early (km, h or days) the plan shows as due.</summary>
    public decimal WarningBefore { get; private set; }

    public IReadOnlyList<string> Tasks => _tasks;

    public decimal? EstimatedCost { get; private set; }

    public IReadOnlyList<MaintenancePlanUnit> Units => _units;

    public static Result<MaintenancePlan> Create(MaintenancePlanData data)
    {
        var plan = new MaintenancePlan();
        var result = plan.Update(data);
        return result.IsFailure ? result.Error : plan;
    }

    public Result Update(MaintenancePlanData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if (data.Interval <= 0 || data.WarningBefore < 0 || data.WarningBefore >= data.Interval)
        {
            return MaintenanceErrors.InvalidInterval;
        }

        if (data.EstimatedCost is < 0)
        {
            return UnitErrors.NegativeValue;
        }

        Trigger = data.Trigger;
        Interval = data.Interval;
        WarningBefore = data.WarningBefore;
        EstimatedCost = data.EstimatedCost;
        _tasks = (data.Tasks ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();

        var requested = (data.Units ?? []).GroupBy(u => u.UnitId).Select(g => g.First()).ToList();
        _units.RemoveAll(u => requested.All(r => r.UnitId != u.UnitId));
        foreach (var unit in requested)
        {
            var existing = _units.FirstOrDefault(u => u.UnitId == unit.UnitId);
            if (existing is null)
            {
                _units.Add(new MaintenancePlanUnit(unit.UnitId, unit.LastServiceOn, unit.LastOdometer, unit.LastHours));
            }
            else if (unit.LastServiceOn is not null)
            {
                existing.Serviced(unit.LastServiceOn.Value, unit.LastOdometer ?? existing.LastOdometer ?? 0, unit.LastHours ?? existing.LastHours ?? 0);
            }
        }

        return Result.Success();
    }

    /// <summary>A preventive order of this plan was closed for the unit: next service counts from now.</summary>
    public void RegisterService(Guid unitId, DateOnly on, decimal odometer, decimal hours) =>
        _units.FirstOrDefault(u => u.UnitId == unitId)?.Serviced(on, odometer, hours);

    public MaintenanceDue? DueFor(Unit unit, DateOnly today)
    {
        var tracking = _units.FirstOrDefault(u => u.UnitId == unit.Id);
        if (tracking is null)
        {
            return null;
        }

        switch (Trigger)
        {
            case MaintenanceTrigger.Kilometers:
            {
                var next = (tracking.LastOdometer ?? 0) + Interval;
                var remaining = next - unit.CurrentOdometer;
                return new MaintenanceDue(Id, unit.Id, next, remaining, null, remaining <= WarningBefore, remaining <= 0);
            }

            case MaintenanceTrigger.Hours:
            {
                var next = (tracking.LastHours ?? 0) + Interval;
                var remaining = next - unit.CurrentHours;
                return new MaintenanceDue(Id, unit.Id, next, remaining, null, remaining <= WarningBefore, remaining <= 0);
            }

            default:
            {
                var start = tracking.LastServiceOn ?? DateOnly.FromDateTime(CreatedAt.UtcDateTime);
                var nextOn = start.AddDays((int)Interval);
                var remaining = nextOn.DayNumber - today.DayNumber;
                return new MaintenanceDue(Id, unit.Id, null, remaining, nextOn, remaining <= WarningBefore, remaining <= 0);
            }
        }
    }
}

public enum MaintenanceType
{
    Preventive = 0,
    Corrective = 1
}

public enum MaintenanceOrderStatus
{
    Open = 0,
    InProgress = 1,
    WaitingParts = 2,
    Closed = 3,
    Cancelled = 4
}

public sealed record MaintenanceTask(string Description, bool Done);

/// <summary>Part or consumable used in a workshop order (from inventory or bought outside).</summary>
public sealed class MaintenancePart : TenantEntity
{
    private MaintenancePart()
    {
    }

    internal MaintenancePart(Guid? itemId, Guid? warehouseId, string description, decimal quantity, decimal unitCost)
    {
        ItemId = itemId;
        WarehouseId = warehouseId;
        Description = description;
        Quantity = quantity;
        UnitCost = unitCost;
        Amount = Money.Round(quantity * unitCost);
    }

    public Guid OrderId { get; private set; }

    public Guid? ItemId { get; private set; }

    public Guid? WarehouseId { get; private set; }

    public string Description { get; private set; } = null!;

    public decimal Quantity { get; private set; }

    public decimal UnitCost { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Inventory movement that consumed the part (set when the order is closed).</summary>
    public Guid? MovementId { get; private set; }

    internal void Consumed(Guid movementId) => MovementId = movementId;
}

public sealed record MaintenancePartData(Guid? ItemId, Guid? WarehouseId, string Description, decimal Quantity, decimal UnitCost);

public sealed record MaintenanceOrderData(
    Guid UnitId,
    MaintenanceType Type,
    Guid? PlanId,
    DateTimeOffset OpenedAt,
    string? ReportedFailure,
    string? Diagnosis,
    IReadOnlyList<MaintenanceTask>? Tasks,
    IReadOnlyList<MaintenancePartData>? Parts,
    decimal LaborHours,
    decimal LaborCostPerHour,
    string? Technicians,
    string? ExternalProvider,
    string? ExternalInvoice,
    decimal ExternalCost,
    string? Notes);

/// <summary>
/// Orden de taller: preventive or corrective maintenance of a unit, with tasks, parts, labor and external costs.
/// Opening it sends the unit to the workshop; closing it releases the unit and restarts its plan.
/// </summary>
public sealed class MaintenanceOrder : TenantAggregateRoot, IAuditReference
{
    private readonly List<MaintenancePart> _parts = [];
    private List<MaintenanceTask> _tasks = [];

    private MaintenanceOrder()
    {
    }

    public string Folio { get; private set; } = null!;

    public long FolioNumber { get; private set; }

    public Guid UnitId { get; private set; }

    public MaintenanceType Type { get; private set; }

    public Guid? PlanId { get; private set; }

    public MaintenanceOrderStatus Status { get; private set; } = MaintenanceOrderStatus.Open;

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public decimal? OdometerAtService { get; private set; }

    public decimal? HoursAtService { get; private set; }

    public string? ReportedFailure { get; private set; }

    public string? Diagnosis { get; private set; }

    public string? WorkPerformed { get; private set; }

    public IReadOnlyList<MaintenanceTask> Tasks => _tasks;

    public IReadOnlyList<MaintenancePart> Parts => _parts;

    public decimal LaborHours { get; private set; }

    public decimal LaborCostPerHour { get; private set; }

    public string? Technicians { get; private set; }

    public string? ExternalProvider { get; private set; }

    public string? ExternalInvoice { get; private set; }

    public decimal ExternalCost { get; private set; }

    public decimal PartsCost { get; private set; }

    public decimal LaborCost { get; private set; }

    public decimal TotalCost { get; private set; }

    public string? Notes { get; private set; }

    public string? CancellationReason { get; private set; }

    public string AuditReference => Folio;

    public bool IsOpen => Status is MaintenanceOrderStatus.Open or MaintenanceOrderStatus.InProgress or MaintenanceOrderStatus.WaitingParts;

    /// <summary>Hours the unit was out of operation (open → closed, or until now).</summary>
    public double DowntimeHours(DateTimeOffset now) => ((ClosedAt ?? now) - OpenedAt).TotalHours;

    public static Result<MaintenanceOrder> Open(string folio, long folioNumber, MaintenanceOrderData data)
    {
        var order = new MaintenanceOrder { Folio = folio, FolioNumber = folioNumber, UnitId = data.UnitId };
        var result = order.Apply(data);
        return result.IsFailure ? result.Error : order;
    }

    public Result Update(MaintenanceOrderData data)
    {
        if (!IsOpen)
        {
            return MaintenanceErrors.Closed;
        }

        if (data.UnitId != UnitId)
        {
            return MaintenanceErrors.UnitCannotChange;
        }

        return Apply(data);
    }

    public Result ChangeStatus(MaintenanceOrderStatus status)
    {
        if (!IsOpen || status is MaintenanceOrderStatus.Closed or MaintenanceOrderStatus.Cancelled)
        {
            return MaintenanceErrors.InvalidTransition;
        }

        Status = status;
        return Result.Success();
    }

    public Result Close(DateTimeOffset at, decimal? odometer, decimal? hours, string? workPerformed)
    {
        if (!IsOpen)
        {
            return MaintenanceErrors.Closed;
        }

        if (at < OpenedAt)
        {
            return MaintenanceErrors.InvalidDates;
        }

        if (string.IsNullOrWhiteSpace(workPerformed))
        {
            return MaintenanceErrors.WorkPerformedRequired;
        }

        Status = MaintenanceOrderStatus.Closed;
        ClosedAt = at;
        OdometerAtService = odometer;
        HoursAtService = hours;
        WorkPerformed = workPerformed.Trim();
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return MaintenanceErrors.ReasonRequired;
        }

        if (!IsOpen)
        {
            return MaintenanceErrors.Closed;
        }

        Status = MaintenanceOrderStatus.Cancelled;
        CancellationReason = reason.Trim();
        return Result.Success();
    }

    public void PartConsumed(Guid partId, Guid movementId) => _parts.FirstOrDefault(p => p.Id == partId)?.Consumed(movementId);

    private Result Apply(MaintenanceOrderData data)
    {
        if (data.LaborHours < 0 || data.LaborCostPerHour < 0 || data.ExternalCost < 0)
        {
            return UnitErrors.NegativeValue;
        }

        var parts = data.Parts ?? [];
        if (parts.Any(p => p.Quantity <= 0 || p.UnitCost < 0 || string.IsNullOrWhiteSpace(p.Description)))
        {
            return MaintenanceErrors.InvalidPart;
        }

        Type = data.Type;
        PlanId = data.Type == MaintenanceType.Preventive ? data.PlanId : null;
        OpenedAt = data.OpenedAt;
        ReportedFailure = Clean(data.ReportedFailure);
        Diagnosis = Clean(data.Diagnosis);
        _tasks = (data.Tasks ?? []).Where(t => !string.IsNullOrWhiteSpace(t.Description)).Select(t => t with { Description = t.Description.Trim() }).ToList();
        LaborHours = data.LaborHours;
        LaborCostPerHour = data.LaborCostPerHour;
        Technicians = Clean(data.Technicians);
        ExternalProvider = Clean(data.ExternalProvider);
        ExternalInvoice = Clean(data.ExternalInvoice);
        ExternalCost = Money.Round(data.ExternalCost);
        Notes = Clean(data.Notes);

        // Parts already consumed from inventory are kept; the rest are replaced.
        _parts.RemoveAll(p => p.MovementId is null);
        _parts.AddRange(parts.Select(p => new MaintenancePart(p.ItemId, p.WarehouseId, p.Description.Trim(), p.Quantity, p.UnitCost)));

        PartsCost = Money.Round(_parts.Sum(p => p.Amount));
        LaborCost = Money.Round(LaborHours * LaborCostPerHour);
        TotalCost = PartsCost + LaborCost + ExternalCost;
        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class MaintenanceErrors
{
    public static readonly Error PlanNotFound = Error.NotFound("Maintenance.PlanNotFound", "El programa de mantenimiento no existe.");

    public static readonly Error NotFound = Error.NotFound("Maintenance.NotFound", "La orden de taller no existe.");

    public static readonly Error InvalidInterval = Error.Validation(
        "Maintenance.InvalidInterval", "El intervalo debe ser mayor a cero y el aviso menor que el intervalo.");

    public static readonly Error InvalidPart = Error.Validation("Maintenance.InvalidPart", "Cada refacción necesita descripción, cantidad mayor a cero y costo.");

    public static readonly Error Closed = Error.Conflict("Maintenance.Closed", "La orden de taller ya está cerrada o cancelada.");

    public static readonly Error UnitCannotChange = Error.Validation("Maintenance.UnitCannotChange", "La unidad de una orden no se puede cambiar.");

    public static readonly Error InvalidTransition = Error.Conflict("Maintenance.InvalidTransition", "Cambio de estatus no permitido.");

    public static readonly Error InvalidDates = Error.Validation("Maintenance.InvalidDates", "El cierre no puede ser anterior a la apertura.");

    public static readonly Error WorkPerformedRequired = Error.Validation("Maintenance.WorkPerformedRequired", "Describe el trabajo realizado.");

    public static readonly Error ReasonRequired = Error.Validation("Maintenance.ReasonRequired", "Indica el motivo.");

    public static readonly Error AlreadyOpen = Error.Conflict("Maintenance.AlreadyOpen", "La unidad ya tiene una orden de taller abierta.");
}
