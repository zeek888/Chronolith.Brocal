using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Automation;
using ChronoLith.Brocal.Application.Features.Pricing;
using ChronoLith.Brocal.Application.Features.Tickets;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Dispatch;

public sealed record WorkOrderSummaryDto(
    Guid Id,
    string Folio,
    WorkOrderStatus Status,
    WorkOrderPriority Priority,
    Guid CustomerId,
    string? CustomerName,
    string? CustomerColor,
    Guid SiteId,
    string? SiteName,
    Guid ServiceTypeId,
    string? ServiceTypeName,
    string? ServiceTypeColor,
    string Description,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    IReadOnlyList<Guid> UnitIds,
    Guid? CrewId,
    string? CrewName,
    string? CustomerReference,
    int TicketCount);

public sealed record PlannedServiceDto(Guid ServiceId, string? ServiceName, string? UnitName, decimal Quantity);

public sealed record WorkOrderDetailDto(
    WorkOrderSummaryDto Summary,
    Guid? ContractId,
    string? ContractName,
    string? Instructions,
    IReadOnlyList<NamedRef> Units,
    IReadOnlyList<NamedRef> Employees,
    IReadOnlyList<PlannedServiceDto> PlannedServices,
    Guid? QuoteId,
    string? QuoteFolio,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? CancellationReason,
    IReadOnlyList<TicketSummaryDto> Tickets,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CreatedAt,
    string? CreatedBy);

internal static class WorkOrderVisibility
{
    public static IQueryable<WorkOrder> Visible(this IQueryable<WorkOrder> orders, IApplicationDbContext db, ITenantContext tenant)
    {
        if (tenant.Has(Permissions.DispatchRead))
        {
            return orders;
        }

        var employeeId = tenant.Membership?.EmployeeId;
        if (employeeId is null)
        {
            return orders.Where(_ => false);
        }

        var id = employeeId.Value;
        return orders.Where(w => w.EmployeeIds.Contains(id) || db.Crews.Any(c => c.Id == w.CrewId && c.MemberIds.Contains(id)));
    }

    public static IQueryable<WorkOrderSummaryDto> ToSummaries(this IQueryable<WorkOrder> orders, IApplicationDbContext db) =>
        orders.Select(w => new WorkOrderSummaryDto(
            w.Id,
            w.Folio,
            w.Status,
            w.Priority,
            w.CustomerId,
            db.Customers.Where(c => c.Id == w.CustomerId).Select(c => c.Name).FirstOrDefault(),
            db.Customers.Where(c => c.Id == w.CustomerId).Select(c => c.Color).FirstOrDefault(),
            w.SiteId,
            db.Sites.Where(s => s.Id == w.SiteId).Select(s => s.Name).FirstOrDefault(),
            w.ServiceTypeId,
            db.ServiceTypes.Where(s => s.Id == w.ServiceTypeId).Select(s => s.Name).FirstOrDefault(),
            db.ServiceTypes.Where(s => s.Id == w.ServiceTypeId).Select(s => s.Color).FirstOrDefault(),
            w.Description,
            w.ScheduledStart,
            w.ScheduledEnd,
            w.UnitIds,
            w.CrewId,
            db.Crews.Where(c => c.Id == w.CrewId).Select(c => c.Name).FirstOrDefault(),
            w.CustomerReference,
            db.Tickets.Count(t => t.WorkOrderId == w.Id && t.Status != TicketStatus.Cancelled)));
}

// ---- Search & detail ------------------------------------------------------------------------------------

public sealed record SearchWorkOrdersQuery(
    string? Search,
    IReadOnlyList<WorkOrderStatus>? Statuses,
    Guid? CustomerId,
    Guid? SiteId,
    Guid? UnitId,
    Guid? CrewId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page = 1,
    int PageSize = 25) : IQuery<PagedResult<WorkOrderSummaryDto>>;

