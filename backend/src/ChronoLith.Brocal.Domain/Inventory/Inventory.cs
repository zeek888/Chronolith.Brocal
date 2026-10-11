using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;

namespace ChronoLith.Brocal.Domain.Inventory;

/// <summary>Almacén (central, base de operaciones, taller).</summary>
public sealed class Warehouse : CatalogAggregate
{
    private Warehouse()
    {
    }

    public string? Location { get; private set; }

    public string? Responsible { get; private set; }

    public static Result<Warehouse> Create(string code, string name, string? location, string? responsible, string? notes)
    {
        var warehouse = new Warehouse();
        var result = warehouse.Update(code, name, location, responsible, notes);
        return result.IsFailure ? result.Error : warehouse;
    }

    public Result Update(string code, string name, string? location, string? responsible, string? notes)
    {
        var identity = SetIdentity(code, name, notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        Responsible = string.IsNullOrWhiteSpace(responsible) ? null : responsible.Trim();
        return Result.Success();
    }
}

public enum ItemCategory
{
    SparePart = 0,
    Consumable = 1,
    Tool = 2,
    Ppe = 3,
    Chemical = 4,
    Other = 5
}

public sealed record InventoryItemData(
    string Code,
    string Name,
    ItemCategory Category,
    string UnitName,
    string? Brand,
    string? PartNumber,
    decimal MinStock,
    decimal? MaxStock,
    decimal? StandardCost,
    string? Notes);

/// <summary>Artículo de almacén: refacción, consumible, herramienta, EPP o químico.</summary>
public sealed class InventoryItem : CatalogAggregate
{
    private InventoryItem()
    {
    }

    public ItemCategory Category { get; private set; }

    public string UnitName { get; private set; } = null!;

    public string? Brand { get; private set; }

    public string? PartNumber { get; private set; }

    /// <summary>Reorder point across warehouses.</summary>
    public decimal MinStock { get; private set; }

    public decimal? MaxStock { get; private set; }

    public decimal? StandardCost { get; private set; }

    /// <summary>Weighted average cost of the stock on hand.</summary>
    public decimal AverageCost { get; private set; }

    public bool IsTool => Category == ItemCategory.Tool;

    public static Result<InventoryItem> Create(InventoryItemData data)
    {
        var item = new InventoryItem();
        var result = item.Update(data);
        if (result.IsFailure)
        {
            return result.Error;
        }

        item.AverageCost = data.StandardCost ?? 0;
        return item;
    }

    public Result Update(InventoryItemData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if (string.IsNullOrWhiteSpace(data.UnitName) || data.UnitName.Trim().Length > 30)
        {
            return InventoryErrors.InvalidUnit;
        }

        if (data.MinStock < 0 || data.MaxStock is < 0 || data.StandardCost is < 0 || data.MaxStock is not null && data.MaxStock < data.MinStock)
        {
            return InventoryErrors.InvalidLevels;
        }

        Category = data.Category;
        UnitName = data.UnitName.Trim();
        Brand = string.IsNullOrWhiteSpace(data.Brand) ? null : data.Brand.Trim();
        PartNumber = string.IsNullOrWhiteSpace(data.PartNumber) ? null : data.PartNumber.Trim();
        MinStock = data.MinStock;
        MaxStock = data.MaxStock;
        StandardCost = data.StandardCost;
        return Result.Success();
    }

    /// <summary>Weighted average after receiving stock.</summary>
    public void Receive(decimal quantityOnHandBefore, decimal quantity, decimal unitCost)
    {
        var total = quantityOnHandBefore + quantity;
        AverageCost = total <= 0
            ? unitCost
            : Math.Round((quantityOnHandBefore * AverageCost + quantity * unitCost) / total, 4);
    }
}

/// <summary>Existencia de un artículo en un almacén.</summary>
public sealed class StockLevel : Entity, ITenantOwned
{
    private StockLevel()
    {
    }

