using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.Operations;

public enum WorkOrderStatus
{
    Scheduled = 0,
    Dispatched = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}

public enum WorkOrderPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

/// <summary>Service expected to be billed (it pre-fills the lines of the ticket born from the order).</summary>
public sealed record PlannedService(Guid ServiceId, decimal Quantity);

public sealed record WorkOrderData(
    Guid CustomerId,
    Guid? ContractId,
    Guid SiteId,
    Guid ServiceTypeId,
    string Description,
    WorkOrderPriority Priority,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    IReadOnlyList<Guid>? UnitIds,
    Guid? CrewId,
    IReadOnlyList<Guid>? EmployeeIds,
    IReadOnlyList<PlannedService>? PlannedServices,
    string? Instructions,
    string? CustomerReference);

public sealed record WorkOrderAssignedEvent(Guid WorkOrderId) : IDomainEvent;

/// <summary>
/// Orden de trabajo: which units and crew go to which site and when. Tickets are born from it (customer, site,
/// service type, units, crew and planned services are copied).
/// </summary>
public sealed class WorkOrder : TenantAggregateRoot, IAuditReference
{
    private List<Guid> _unitIds = [];
    private List<Guid> _employeeIds = [];
    private List<PlannedService> _plannedServices = [];

    private WorkOrder()
    {
    }

    public string Folio { get; private set; } = null!;

    public long FolioNumber { get; private set; }

    public WorkOrderStatus Status { get; private set; } = WorkOrderStatus.Scheduled;

    public WorkOrderPriority Priority { get; private set; } = WorkOrderPriority.Normal;

    public Guid CustomerId { get; private set; }

    public Guid? ContractId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid ServiceTypeId { get; private set; }

    /// <summary>Quote the order came from, if any.</summary>
    public Guid? QuoteId { get; private set; }

    public string Description { get; private set; } = null!;

    public string? Instructions { get; private set; }

    /// <summary>Reference of the customer (orden de servicio, solicitud, pedido).</summary>
    public string? CustomerReference { get; private set; }

    public DateTimeOffset ScheduledStart { get; private set; }

    public DateTimeOffset ScheduledEnd { get; private set; }

    public IReadOnlyList<Guid> UnitIds => _unitIds;

    public Guid? CrewId { get; private set; }

    public IReadOnlyList<Guid> EmployeeIds => _employeeIds;

    public IReadOnlyList<PlannedService> PlannedServices => _plannedServices;

    public DateTimeOffset? DispatchedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public string AuditReference => Folio;

    public bool IsOpen => Status is WorkOrderStatus.Scheduled or WorkOrderStatus.Dispatched or WorkOrderStatus.InProgress;

    public static Result<WorkOrder> Create(string folio, long folioNumber, WorkOrderData data, Guid? quoteId = null)
    {
        var order = new WorkOrder { Folio = folio, FolioNumber = folioNumber, QuoteId = quoteId };
        var result = order.Apply(data);
        return result.IsFailure ? result.Error : order;
    }

    public Result Update(WorkOrderData data)
    {
        if (Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
        {
            return WorkOrderErrors.Closed;
        }

        var hadAssignments = _unitIds.Count + _employeeIds.Count + (CrewId is null ? 0 : 1);
        var result = Apply(data);
        if (result.IsSuccess && Status == WorkOrderStatus.Dispatched && hadAssignments == 0)
        {
            Raise(new WorkOrderAssignedEvent(Id));
        }

        return result;
    }

    public Result Dispatch(DateTimeOffset at)
    {
        if (Status != WorkOrderStatus.Scheduled)
        {
            return WorkOrderErrors.InvalidTransition(Status, WorkOrderStatus.Dispatched);
        }

        if (_unitIds.Count == 0 && CrewId is null && _employeeIds.Count == 0)
        {
            return WorkOrderErrors.NothingAssigned;
        }

        Status = WorkOrderStatus.Dispatched;
        DispatchedAt = at;
        Raise(new WorkOrderAssignedEvent(Id));
        return Result.Success();
    }

    public Result Start(DateTimeOffset at)
    {
        if (Status is not (WorkOrderStatus.Scheduled or WorkOrderStatus.Dispatched))
        {
            return WorkOrderErrors.InvalidTransition(Status, WorkOrderStatus.InProgress);
        }

        Status = WorkOrderStatus.InProgress;
        DispatchedAt ??= at;
        StartedAt = at;
        return Result.Success();
    }

    public Result Complete(DateTimeOffset at)
    {
        if (!IsOpen)
        {
            return WorkOrderErrors.InvalidTransition(Status, WorkOrderStatus.Completed);
        }

        Status = WorkOrderStatus.Completed;
        StartedAt ??= at;
        CompletedAt = at;
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return TicketErrors.ReasonRequired;
        }

        if (!IsOpen)
        {
            return WorkOrderErrors.InvalidTransition(Status, WorkOrderStatus.Cancelled);
        }

        Status = WorkOrderStatus.Cancelled;
        CancellationReason = reason.Trim();
        return Result.Success();
    }