internal sealed class SearchWorkOrdersQueryHandler(IApplicationDbContext db, ITenantContext tenant) : IQueryHandler<SearchWorkOrdersQuery, PagedResult<WorkOrderSummaryDto>>
{
    public async Task<Result<PagedResult<WorkOrderSummaryDto>>> Handle(SearchWorkOrdersQuery query, CancellationToken cancellationToken)
    {
        var request = new PageRequest(query.Search, query.Page, query.PageSize);
        var orders = db.WorkOrders.AsNoTracking().Visible(db, tenant);
        if (request.Term is { } term)
        {
            orders = orders.Where(w =>
                w.Folio.ToLower().Contains(term) ||
                w.Description.ToLower().Contains(term) ||
                (w.CustomerReference != null && w.CustomerReference.ToLower().Contains(term)) ||
                db.Sites.Any(s => s.Id == w.SiteId && s.Name.ToLower().Contains(term)));
        }

        if (query.Statuses is { Count: > 0 } statuses)
        {
            orders = orders.Where(w => statuses.Contains(w.Status));
        }

        orders = orders
            .WhereIf(query.CustomerId is not null, w => w.CustomerId == query.CustomerId)
            .WhereIf(query.SiteId is not null, w => w.SiteId == query.SiteId)
            .WhereIf(query.UnitId is not null, w => w.UnitIds.Contains(query.UnitId!.Value))
            .WhereIf(query.CrewId is not null, w => w.CrewId == query.CrewId)
            .WhereIf(query.From is not null, w => w.ScheduledEnd >= query.From)
            .WhereIf(query.To is not null, w => w.ScheduledStart <= query.To);

        return await orders.OrderByDescending(w => w.ScheduledStart).ThenByDescending(w => w.FolioNumber).ToSummaries(db).ToPagedAsync(request, cancellationToken);
    }
}

public sealed record GetWorkOrderQuery(Guid Id) : IQuery<WorkOrderDetailDto>;

internal sealed class GetWorkOrderQueryHandler(IApplicationDbContext db, ITenantContext tenant, DispatchConflicts conflicts)
    : IQueryHandler<GetWorkOrderQuery, WorkOrderDetailDto>
{
    public async Task<Result<WorkOrderDetailDto>> Handle(GetWorkOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await db.WorkOrders.AsNoTracking().Visible(db, tenant).FirstOrDefaultAsync(w => w.Id == query.Id, cancellationToken);
        if (order is null)
        {
            return WorkOrderErrors.NotFound;
        }

        var summary = await db.WorkOrders.AsNoTracking().Where(w => w.Id == order.Id).ToSummaries(db).FirstAsync(cancellationToken);
        var units = await CatalogNames.LoadAsync<Unit>(db, order.UnitIds, cancellationToken);
        var employees = await CatalogNames.LoadAsync<Employee>(db, order.EmployeeIds, cancellationToken);
        var contracts = await CatalogNames.LoadAsync<Contract>(db, [order.ContractId], cancellationToken);
        var serviceIds = order.PlannedServices.Select(p => p.ServiceId).ToList();
        var services = await db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var quote = order.QuoteId is null ? null : await db.Quotes.AsNoTracking().Where(q => q.Id == order.QuoteId).Select(q => q.Folio).FirstOrDefaultAsync(cancellationToken);

        var tickets = await new SearchTicketsQueryHandler(db, tenant)
            .Handle(new SearchTicketsQuery(null, null, null, null, null, order.Id, null, null, null, null, 1, 100), cancellationToken);

        var warnings = order.IsOpen ? await conflicts.CheckAsync(order, cancellationToken) : [];

        return new WorkOrderDetailDto(
            summary,
            order.ContractId,
            contracts.NameOf(order.ContractId),
            order.Instructions,
            order.UnitIds.Where(units.ContainsKey).Select(id => new NamedRef(id, units[id].Code, units[id].Name)).ToList(),
            order.EmployeeIds.Where(employees.ContainsKey).Select(id => new NamedRef(id, employees[id].Code, employees[id].Name)).ToList(),
            order.PlannedServices.Select(p => new PlannedServiceDto(p.ServiceId, services.GetValueOrDefault(p.ServiceId)?.Name, services.GetValueOrDefault(p.ServiceId)?.UnitName, p.Quantity)).ToList(),
            order.QuoteId,
            quote,
            order.DispatchedAt,
            order.StartedAt,
            order.CompletedAt,
            order.CancellationReason,
            tickets.IsSuccess ? tickets.Value.Items : [],
            warnings,
            order.CreatedAt,
            order.CreatedBy);
    }
}

