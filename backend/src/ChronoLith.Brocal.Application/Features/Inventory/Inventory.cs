using System.Linq.Expressions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Automation;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Inventory;
using ChronoLith.Brocal.Domain.People;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Inventory;

// ---- Warehouses and items -------------------------------------------------------------------------------

public sealed record WarehouseData(string Code, string Name, string? Location, string? Responsible, string? Notes);

public sealed record WarehouseDto(Guid Id, string Code, string Name, string? Location, string? Responsible, string? Notes, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

internal sealed class WarehouseDataValidator : AbstractValidator<WarehouseData>
{
    public WarehouseDataValidator()
    {
        RuleFor(w => w.Code).BusinessCode();
        RuleFor(w => w.Name).RequiredName();
    }
}

internal sealed class WarehouseSpec(IApplicationDbContext db) : ICatalogSpec<Warehouse, WarehouseData, WarehouseDto>
{
    public string Name => "Warehouses";

    public Expression<Func<Warehouse, WarehouseDto>> Projection => w => new WarehouseDto(w.Id, w.Code, w.Name, w.Location, w.Responsible, w.Notes, w.IsActive, w.CreatedAt, w.UpdatedAt);

    public string CodeOf(WarehouseData data) => data.Code;

    public Result<Warehouse> Create(WarehouseData data) => Warehouse.Create(data.Code, data.Name, data.Location, data.Responsible, data.Notes);

    public Result Update(Warehouse entity, WarehouseData data) => entity.Update(data.Code, data.Name, data.Location, data.Responsible, data.Notes);

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.InventoryMovements.AnyAsync(m => m.WarehouseId == id || m.TargetWarehouseId == id, cancellationToken)
        || await db.StockLevels.AnyAsync(s => s.WarehouseId == id && s.Quantity != 0, cancellationToken);
}

public sealed record InventoryItemDto(
    Guid Id, string Code, string Name, ItemCategory Category, string UnitName, string? Brand, string? PartNumber, decimal MinStock, decimal? MaxStock,
    decimal? StandardCost, decimal AverageCost, decimal OnHand, bool BelowMinimum, string? Notes, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

internal sealed class InventoryItemDataValidator : AbstractValidator<InventoryItemData>
{
    public InventoryItemDataValidator()
    {
        RuleFor(i => i.Code).BusinessCode();
        RuleFor(i => i.Name).RequiredName();
        RuleFor(i => i.UnitName).NotEmpty().MaximumLength(30);
        RuleFor(i => i.MinStock).GreaterThanOrEqualTo(0);
    }
}

internal sealed class InventoryItemSpec(IApplicationDbContext db) : ICatalogSpec<InventoryItem, InventoryItemData, InventoryItemDto>
{
    public string Name => "InventoryItems";

    public Expression<Func<InventoryItem, InventoryItemDto>> Projection => i => new InventoryItemDto(
        i.Id, i.Code, i.Name, i.Category, i.UnitName, i.Brand, i.PartNumber, i.MinStock, i.MaxStock, i.StandardCost, i.AverageCost,
        db.StockLevels.Where(s => s.ItemId == i.Id).Sum(s => (decimal?)s.Quantity) ?? 0,
        (db.StockLevels.Where(s => s.ItemId == i.Id).Sum(s => (decimal?)s.Quantity) ?? 0) < i.MinStock,
        i.Notes, i.IsActive, i.CreatedAt, i.UpdatedAt);

    public Expression<Func<InventoryItem, string?>>? LookupExtra => i => i.UnitName;

    public string CodeOf(InventoryItemData data) => data.Code;

    public Result<InventoryItem> Create(InventoryItemData data) => InventoryItem.Create(data);

    public Result Update(InventoryItem entity, InventoryItemData data) => entity.Update(data);

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.InventoryMovements.AnyAsync(m => m.ItemId == id, cancellationToken) || await db.ToolAssignments.AnyAsync(t => t.ItemId == id, cancellationToken);

    public IQueryable<InventoryItem> Filter(IQueryable<InventoryItem> query, IReadOnlyDictionary<string, string?> filters)
    {
        if (filters.TryGetValue("category", out var category) && Enum.TryParse<ItemCategory>(category, true, out var value))
        {
            query = query.Where(i => i.Category == value);
        }

        if (filters.TryGetValue("belowMinimum", out var below) && below == "true")
        {
            query = query.Where(i => (db.StockLevels.Where(s => s.ItemId == i.Id).Sum(s => (decimal?)s.Quantity) ?? 0) < i.MinStock);
        }

        return query;
    }
}

// ---- Stock and movements --------------------------------------------------------------------------------

public sealed record StockRowDto(Guid ItemId, string ItemCode, string ItemName, ItemCategory Category, string UnitName, Guid WarehouseId, string WarehouseName, decimal Quantity, decimal AverageCost, decimal Value, decimal MinStock);

public sealed record GetStockQuery(Guid? WarehouseId, string? Search, bool OnlyWithStock = true) : IQuery<IReadOnlyList<StockRowDto>>;

internal sealed class GetStockQueryHandler(IApplicationDbContext db) : IQueryHandler<GetStockQuery, IReadOnlyList<StockRowDto>>
{
    public async Task<Result<IReadOnlyList<StockRowDto>>> Handle(GetStockQuery query, CancellationToken cancellationToken)
    {
        var rows = from s in db.StockLevels.AsNoTracking()
                   join i in db.InventoryItems.AsNoTracking() on s.ItemId equals i.Id
                   join w in db.Warehouses.AsNoTracking() on s.WarehouseId equals w.Id
                   select new { s, i, w };

        if (query.WarehouseId is not null)
        {
            rows = rows.Where(r => r.w.Id == query.WarehouseId);
        }

        if (query.OnlyWithStock)
        {
            rows = rows.Where(r => r.s.Quantity != 0);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            rows = rows.Where(r => r.i.Code.ToLower().Contains(term) || r.i.Name.ToLower().Contains(term));
        }

        var list = await rows.OrderBy(r => r.i.Name).ThenBy(r => r.w.Name)
            .Select(r => new StockRowDto(r.i.Id, r.i.Code, r.i.Name, r.i.Category, r.i.UnitName, r.w.Id, r.w.Name, r.s.Quantity, r.i.AverageCost, r.s.Quantity * r.i.AverageCost, r.i.MinStock))
            .ToListAsync(cancellationToken);
        return list;
    }
}

public sealed record MovementDto(
    Guid Id, MovementType Type, Guid ItemId, string? ItemCode, string? ItemName, Guid WarehouseId, string? WarehouseName, Guid? TargetWarehouseId, string? TargetWarehouseName,
    decimal Quantity, decimal UnitCost, decimal TotalCost, decimal? Difference, DateTimeOffset MovedAt, string? Reference, Guid? CrewId, string? CrewName,
    Guid? EmployeeId, string? EmployeeName, Guid? UnitId, Guid? MaintenanceOrderId, string? Notes, string? CreatedBy);

public sealed record SearchMovementsQuery(Guid? ItemId, Guid? WarehouseId, MovementType? Type, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 50) : IQuery<PagedResult<MovementDto>>;

internal sealed class SearchMovementsQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<SearchMovementsQuery, PagedResult<MovementDto>>
{
    public async Task<Result<PagedResult<MovementDto>>> Handle(SearchMovementsQuery query, CancellationToken cancellationToken)
    {
        var from = query.From is null ? (DateTimeOffset?)null : clock.StartOfDay(query.From.Value);
        var to = query.To is null ? (DateTimeOffset?)null : clock.StartOfDay(query.To.Value.AddDays(1));
        return await db.InventoryMovements.AsNoTracking()
            .WhereIf(query.ItemId is not null, m => m.ItemId == query.ItemId)
            .WhereIf(query.WarehouseId is not null, m => m.WarehouseId == query.WarehouseId || m.TargetWarehouseId == query.WarehouseId)
            .WhereIf(query.Type is not null, m => m.Type == query.Type)
            .WhereIf(from is not null, m => m.MovedAt >= from)
            .WhereIf(to is not null, m => m.MovedAt < to)
            .OrderByDescending(m => m.MovedAt)
            .Select(m => new MovementDto(m.Id, m.Type, m.ItemId,
                db.InventoryItems.Where(i => i.Id == m.ItemId).Select(i => i.Code).FirstOrDefault(),
                db.InventoryItems.Where(i => i.Id == m.ItemId).Select(i => i.Name).FirstOrDefault(),
                m.WarehouseId, db.Warehouses.Where(w => w.Id == m.WarehouseId).Select(w => w.Name).FirstOrDefault(),
                m.TargetWarehouseId, db.Warehouses.Where(w => w.Id == m.TargetWarehouseId).Select(w => w.Name).FirstOrDefault(),
                m.Quantity, m.UnitCost, m.TotalCost, m.Difference, m.MovedAt, m.Reference, m.CrewId,
                db.Crews.Where(c => c.Id == m.CrewId).Select(c => c.Name).FirstOrDefault(), m.EmployeeId,
                db.Employees.Where(e => e.Id == m.EmployeeId).Select(e => e.Name).FirstOrDefault(), m.UnitId, m.MaintenanceOrderId, m.Notes, m.CreatedBy))
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);
    }
}

/// <summary>
/// Records a stock movement (receipt, issue, transfer, physical count adjustment, issue to a crew or return) and
/// updates the stock of the warehouses and the average cost of the item.
/// </summary>
public sealed record RegisterMovementCommand(MovementData Data) : ICommand<Guid>;

internal sealed class RegisterMovementCommandValidator : AbstractValidator<RegisterMovementCommand>
{
    public RegisterMovementCommandValidator()
    {
        RuleFor(c => c.Data.ItemId).RequiredId("el artículo");
        RuleFor(c => c.Data.WarehouseId).RequiredId("el almacén");
        RuleFor(c => c.Data.Quantity).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Data.UnitCost).GreaterThanOrEqualTo(0).When(c => c.Data.UnitCost is not null);
    }
}