    public StockLevel(Guid itemId, Guid warehouseId)
    {
        ItemId = itemId;
        WarehouseId = warehouseId;
    }

    public Guid TenantId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>Concurrency token: two simultaneous withdrawals cannot both pass the stock check.</summary>
    public uint RowVersion { get; private set; }

    public Result Add(decimal quantity)
    {
        if (quantity <= 0)
        {
            return InventoryErrors.InvalidQuantity;
        }

        Quantity += quantity;
        return Result.Success();
    }

    public Result Remove(decimal quantity, bool allowNegative = false)
    {
        if (quantity <= 0)
        {
            return InventoryErrors.InvalidQuantity;
        }

        if (!allowNegative && Quantity < quantity)
        {
            return InventoryErrors.InsufficientStock(Quantity);
        }

        Quantity -= quantity;
        return Result.Success();
    }

    public void SetCount(decimal quantity) => Quantity = quantity;
}

public enum MovementType
{
    /// <summary>Entrada por compra o recepción.</summary>
    Receipt = 0,

    /// <summary>Salida (consumo general).</summary>
    Issue = 1,

    /// <summary>Traspaso entre almacenes.</summary>
    Transfer = 2,

    /// <summary>Ajuste por conteo físico (la cantidad es la existencia contada).</summary>
    Adjustment = 3,

    /// <summary>Consumo en una orden de taller.</summary>
    Maintenance = 4,

    /// <summary>Salida a una cuadrilla o persona (herramienta o EPP).</summary>
    CrewIssue = 5,

    /// <summary>Devolución al almacén.</summary>
    Return = 6
}

public sealed record MovementData(
    MovementType Type,
    Guid ItemId,
    Guid WarehouseId,
    Guid? TargetWarehouseId,
    decimal Quantity,
    decimal? UnitCost,
    DateTimeOffset MovedAt,
    string? Reference,
    Guid? MaintenanceOrderId,
    Guid? CrewId,
    Guid? EmployeeId,
    Guid? UnitId,
    string? Notes);

/// <summary>Movimiento de almacén. It is immutable: corrections are made with another movement.</summary>
public sealed class InventoryMovement : TenantAggregateRoot
{
    private InventoryMovement()
    {
    }

    public MovementType Type { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid? TargetWarehouseId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitCost { get; private set; }

    public decimal TotalCost { get; private set; }

    /// <summary>For adjustments: difference applied (counted − previous).</summary>
    public decimal? Difference { get; private set; }

    public DateTimeOffset MovedAt { get; private set; }

    public string? Reference { get; private set; }

    public Guid? MaintenanceOrderId { get; private set; }

    public Guid? CrewId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public Guid? UnitId { get; private set; }

    public string? Notes { get; private set; }

    public static Result<InventoryMovement> Create(MovementData data, decimal unitCost, decimal? difference = null)
    {
        if (data.Type != MovementType.Adjustment && data.Quantity <= 0 || data.Type == MovementType.Adjustment && data.Quantity < 0)
        {
            return InventoryErrors.InvalidQuantity;
        }

        if (data.Type == MovementType.Transfer && (data.TargetWarehouseId is null || data.TargetWarehouseId == data.WarehouseId))
        {
            return InventoryErrors.InvalidTransfer;
        }

        if (data.Type == MovementType.CrewIssue && data.CrewId is null && data.EmployeeId is null)
        {
            return InventoryErrors.RecipientRequired;
        }

        var quantity = data.Type == MovementType.Adjustment ? Math.Abs(difference ?? 0) : data.Quantity;
        return new InventoryMovement
        {
            Type = data.Type,
            ItemId = data.ItemId,
            WarehouseId = data.WarehouseId,
            TargetWarehouseId = data.Type == MovementType.Transfer ? data.TargetWarehouseId : null,
            Quantity = data.Type == MovementType.Adjustment ? data.Quantity : quantity,
            UnitCost = unitCost,
            TotalCost = Money.Round(quantity * unitCost),
            Difference = difference,
            MovedAt = data.MovedAt,
            Reference = string.IsNullOrWhiteSpace(data.Reference) ? null : data.Reference.Trim(),
            MaintenanceOrderId = data.MaintenanceOrderId,
            CrewId = data.CrewId,
            EmployeeId = data.EmployeeId,
            UnitId = data.UnitId,
            Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim()
        };
    }

    /// <summary>Whether the movement adds stock to its warehouse (receipts and returns).</summary>
    public static bool Adds(MovementType type) => type is MovementType.Receipt or MovementType.Return;
}

public sealed record ToolAssignmentData(
    Guid ItemId,
    Guid WarehouseId,
    decimal Quantity,
    string? SerialNumber,
    Guid? CrewId,
    Guid? EmployeeId,
    DateTimeOffset AssignedAt,
    DateOnly? ExpectedReturnOn,
    string? Notes);

/// <summary>Herramienta asignada a una cuadrilla o persona (resguardo) until it is returned.</summary>
public sealed class ToolAssignment : TenantAggregateRoot
{
    private ToolAssignment()
    {
    }