    /// <summary>A ticket was captured from this order: an order still scheduled moves to in progress.</summary>
    public void TicketCaptured(DateTimeOffset at)
    {
        if (Status is WorkOrderStatus.Scheduled or WorkOrderStatus.Dispatched)
        {
            Status = WorkOrderStatus.InProgress;
            DispatchedAt ??= at;
            StartedAt = at;
        }
    }

    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => ScheduledStart < end && start < ScheduledEnd;

    public bool IsAssignedTo(Guid? employeeId, IReadOnlyCollection<Guid> crewIdsOfEmployee) =>
        employeeId is not null && (_employeeIds.Contains(employeeId.Value) || CrewId is not null && crewIdsOfEmployee.Contains(CrewId.Value));

    private Result Apply(WorkOrderData data)
    {
        if (string.IsNullOrWhiteSpace(data.Description) || data.Description.Trim().Length > 2000)
        {
            return WorkOrderErrors.DescriptionRequired;
        }

        if (data.ScheduledEnd <= data.ScheduledStart)
        {
            return WorkOrderErrors.InvalidSchedule;
        }

        if ((data.PlannedServices ?? []).Any(s => s.Quantity <= 0))
        {
            return WorkOrderErrors.InvalidQuantity;
        }

        CustomerId = data.CustomerId;
        ContractId = data.ContractId;
        SiteId = data.SiteId;
        ServiceTypeId = data.ServiceTypeId;
        Description = data.Description.Trim();
        Priority = data.Priority;
        ScheduledStart = data.ScheduledStart;
        ScheduledEnd = data.ScheduledEnd;
        _unitIds = (data.UnitIds ?? []).Distinct().ToList();
        CrewId = data.CrewId;
        _employeeIds = (data.EmployeeIds ?? []).Distinct().ToList();
        _plannedServices = (data.PlannedServices ?? []).GroupBy(s => s.ServiceId).Select(g => new PlannedService(g.Key, g.Sum(s => s.Quantity))).ToList();
        Instructions = string.IsNullOrWhiteSpace(data.Instructions) ? null : data.Instructions.Trim();
        CustomerReference = string.IsNullOrWhiteSpace(data.CustomerReference) ? null : data.CustomerReference.Trim();
        return Result.Success();
    }
}

public static class WorkOrderErrors
{
    public static readonly Error NotFound = Error.NotFound("WorkOrders.NotFound", "La orden de trabajo no existe.");

    public static readonly Error DescriptionRequired = Error.Validation("WorkOrders.DescriptionRequired", "Describe el trabajo (máximo 2000 caracteres).");

    public static readonly Error InvalidSchedule = Error.Validation("WorkOrders.InvalidSchedule", "El término programado debe ser posterior al inicio.");

    public static readonly Error InvalidQuantity = Error.Validation("WorkOrders.InvalidQuantity", "Las cantidades planeadas deben ser mayores a cero.");

    public static readonly Error NothingAssigned = Error.Validation("WorkOrders.NothingAssigned", "Asigna al menos una unidad, cuadrilla o persona antes de despachar.");

    public static readonly Error Closed = Error.Conflict("WorkOrders.Closed", "La orden ya está terminada o cancelada.");

    public static Error InvalidTransition(WorkOrderStatus from, WorkOrderStatus to) =>
        Error.Conflict("WorkOrders.InvalidTransition", $"No se puede pasar la orden de '{from}' a '{to}'.");
}