internal sealed class RegisterMovementCommandHandler(IApplicationDbContext db, IBusinessRules rules) : ICommandHandler<RegisterMovementCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterMovementCommand command, CancellationToken cancellationToken)
    {
        var data = command.Data;
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == data.ItemId, cancellationToken);
        if (item is null)
        {
            return CatalogErrors.ReferenceNotFound("InventoryItems");
        }

        var references = Result.FirstFailure(
            await References.EnsureAsync<Warehouse>(db, data.WarehouseId, "Warehouses", cancellationToken, requireActive: true),
            await References.EnsureAsync<Warehouse>(db, data.TargetWarehouseId, "Warehouses", cancellationToken, requireActive: true),
            await References.EnsureAsync<Crew>(db, data.CrewId, "Crews", cancellationToken),
            await References.EnsureAsync<Employee>(db, data.EmployeeId, EmployeeErrors.Catalog, cancellationToken));
        if (references.IsFailure)
        {
            return references.Error;
        }

        var stock = await StockAsync(item.Id, data.WarehouseId, cancellationToken);
        var unitCost = data.UnitCost ?? item.AverageCost;
        decimal? difference = null;

        switch (data.Type)
        {
            case MovementType.Receipt or MovementType.Return:
                item.Receive(await OnHandAsync(item.Id, cancellationToken), data.Quantity, unitCost);
                var added = stock.Add(data.Quantity);
                if (added.IsFailure)
                {
                    return added.Error;
                }

                break;

            case MovementType.Transfer:
                var moved = stock.Remove(data.Quantity);
                if (moved.IsFailure)
                {
                    return moved.Error;
                }

                (await StockAsync(item.Id, data.TargetWarehouseId!.Value, cancellationToken)).Add(data.Quantity);
                unitCost = item.AverageCost;
                break;

            case MovementType.Adjustment:
                difference = data.Quantity - stock.Quantity;
                stock.SetCount(data.Quantity);
                unitCost = item.AverageCost;
                break;

            default:
                var removed = stock.Remove(data.Quantity);
                if (removed.IsFailure)
                {
                    return removed.Error;
                }

                unitCost = item.AverageCost;
                break;
        }

        var movement = InventoryMovement.Create(data, unitCost, difference);
        if (movement.IsFailure)
        {
            return movement.Error;
        }

        // Stock after the movement: the warehouse in memory, the whole item from the saved stock plus the change.
        var change = data.Type switch
        {
            MovementType.Receipt or MovementType.Return => data.Quantity,
            MovementType.Transfer => 0,
            MovementType.Adjustment => difference ?? 0,
            _ => -data.Quantity
        };
        var onHandAfter = await OnHandAsync(item.Id, cancellationToken) + change;
        var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.InventoryMovement, movement.Value.Id, $"{item.Code} {item.Name}",
            DocumentFacts.Movement(movement.Value, stock.Quantity, onHandAfter, item.MinStock)), AutomationTrigger.Save, cancellationToken);
        if (checks.IsFailure)
        {
            return checks.Error;
        }

        db.InventoryMovements.Add(movement.Value);
        await db.SaveChangesAsync(cancellationToken);
        return movement.Value.Id;
    }

    private async Task<StockLevel> StockAsync(Guid itemId, Guid warehouseId, CancellationToken cancellationToken)
    {
        var stock = await db.StockLevels.FirstOrDefaultAsync(s => s.ItemId == itemId && s.WarehouseId == warehouseId, cancellationToken)
                    ?? db.StockLevels.Local.FirstOrDefault(s => s.ItemId == itemId && s.WarehouseId == warehouseId);
        if (stock is null)
        {
            stock = new StockLevel(itemId, warehouseId);
            db.StockLevels.Add(stock);
        }

        return stock;
    }

    private async Task<decimal> OnHandAsync(Guid itemId, CancellationToken cancellationToken) =>
        await db.StockLevels.Where(s => s.ItemId == itemId).SumAsync(s => (decimal?)s.Quantity, cancellationToken) ?? 0;
}

