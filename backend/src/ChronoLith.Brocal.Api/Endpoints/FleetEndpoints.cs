using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Fleet;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>Fichas de unidades y equipos: catalog, meters, status and history per unit.</summary>
internal sealed class UnitEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        string[] read = [Permissions.FleetRead, Permissions.FleetManage, Permissions.MaintenanceRead, Permissions.PermitsRead, Permissions.FuelRead, Permissions.DispatchManage];

        var units = app.MapCatalog<Unit, UnitData, UnitDto>("fleet/units", "Unidades", read, Permissions.FleetManage);

        units.MapGet("{id:guid}/overview", async (Guid id, int? days, IQueryHandler<GetUnitOverviewQuery, UnitOverviewDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetUnitOverviewQuery(id, days ?? 120), ct)).ToOk())
            .RequirePermission(read)
            .WithSummary("Historial de la unidad: lecturas, cambios de estatus, boletas, taller, combustible y documentos.");

        units.MapPost("{id:guid}/meters", async (Guid id, MetersRequest body, ICommandHandler<RecordMetersCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new RecordMetersCommand(id, body.Odometer, body.Hours, body.RecordedAt, body.IsCorrection, body.Notes), ct)).ToNoContent())
            .RequirePermission(Permissions.FleetMeters, Permissions.FleetManage)
            .WithSummary("Registra kilometraje u horómetro (las correcciones a la baja requieren administrar la flota).");

        units.MapPost("{id:guid}/status", async (Guid id, UnitStatusRequest body, ICommandHandler<ChangeUnitStatusCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new ChangeUnitStatusCommand(id, body.Status, body.Reason), ct)).ToNoContent())
            .RequirePermission(Permissions.FleetManage, Permissions.MaintenanceManage);
    }

    internal sealed record MetersRequest(decimal? Odometer, decimal? Hours, DateTimeOffset? RecordedAt, bool IsCorrection, string? Notes);

    internal sealed record UnitStatusRequest(UnitStatus Status, string? Reason);
}

/// <summary>Mantenimiento preventivo y correctivo: plans, due list and workshop orders.</summary>
internal sealed class MaintenanceEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        string[] read = [Permissions.MaintenanceRead, Permissions.MaintenanceManage];

        app.MapCatalog<MaintenancePlan, MaintenancePlanData, MaintenancePlanDto>("maintenance/plans", "Mantenimiento", read, Permissions.MaintenanceManage);

        app.MapGet("maintenance/due", async (bool? onlyDue, IQueryHandler<GetMaintenanceDueQuery, IReadOnlyList<MaintenanceDueDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetMaintenanceDueQuery(onlyDue ?? false), ct)).ToOk())
            .WithTags("Mantenimiento")
            .RequirePermission([.. read, Permissions.FleetRead])
            .WithSummary("Servicios preventivos por unidad con su avance (km, horas o días) y semáforo.");

        var orders = app.MapGroup("maintenance/orders").WithTags("Mantenimiento");

        orders.MapGet("", async (string? search, MaintenanceOrderStatus? status, Guid? unitId, MaintenanceType? type, int? page, int? pageSize,
                IQueryHandler<SearchMaintenanceOrdersQuery, PagedResult<MaintenanceOrderSummaryDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchMaintenanceOrdersQuery(search, status, unitId, type, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission(read);

        orders.MapGet("{id:guid}", async (Guid id, IQueryHandler<GetMaintenanceOrderQuery, MaintenanceOrderDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetMaintenanceOrderQuery(id), ct)).ToOk())
            .RequirePermission(read);

        orders.MapPost("", async (MaintenanceOrderData data, ICommandHandler<SaveMaintenanceOrderCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveMaintenanceOrderCommand(null, data), ct)).ToCreated("/api/maintenance/orders"))
            .RequirePermission(Permissions.MaintenanceManage);

        orders.MapPut("{id:guid}", async (Guid id, MaintenanceOrderData data, ICommandHandler<SaveMaintenanceOrderCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveMaintenanceOrderCommand(id, data), ct)).ToNoContent())
            .RequirePermission(Permissions.MaintenanceManage);

        orders.MapPost("{id:guid}/status", async (Guid id, MaintenanceStatusRequest body, ICommandHandler<ChangeMaintenanceStatusCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new ChangeMaintenanceStatusCommand(id, body.Status), ct)).ToNoContent())
            .RequirePermission(Permissions.MaintenanceManage);

        orders.MapPost("{id:guid}/close", async (Guid id, CloseMaintenanceRequest body, ICommandHandler<CloseMaintenanceOrderCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new CloseMaintenanceOrderCommand(id, body.ClosedAt, body.Odometer, body.Hours, body.WorkPerformed, body.ReleaseUnit ?? true), ct)).ToNoContent())
            .RequirePermission(Permissions.MaintenanceManage)
            .WithSummary("Cierra la orden: descarga refacciones del almacén, reinicia el programa preventivo y libera la unidad.");

        orders.MapPost("{id:guid}/cancel", async (Guid id, ReasonRequest body, ICommandHandler<CancelMaintenanceOrderCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new CancelMaintenanceOrderCommand(id, body.Reason), ct)).ToNoContent())
            .RequirePermission(Permissions.MaintenanceManage);
    }

    internal sealed record MaintenanceStatusRequest(MaintenanceOrderStatus Status);

    internal sealed record CloseMaintenanceRequest(DateTimeOffset? ClosedAt, decimal? Odometer, decimal? Hours, string WorkPerformed, bool? ReleaseUnit);
}

/// <summary>Control de combustible: loads, efficiency and anomalies per unit.</summary>
internal sealed class FuelEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("fuel").WithTags("Combustible");

        group.MapGet("loads", async (Guid? unitId, DateOnly? from, DateOnly? to, bool? onlyAnomalies, int? page, int? pageSize,
                IQueryHandler<SearchFuelLoadsQuery, PagedResult<FuelLoadDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchFuelLoadsQuery(unitId, from, to, onlyAnomalies, page ?? 1, pageSize ?? 50), ct)).ToOk())
            .RequirePermission(Permissions.FuelRead, Permissions.FuelManage, Permissions.FuelCapture);

        group.MapPost("loads", async (FuelLoadData data, ICommandHandler<SaveFuelLoadCommand, FuelLoadDto> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveFuelLoadCommand(null, data), ct)).ToOk())
            .RequirePermission(Permissions.FuelCapture, Permissions.FuelManage)
            .WithSummary("Registra una carga; calcula rendimiento (km/l o l/h) y marca anomalías contra el esperado de la unidad.");

        group.MapPut("loads/{id:guid}", async (Guid id, FuelLoadData data, ICommandHandler<SaveFuelLoadCommand, FuelLoadDto> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveFuelLoadCommand(id, data), ct)).ToOk())
            .RequirePermission(Permissions.FuelManage);

        group.MapDelete("loads/{id:guid}", async (Guid id, ICommandHandler<DeleteFuelLoadCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new DeleteFuelLoadCommand(id), ct)).ToNoContent())
            .RequirePermission(Permissions.FuelManage);

        group.MapGet("efficiency", async (DateOnly from, DateOnly to, IQueryHandler<GetFuelEfficiencyQuery, IReadOnlyList<FuelEfficiencyRow>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetFuelEfficiencyQuery(from, to), ct)).ToOk())
            .RequirePermission(Permissions.FuelRead, Permissions.FuelManage);
    }
}