    public Guid ItemId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public decimal Quantity { get; private set; }

    public string? SerialNumber { get; private set; }

    public Guid? CrewId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public DateOnly? ExpectedReturnOn { get; private set; }

    public DateTimeOffset? ReturnedAt { get; private set; }

    public string? ReturnCondition { get; private set; }

    public string? Notes { get; private set; }

    public bool IsOpen => ReturnedAt is null;

    public static Result<ToolAssignment> Create(ToolAssignmentData data)
    {
        if (data.Quantity <= 0)
        {
            return InventoryErrors.InvalidQuantity;
        }

        if (data.CrewId is null && data.EmployeeId is null)
        {
            return InventoryErrors.RecipientRequired;
        }

        return new ToolAssignment
        {
            ItemId = data.ItemId,
            WarehouseId = data.WarehouseId,
            Quantity = data.Quantity,
            SerialNumber = string.IsNullOrWhiteSpace(data.SerialNumber) ? null : data.SerialNumber.Trim(),
            CrewId = data.CrewId,
            EmployeeId = data.EmployeeId,
            AssignedAt = data.AssignedAt,
            ExpectedReturnOn = data.ExpectedReturnOn,
            Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim()
        };
    }

    public Result Return(DateTimeOffset at, string? condition)
    {
        if (!IsOpen)
        {
            return InventoryErrors.AlreadyReturned;
        }

        ReturnedAt = at;
        ReturnCondition = string.IsNullOrWhiteSpace(condition) ? null : condition.Trim();
        return Result.Success();
    }
}

public static class InventoryErrors
{
    public static readonly Error InvalidUnit = Error.Validation("Inventory.InvalidUnit", "La unidad de medida es obligatoria (máximo 30 caracteres).");

    public static readonly Error InvalidLevels = Error.Validation("Inventory.InvalidLevels", "Mínimo, máximo y costo no pueden ser negativos y el máximo no puede ser menor al mínimo.");

    public static readonly Error InvalidQuantity = Error.Validation("Inventory.InvalidQuantity", "La cantidad debe ser mayor a cero.");

    public static readonly Error InvalidTransfer = Error.Validation("Inventory.InvalidTransfer", "Indica un almacén destino distinto al de origen.");

    public static readonly Error RecipientRequired = Error.Validation("Inventory.RecipientRequired", "Indica la cuadrilla o la persona que recibe.");

    public static readonly Error AlreadyReturned = Error.Conflict("Inventory.AlreadyReturned", "La herramienta ya fue devuelta.");

    public static readonly Error AssignmentNotFound = Error.NotFound("Inventory.AssignmentNotFound", "La asignación no existe.");

    public static Error InsufficientStock(decimal available) =>
        Error.Conflict("Inventory.InsufficientStock", $"Existencia insuficiente (disponible: {available:0.##}).");
}