// ---- Tools assigned to crews --------------------------------------------------------------------------

public sealed record ToolAssignmentDto(
    Guid Id, Guid ItemId, string? ItemCode, string? ItemName, Guid WarehouseId, decimal Quantity, string? SerialNumber, Guid? CrewId, string? CrewName,
    Guid? EmployeeId, string? EmployeeName, DateTimeOffset AssignedAt, DateOnly? ExpectedReturnOn, DateTimeOffset? ReturnedAt, string? ReturnCondition, string? Notes, bool IsOverdue);

public sealed record SearchToolAssignmentsQuery(Guid? CrewId, Guid? EmployeeId, bool? OpenOnly) : IQuery<IReadOnlyList<ToolAssignmentDto>>;

internal sealed class SearchToolAssignmentsQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<SearchToolAssignmentsQuery, IReadOnlyList<ToolAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<ToolAssignmentDto>>> Handle(SearchToolAssignmentsQuery query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        return await db.ToolAssignments.AsNoTracking()
            .WhereIf(query.CrewId is not null, t => t.CrewId == query.CrewId)
            .WhereIf(query.EmployeeId is not null, t => t.EmployeeId == query.EmployeeId)
            .WhereIf(query.OpenOnly == true, t => t.ReturnedAt == null)
            .OrderByDescending(t => t.AssignedAt)
            .Select(t => new ToolAssignmentDto(t.Id, t.ItemId,
                db.InventoryItems.Where(i => i.Id == t.ItemId).Select(i => i.Code).FirstOrDefault(),
                db.InventoryItems.Where(i => i.Id == t.ItemId).Select(i => i.Name).FirstOrDefault(),
                t.WarehouseId, t.Quantity, t.SerialNumber, t.CrewId, db.Crews.Where(c => c.Id == t.CrewId).Select(c => c.Name).FirstOrDefault(),
                t.EmployeeId, db.Employees.Where(e => e.Id == t.EmployeeId).Select(e => e.Name).FirstOrDefault(),
                t.AssignedAt, t.ExpectedReturnOn, t.ReturnedAt, t.ReturnCondition, t.Notes, t.ReturnedAt == null && t.ExpectedReturnOn != null && t.ExpectedReturnOn < today))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Resguardo: the tool leaves the warehouse (crew issue movement) and stays assigned until it is returned.</summary>
public sealed record AssignToolCommand(ToolAssignmentData Data) : ICommand<Guid>;

internal sealed class AssignToolCommandHandler(IApplicationDbContext db, RegisterMovementCommandHandler movements) : ICommandHandler<AssignToolCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AssignToolCommand command, CancellationToken cancellationToken)
    {
        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var created = ToolAssignment.Create(command.Data);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error);
            }

            var movement = await movements.Handle(new RegisterMovementCommand(new MovementData(MovementType.CrewIssue, command.Data.ItemId, command.Data.WarehouseId, null,
                command.Data.Quantity, null, command.Data.AssignedAt, $"Resguardo {command.Data.SerialNumber}".Trim(), null, command.Data.CrewId, command.Data.EmployeeId, null, command.Data.Notes)), ct);
            if (movement.IsFailure)
            {
                return Result.Failure<Guid>(movement.Error);
            }

            db.ToolAssignments.Add(created.Value);
            await db.SaveChangesAsync(ct);
            return Result.Success(created.Value.Id);
        }, cancellationToken);
    }
}

public sealed record ReturnToolCommand(Guid Id, string? Condition, bool BackToStock) : ICommand;

internal sealed class ReturnToolCommandHandler(IApplicationDbContext db, ITenantClock clock, RegisterMovementCommandHandler movements) : ICommandHandler<ReturnToolCommand>
{
    public async Task<Result> Handle(ReturnToolCommand command, CancellationToken cancellationToken)
    {
        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var assignment = await db.ToolAssignments.FirstOrDefaultAsync(t => t.Id == command.Id, ct);
            if (assignment is null)
            {
                return InventoryErrors.AssignmentNotFound;
            }

            var returned = assignment.Return(clock.UtcNow, command.Condition);
            if (returned.IsFailure)
            {
                return returned;
            }

            if (command.BackToStock)
            {
                var movement = await movements.Handle(new RegisterMovementCommand(new MovementData(MovementType.Return, assignment.ItemId, assignment.WarehouseId, null,
                    assignment.Quantity, null, clock.UtcNow, "Devolución de resguardo", null, assignment.CrewId, assignment.EmployeeId, null, command.Condition)), ct);
                if (movement.IsFailure)
                {
                    return Result.Failure(movement.Error);
                }
            }

            await db.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}