// ---- Save -----------------------------------------------------------------------------------------------

public sealed record SaveWorkOrderResult(Guid Id, string Folio, IReadOnlyList<string> Warnings);

public sealed record SaveWorkOrderCommand(Guid? Id, WorkOrderData Data, bool Dispatch) : ICommand<SaveWorkOrderResult>;

internal sealed class SaveWorkOrderCommandValidator : AbstractValidator<SaveWorkOrderCommand>
{
    public SaveWorkOrderCommandValidator()
    {
        RuleFor(c => c.Data.CustomerId).RequiredId("el cliente");
        RuleFor(c => c.Data.SiteId).RequiredId("la locación o pozo");
        RuleFor(c => c.Data.ServiceTypeId).RequiredId("el tipo de servicio");
        RuleFor(c => c.Data.Description).NotEmpty().MaximumLength(2000);
        RuleFor(c => c.Data.ScheduledEnd).GreaterThan(c => c.Data.ScheduledStart).WithMessage("El término debe ser posterior al inicio.");
    }
}

internal sealed class SaveWorkOrderCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    IFolioService folios,
    DispatchConflicts conflicts,
    WorkOrderNotifier notifier,
    ITenantClock clock,
    WorkOrderAutomation automation) : ICommandHandler<SaveWorkOrderCommand, SaveWorkOrderResult>
{
    public async Task<Result<SaveWorkOrderResult>> Handle(SaveWorkOrderCommand command, CancellationToken cancellationToken)
    {
        var references = await WorkOrderReferences.ValidateAsync(db, command.Data, cancellationToken);
        if (references.IsFailure)
        {
            return references.Error;
        }

        if (command.Id is { } id)
        {
            var order = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
            if (order is null)
            {
                return WorkOrderErrors.NotFound;
            }

            var result = order.Update(command.Data);
            if (result.IsFailure)
            {
                return result.Error;
            }

            var held = await automation.SaveAsync(order, command.Dispatch && order.Status == WorkOrderStatus.Scheduled, cancellationToken);
            if (held.IsFailure)
            {
                return held.Error;
            }

            if (command.Dispatch && order.Status == WorkOrderStatus.Scheduled && held.Value is null)
            {
                var dispatched = order.Dispatch(clock.UtcNow);
                if (dispatched.IsFailure)
                {
                    return dispatched.Error;
                }
            }

            await notifier.NotifyIfAssignedAsync(order, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new SaveWorkOrderResult(order.Id, order.Folio, WithHold(held.Value, await conflicts.CheckAsync(order, cancellationToken)));
        }

        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var number = await folios.NextAsync(FolioKeys.WorkOrder, ct);
            var created = WorkOrder.Create(FolioSequence.Format(tenant.Tenant.Settings.WorkOrderPrefix, number), number, command.Data);
            if (created.IsFailure)
            {
                return Result.Failure<SaveWorkOrderResult>(created.Error);
            }

            var order = created.Value;
            var held = await automation.SaveAsync(order, command.Dispatch, ct);
            if (held.IsFailure)
            {
                return Result.Failure<SaveWorkOrderResult>(held.Error);
            }

            if (command.Dispatch && held.Value is null)
            {
                var dispatched = order.Dispatch(clock.UtcNow);
                if (dispatched.IsFailure)
                {
                    return Result.Failure<SaveWorkOrderResult>(dispatched.Error);
                }
            }

            db.WorkOrders.Add(order);
            await notifier.NotifyIfAssignedAsync(order, ct);
            await db.SaveChangesAsync(ct);
            return Result.Success(new SaveWorkOrderResult(order.Id, order.Folio, WithHold(held.Value, await conflicts.CheckAsync(order, ct))));
        }, cancellationToken);
    }

    /// <summary>The order was saved but its dispatch waits for the approval workflow: the message goes first.</summary>
    private static IReadOnlyList<string> WithHold(string? held, IReadOnlyList<string> warnings) => held is null ? warnings : [held, .. warnings];
}

