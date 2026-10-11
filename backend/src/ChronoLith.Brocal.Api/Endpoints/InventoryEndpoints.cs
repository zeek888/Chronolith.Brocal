using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Inventory;
using ChronoLith.Brocal.Domain.Inventory;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>Inventario y almacén: spare parts, consumables and tools assigned to crews.</summary>
internal sealed class InventoryEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        string[] read = [Permissions.InventoryRead, Permissions.InventoryMove, Permissions.InventoryManage];
        string[] move = [Permissions.InventoryMove, Permissions.InventoryManage];

        app.MapCatalog<Warehouse, WarehouseData, WarehouseDto>("inventory/warehouses", "Almacén", read, Permissions.InventoryManage);
        app.MapCatalog<InventoryItem, InventoryItemData, InventoryItemDto>("inventory/items", "Almacén", [.. read, Permissions.MaintenanceManage], Permissions.InventoryManage);

        var group = app.MapGroup("inventory").WithTags("Almacén");

        group.MapGet("stock", async (Guid? warehouseId, string? search, bool? onlyWithStock, IQueryHandler<GetStockQuery, IReadOnlyList<StockRowDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetStockQuery(warehouseId, search, onlyWithStock ?? true), ct)).ToOk())
            .RequirePermission([.. read, Permissions.MaintenanceManage])
            .WithSummary("Existencias por almacén con costo promedio y alerta de mínimo.");

        group.MapGet("movements", async (Guid? itemId, Guid? warehouseId, MovementType? type, DateOnly? from, DateOnly? to, int? page, int? pageSize,
                IQueryHandler<SearchMovementsQuery, PagedResult<MovementDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchMovementsQuery(itemId, warehouseId, type, from, to, page ?? 1, pageSize ?? 50), ct)).ToOk())
            .RequirePermission(read);

        group.MapPost("movements", async (MovementData data, ICommandHandler<RegisterMovementCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new RegisterMovementCommand(data), ct)).ToCreated("/api/inventory/movements"))
            .RequirePermission(move)
            .WithSummary("Entradas, salidas, traspasos y ajustes (las salidas pueden cargarse a unidad, orden o cuadrilla).");

        group.MapGet("tools", async (Guid? crewId, Guid? employeeId, bool? openOnly, IQueryHandler<SearchToolAssignmentsQuery, IReadOnlyList<ToolAssignmentDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchToolAssignmentsQuery(crewId, employeeId, openOnly), ct)).ToOk())
            .RequirePermission([.. read, Permissions.CrewsRead]);

        group.MapPost("tools", async (ToolAssignmentData data, ICommandHandler<AssignToolCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new AssignToolCommand(data), ct)).ToCreated("/api/inventory/tools"))
            .RequirePermission(move)
            .WithSummary("Asigna herramienta a una cuadrilla o persona (resguardo).");

        group.MapPost("tools/{id:guid}/return", async (Guid id, ReturnToolRequest body, ICommandHandler<ReturnToolCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new ReturnToolCommand(id, body.Condition, body.BackToStock), ct)).ToNoContent())
            .RequirePermission(move);
    }

    internal sealed record ReturnToolRequest(string? Condition, bool BackToStock);
}
