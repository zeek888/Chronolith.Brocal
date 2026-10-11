using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Hse;
using ChronoLith.Brocal.Domain.Hse;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>HSE: incidents, checklist inspections and safety talks.</summary>
internal sealed class HseEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        string[] read = [Permissions.HseRead, Permissions.HseManage, Permissions.HseReport];
        string[] report = [Permissions.HseReport, Permissions.HseManage];

        app.MapCatalog<InspectionTemplate, InspectionTemplateData, InspectionTemplateDto>("hse/templates", "HSE", read, Permissions.HseManage);

        var group = app.MapGroup("hse").WithTags("HSE");

        group.MapGet("incidents", async (string? search, IncidentStatus? status, IncidentType? type, IncidentSeverity? severity, DateOnly? from, DateOnly? to, int? page, int? pageSize,
                IQueryHandler<SearchIncidentsQuery, PagedResult<IncidentSummaryDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchIncidentsQuery(search, status, type, severity, from, to, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("incidents/{id:guid}", async (Guid id, IQueryHandler<GetIncidentQuery, IncidentDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetIncidentQuery(id), ct)).ToOk())
            .RequirePermission(read);

        group.MapPost("incidents", async (IncidentData data, ICommandHandler<SaveIncidentCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveIncidentCommand(null, data), ct)).ToCreated("/api/hse/incidents"))
            .RequirePermission(report);

        group.MapPut("incidents/{id:guid}", async (Guid id, IncidentData data, ICommandHandler<SaveIncidentCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveIncidentCommand(id, data), ct)).ToNoContent())
            .RequirePermission(report);

        group.MapPost("incidents/{id:guid}/close", async (Guid id, bool? reopen, ICommandHandler<CloseIncidentCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new CloseIncidentCommand(id, reopen ?? false), ct)).ToNoContent())
            .RequirePermission(Permissions.HseManage);

        group.MapGet("inspections", async (InspectionCategory? category, Guid? unitId, bool? onlyFailed, DateOnly? from, DateOnly? to, int? page, int? pageSize,
                IQueryHandler<SearchInspectionsQuery, PagedResult<InspectionSummaryDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchInspectionsQuery(category, unitId, onlyFailed, from, to, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("inspections/{id:guid}", async (Guid id, IQueryHandler<GetInspectionQuery, InspectionDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetInspectionQuery(id), ct)).ToOk())
            .RequirePermission(read);

        group.MapPost("inspections", async (RecordInspectionCommand command, ICommandHandler<RecordInspectionCommand, InspectionSummaryDto> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToOk())
            .RequirePermission(report)
            .WithSummary("Registra una inspección por checklist; los puntos críticos fallidos abren un hallazgo.");

        group.MapGet("talks", async (DateOnly? from, DateOnly? to, int? page, int? pageSize, IQueryHandler<SearchSafetyTalksQuery, PagedResult<SafetyTalkDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchSafetyTalksQuery(from, to, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission(read);

        group.MapPost("talks", async (SafetyTalkData data, ICommandHandler<SaveSafetyTalkCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveSafetyTalkCommand(null, data), ct)).ToCreated("/api/hse/talks"))
            .RequirePermission(report);

        group.MapPut("talks/{id:guid}", async (Guid id, SafetyTalkData data, ICommandHandler<SaveSafetyTalkCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveSafetyTalkCommand(id, data), ct)).ToNoContent())
            .RequirePermission(report);

        group.MapGet("stats", async (DateOnly from, DateOnly to, IQueryHandler<GetHseStatsQuery, HseStatsDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetHseStatsQuery(from, to), ct)).ToOk())
            .RequirePermission(Permissions.HseRead, Permissions.HseManage, Permissions.DashboardRead)
            .WithSummary("Indicadores HSE: incidentes por tipo y severidad, días perdidos, inspecciones y pláticas.");
    }
}