/// <summary>Business rules (moments "save" and "dispatch") and approval workflow of the dispatch of a work order.</summary>
internal sealed class WorkOrderAutomation(DocumentFacts facts, IBusinessRules rules, IWorkflowEngine workflows)
{
    /// <summary>Rules of the saved order and, when it is going to be dispatched, its gate: the message when the dispatch is held.</summary>
    public async Task<Result<string?>> SaveAsync(WorkOrder order, bool dispatch, CancellationToken cancellationToken)
    {
        var values = await facts.WorkOrderAsync(order, cancellationToken);
        var saved = await rules.EnforceAsync(new RuleSubject(AutomationDocument.WorkOrder, order.Id, order.Folio, values), AutomationTrigger.Save, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        if (!dispatch)
        {
            return Result.Success<string?>(null);
        }

        var gate = await DispatchAsync(order, values, GatePersistence.CurrentUnitOfWork, cancellationToken);
        return gate.IsFailure ? gate.Error : Result.Success(gate.Value.Passed ? null : gate.Value.Message);
    }

    public async Task<Result<WorkflowGateResult>> DispatchAsync(WorkOrder order, IReadOnlyDictionary<string, object?>? values, GatePersistence persistence, CancellationToken cancellationToken)
    {
        values ??= await facts.WorkOrderAsync(order, cancellationToken);
        var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.WorkOrder, order.Id, order.Folio, values), AutomationTrigger.Dispatch, cancellationToken);
        if (checks.IsFailure)
        {
            return checks.Error;
        }

        return await workflows.GateAsync(new WorkflowSubject(AutomationDocument.WorkOrder, order.Id, order.Folio, values), persistence, cancellationToken);
    }
}

public enum WorkOrderAction
{
    Dispatch = 0,
    Start = 1,
    Complete = 2,
    Cancel = 3
}

public sealed record ChangeWorkOrderStatusCommand(Guid Id, WorkOrderAction Action, string? Reason) : ICommand;

internal sealed class ChangeWorkOrderStatusCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    ITenantClock clock,
    WorkOrderNotifier notifier,
    IAuditLog audit,
    WorkOrderAutomation automation,
    IWorkflowEngine workflows) : ICommandHandler<ChangeWorkOrderStatusCommand>
{
    public async Task<Result> Handle(ChangeWorkOrderStatusCommand command, CancellationToken cancellationToken)
    {
        // Field staff with "own" visibility can start/complete their orders; scheduling actions need dispatch.manage.
        var order = await db.WorkOrders.Visible(db, tenant).FirstOrDefaultAsync(w => w.Id == command.Id, cancellationToken);
        if (order is null)
        {
            return WorkOrderErrors.NotFound;
        }

        if (command.Action is WorkOrderAction.Dispatch or WorkOrderAction.Cancel && !tenant.Has(Permissions.DispatchManage))
        {
            return Error.Forbidden("WorkOrders.Forbidden", "No tienes permiso para despachar o cancelar órdenes.");
        }

        // Held by an approval workflow: the request is saved on its own and the API answers 202.
        if (command.Action == WorkOrderAction.Dispatch && order.Status == WorkOrderStatus.Scheduled)
        {
            var gate = await automation.DispatchAsync(order, null, GatePersistence.Isolated, cancellationToken);
            if (gate.IsFailure)
            {
                return gate.Error;
            }

            if (!gate.Value.Passed)
            {
                return gate.Value.ToError();
            }
        }

        var now = clock.UtcNow;
        var result = command.Action switch
        {
            WorkOrderAction.Dispatch => order.Dispatch(now),
            WorkOrderAction.Start => order.Start(now),
            WorkOrderAction.Complete => order.Complete(now),
            _ => order.Cancel(command.Reason ?? string.Empty)
        };

        if (result.IsFailure)
        {
            return result;
        }

        if (command.Action == WorkOrderAction.Dispatch)
        {
            await notifier.NotifyIfAssignedAsync(order, cancellationToken);
        }

        if (command.Action == WorkOrderAction.Cancel)
        {
            await workflows.CancelPendingAsync(AutomationDocument.WorkOrder, order.Id, "La orden se canceló.", cancellationToken);
        }

        audit.Record(command.Action == WorkOrderAction.Cancel ? AuditAction.Cancelled : AuditAction.StatusChanged, nameof(WorkOrder), order.Id, $"{order.Folio} → {order.Status}", command.Reason);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class WorkOrderReferences
{
    public static async Task<Result> ValidateAsync(IApplicationDbContext db, WorkOrderData data, CancellationToken cancellationToken)
    {
        var failure = Result.FirstFailure(
            await References.EnsureAsync<Customer>(db, data.CustomerId, CustomerErrors.Catalog, cancellationToken),
            await References.EnsureAsync<Site>(db, data.SiteId, SiteErrors.Catalog, cancellationToken),
            await References.EnsureAsync<ServiceType>(db, data.ServiceTypeId, ServiceTypeErrors.Catalog, cancellationToken),
            await References.EnsureAllAsync<Unit>(db, data.UnitIds, UnitErrors.Catalog, cancellationToken),
            await References.EnsureAllAsync<Employee>(db, data.EmployeeIds, EmployeeErrors.Catalog, cancellationToken),
            await References.EnsureAsync<Crew>(db, data.CrewId, "Crews", cancellationToken),
            await References.EnsureAllAsync<Service>(db, data.PlannedServices?.Select(p => p.ServiceId), ServiceErrors.Catalog, cancellationToken));

        if (failure.IsFailure)
        {
            return failure;
        }

        if (data.ContractId is not null)
        {
            var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == data.ContractId, cancellationToken);
            if (contract is null || contract.CustomerId != data.CustomerId)
            {
                return ContractErrors.CustomerMismatch;
            }
        }

        return Result.Success();
    }
}

/// <summary>Warns the dispatcher about double bookings, units in the workshop and expired documents.</summary>
internal sealed class DispatchConflicts(IApplicationDbContext db, ITenantClock clock)
{
    public async Task<IReadOnlyList<string>> CheckAsync(WorkOrder order, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var today = clock.Today;
        var unitIds = order.UnitIds.ToList();

        var overlapping = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id != order.Id && w.Status != WorkOrderStatus.Cancelled && w.Status != WorkOrderStatus.Completed)
            .Where(w => w.ScheduledStart < order.ScheduledEnd && order.ScheduledStart < w.ScheduledEnd)
            .Select(w => new { w.Folio, w.UnitIds, w.CrewId, w.EmployeeIds })
            .ToListAsync(cancellationToken);

        var units = await db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToListAsync(cancellationToken);
        foreach (var unit in units)
        {
            foreach (var other in overlapping.Where(o => o.UnitIds.Contains(unit.Id)))
            {
                warnings.Add($"La unidad {unit.Code} también está programada en {other.Folio}.");
            }

            if (unit.Status == UnitStatus.Workshop)
            {
                warnings.Add($"La unidad {unit.Code} está en taller.");
            }
            else if (unit.Status == UnitStatus.OutOfService || !unit.IsActive)
            {
                warnings.Add($"La unidad {unit.Code} está fuera de servicio.");
            }
        }

        if (order.CrewId is { } crewId)
        {
            foreach (var other in overlapping.Where(o => o.CrewId == crewId))
            {
                warnings.Add($"La cuadrilla también está programada en {other.Folio}.");
            }
        }

        var people = order.EmployeeIds.ToList();
        if (order.CrewId is not null)
        {
            var crew = await db.Crews.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CrewId, cancellationToken);
            people.AddRange(crew?.MemberIds ?? []);
        }

        var expiredDocs = await db.ComplianceDocuments.AsNoTracking()
            .Where(d => d.ExpiresOn != null && d.ExpiresOn < today)
            .Where(d => d.Subject == DocumentSubject.Unit && unitIds.Contains(d.SubjectId) || d.Subject == DocumentSubject.Employee && people.Contains(d.SubjectId))
            .Select(d => new { d.Subject, d.SubjectId, d.DocumentTypeId })
            .ToListAsync(cancellationToken);

        if (expiredDocs.Count > 0)
        {
            var types = await CatalogNames.LoadAsync<DocumentType>(db, expiredDocs.Select(d => d.DocumentTypeId), cancellationToken);
            var employees = await CatalogNames.LoadAsync<Employee>(db, expiredDocs.Where(d => d.Subject == DocumentSubject.Employee).Select(d => d.SubjectId), cancellationToken);
            foreach (var doc in expiredDocs)
            {
                var who = doc.Subject == DocumentSubject.Unit
                    ? $"La unidad {units.FirstOrDefault(u => u.Id == doc.SubjectId)?.Code}"
                    : employees.NameOf(doc.SubjectId) ?? "Una persona asignada";
                warnings.Add($"{who} tiene vencido: {types.NameOf(doc.DocumentTypeId)}.");
            }
        }

        return warnings.Distinct().ToList();
    }
}

/// <summary>In-app notice to the people assigned to a dispatched order.</summary>
internal sealed class WorkOrderNotifier(IApplicationDbContext db, INotificationPublisher notifications, ITenantClock clock)
{
    public async Task NotifyIfAssignedAsync(WorkOrder order, CancellationToken cancellationToken)
    {
        if (order.Status != WorkOrderStatus.Dispatched)
        {
            return;
        }

        var employeeIds = order.EmployeeIds.ToList();
        if (order.CrewId is not null)
        {
            var crew = await db.Crews.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CrewId, cancellationToken);
            employeeIds.AddRange(crew?.MemberIds ?? []);
        }

        var memberships = await db.Memberships.AsNoTracking()
            .Where(m => m.EmployeeId != null && employeeIds.Contains(m.EmployeeId.Value))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
        {
            return;
        }

        var site = await db.Sites.AsNoTracking().Where(s => s.Id == order.SiteId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken);
        var start = TimeZoneInfo.ConvertTime(order.ScheduledStart, clock.TimeZone);
        await notifications.PublishAsync(new NotificationMessage(
            NotificationEvent.WorkOrderAssigned,
            NotificationSeverity.Info,
            $"Orden {order.Folio} asignada",
            $"{order.Description}\nLocación: {site}\nInicio: {start:dd/MM/yyyy HH:mm}",
            $"ordenes/{order.Id}",
            memberships,
            DeduplicationKey: $"wo-assigned:{order.Id}"), cancellationToken);
    }
}

// ---- Board ----------------------------------------------------------------------------------------------

public sealed record BoardUnitDto(Guid Id, string Code, string Name, UnitType Type, UnitStatus Status);

public sealed record DispatchBoardDto(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<BoardUnitDto> Units, IReadOnlyList<WorkOrderSummaryDto> Orders);

/// <summary>Orders of a period by unit (Gantt of the dispatcher), including the unassigned ones.</summary>
public sealed record GetDispatchBoardQuery(DateTimeOffset From, DateTimeOffset To) : IQuery<DispatchBoardDto>;

internal sealed class GetDispatchBoardQueryHandler(IApplicationDbContext db, ITenantContext tenant) : IQueryHandler<GetDispatchBoardQuery, DispatchBoardDto>
{
    public async Task<Result<DispatchBoardDto>> Handle(GetDispatchBoardQuery query, CancellationToken cancellationToken)
    {
        if (query.To <= query.From || (query.To - query.From).TotalDays > 120)
        {
            return Error.Validation("Dispatch.InvalidRange", "El periodo del tablero va de 1 a 120 días.");
        }

        var units = await db.Units.AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.Code)
            .Select(u => new BoardUnitDto(u.Id, u.Code, u.Name, u.Type, u.Status))
            .ToListAsync(cancellationToken);

        var orders = await db.WorkOrders.AsNoTracking()
            .Visible(db, tenant)
            .Where(w => w.Status != WorkOrderStatus.Cancelled)
            .Where(w => w.ScheduledStart < query.To && query.From < w.ScheduledEnd)
            .OrderBy(w => w.ScheduledStart)
            .ToSummaries(db)
            .ToListAsync(cancellationToken);

        return new DispatchBoardDto(query.From, query.To, units, orders);
    }
}

// ---- Ticket draft from an order -------------------------------------------------------------------------

public sealed record TicketDraftDto(TicketData Data, IReadOnlyList<TicketLineInput> Lines, IReadOnlyList<ResolvedPriceDto> Prices);

/// <summary>"La boleta nace de la orden": data and priced lines to start a ticket from a work order.</summary>
public sealed record GetTicketDraftFromWorkOrderQuery(Guid WorkOrderId) : IQuery<TicketDraftDto>;

internal sealed class GetTicketDraftFromWorkOrderQueryHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock)
    : IQueryHandler<GetTicketDraftFromWorkOrderQuery, TicketDraftDto>
{
    public async Task<Result<TicketDraftDto>> Handle(GetTicketDraftFromWorkOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await db.WorkOrders.AsNoTracking().Visible(db, tenant).FirstOrDefaultAsync(w => w.Id == query.WorkOrderId, cancellationToken);
        if (order is null)
        {
            return WorkOrderErrors.NotFound;
        }

        var employees = order.EmployeeIds.ToList();
        if (order.CrewId is not null)
        {
            var crew = await db.Crews.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CrewId, cancellationToken);
            employees.AddRange(crew?.MemberIds ?? []);
        }

        var type = await db.ServiceTypes.AsNoTracking().FirstAsync(t => t.Id == order.ServiceTypeId, cancellationToken);
        var planned = order.PlannedServices.Count > 0
            ? order.PlannedServices.ToList()
            : type.DefaultServiceIds.Select(id => new PlannedService(id, 1)).ToList();

        var today = clock.Today;
        var data = new TicketData(
            order.CustomerId,
            order.ContractId,
            order.SiteId,
            order.ServiceTypeId,
            order.Id,
            today,
            clock.UtcNow,
            null,
            order.UnitIds,
            order.CrewId,
            employees.Distinct().ToList(),
            order.Description,
            null,
            null,
            new Dictionary<string, string?>(),
            null);

        var prices = await new ResolvePricesQueryHandler(db, clock)
            .Handle(new ResolvePricesQuery(order.CustomerId, order.ContractId, today, planned.Select(p => p.ServiceId).ToList()), cancellationToken);

        return new TicketDraftDto(
            data,
            planned.Select(p => new TicketLineInput(p.ServiceId, p.Quantity, null, null)).ToList(),
            prices.IsSuccess ? prices.Value : []);
    }
}
