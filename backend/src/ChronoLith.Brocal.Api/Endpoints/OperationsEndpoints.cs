using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Dispatch;
using ChronoLith.Brocal.Application.Features.Tickets;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>Boletas de servicio.</summary>
internal sealed class TicketEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("tickets").WithTags("Boletas");
        string[] read = [Permissions.TicketsRead, Permissions.TicketsReadOwn, Permissions.PortalAccess];

        group.MapGet("", async ([AsParameters] TicketSearchRequest request, IQueryHandler<SearchTicketsQuery, PagedResult<TicketSummaryDto>> handler, CancellationToken ct) =>
                (await handler.Handle(request.ToQuery(), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("{id:guid}", async (Guid id, IQueryHandler<GetTicketQuery, TicketDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetTicketQuery(id), ct)).ToOk())
            .RequirePermission(read);

        group.MapPost("", async (SaveTicketRequest body, ICommandHandler<SaveTicketCommand, SaveTicketResult> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveTicketCommand(null, body.ClientId, body.Data, body.Lines, body.Meters), ct)).ToOk())
            .RequirePermission(Permissions.TicketsCapture)
            .WithSummary("Crea una boleta (idempotente por clientId para la sincronización offline).");

        group.MapPut("{id:guid}", async (Guid id, SaveTicketRequest body, ICommandHandler<SaveTicketCommand, SaveTicketResult> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveTicketCommand(id, null, body.Data, body.Lines, body.Meters), ct)).ToOk())
            .RequirePermission(Permissions.TicketsCapture);

        group.MapDelete("{id:guid}", async (Guid id, ICommandHandler<DeleteDraftTicketCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new DeleteDraftTicketCommand(id), ct)).ToNoContent())
            .RequirePermission(Permissions.TicketsCapture);

        group.MapPost("{id:guid}/photos", async (Guid id, PhotoRequest body, ICommandHandler<AddTicketPhotoCommand, TicketPhotoDto> handler, CancellationToken ct) =>
                (await handler.Handle(new AddTicketPhotoCommand(id, body.FileId, body.Caption, body.TakenAt, body.Location), ct)).ToOk())
            .RequirePermission(Permissions.TicketsCapture);

        group.MapDelete("{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, ICommandHandler<RemoveTicketPhotoCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new RemoveTicketPhotoCommand(id, photoId), ct)).ToNoContent())
            .RequirePermission(Permissions.TicketsCapture);

        group.MapPost("{id:guid}/sign", async (Guid id, SignRequest body, ICommandHandler<SignTicketCommand, TicketDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new SignTicketCommand(id, body.SignerName, body.SignerPosition, body.SignatureFileId, body.SignedAt, body.Location), ct)).ToOk())
            .RequirePermission(Permissions.TicketsSign)
            .WithSummary("Cierra la boleta con la firma del cliente y genera el PDF con la marca de la empresa.");

        group.MapPost("{id:guid}/close", async (Guid id, ICommandHandler<CloseTicketCommand, TicketDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new CloseTicketCommand(id), ct)).ToOk())
            .RequirePermission(Permissions.TicketsCapture);

        group.MapPost("approve", async (IdsRequest body, ICommandHandler<ApproveTicketsCommand, int> handler, CancellationToken ct) =>
                (await handler.Handle(new ApproveTicketsCommand(body.Ids), ct)).ToOk())
            .RequirePermission(Permissions.TicketsApprove);

        group.MapPost("{id:guid}/cancel", async (Guid id, ReasonRequest body, ICommandHandler<CancelTicketCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new CancelTicketCommand(id, body.Reason), ct)).ToNoContent())
            .RequirePermission(Permissions.TicketsCancel);

        group.MapPost("{id:guid}/reopen", async (Guid id, ReasonRequest body, ICommandHandler<ReopenTicketCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new ReopenTicketCommand(id, body.Reason), ct)).ToNoContent())
            .RequirePermission(Permissions.TicketsReopen);

        group.MapGet("{id:guid}/pdf", async (Guid id, bool? regenerate, IQueryHandler<GetTicketPdfQuery, FileContent> handler, CancellationToken ct) =>
                (await handler.Handle(new GetTicketPdfQuery(id, regenerate ?? false), ct)).ToFile())
            .RequirePermission(read);
    }

    internal sealed record TicketSearchRequest(
        string? Search,
        TicketStatus[]? Status,
        Guid? CustomerId,
        Guid? SiteId,
        Guid? ServiceTypeId,
        Guid? WorkOrderId,
        Guid? UnitId,
        DateOnly? From,
        DateOnly? To,
        bool? Uninvoiced,
        int? Page,
        int? PageSize,
        string? Sort,
        bool? Descending)
    {
        public SearchTicketsQuery ToQuery() => new(Search, Status, CustomerId, SiteId, ServiceTypeId, WorkOrderId, UnitId, From, To, Uninvoiced,
            Page ?? 1, PageSize ?? 25, Sort, Descending ?? true);
    }

    internal sealed record SaveTicketRequest(Guid? ClientId, TicketData Data, IReadOnlyList<TicketLineInput> Lines, IReadOnlyList<UnitMeterInput>? Meters);

    internal sealed record PhotoRequest(Guid FileId, string? Caption, DateTimeOffset? TakenAt, GeoPoint? Location);

    internal sealed record SignRequest(string SignerName, string? SignerPosition, Guid SignatureFileId, DateTimeOffset? SignedAt, GeoPoint? Location);
}

/// <summary>Órdenes de trabajo y programación.</summary>
internal sealed class WorkOrderEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("work-orders").WithTags("Órdenes de trabajo");
        string[] read = [Permissions.DispatchRead, Permissions.DispatchReadOwn];

        group.MapGet("", async (string? search, WorkOrderStatus[]? status, Guid? customerId, Guid? siteId, Guid? unitId, Guid? crewId,
                DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
                IQueryHandler<SearchWorkOrdersQuery, PagedResult<WorkOrderSummaryDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchWorkOrdersQuery(search, status, customerId, siteId, unitId, crewId, from, to, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("board", async (DateTimeOffset from, DateTimeOffset to, IQueryHandler<GetDispatchBoardQuery, DispatchBoardDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetDispatchBoardQuery(from, to), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("{id:guid}", async (Guid id, IQueryHandler<GetWorkOrderQuery, WorkOrderDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetWorkOrderQuery(id), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("{id:guid}/ticket-draft", async (Guid id, IQueryHandler<GetTicketDraftFromWorkOrderQuery, TicketDraftDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetTicketDraftFromWorkOrderQuery(id), ct)).ToOk())
            .RequirePermission(Permissions.TicketsCapture)
            .WithSummary("Datos y conceptos con precio para capturar la boleta que nace de la orden.");

        group.MapPost("", async (SaveWorkOrderRequest body, ICommandHandler<SaveWorkOrderCommand, SaveWorkOrderResult> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveWorkOrderCommand(null, body.Data, body.Dispatch), ct)).ToOk())
            .RequirePermission(Permissions.DispatchManage);

        group.MapPut("{id:guid}", async (Guid id, SaveWorkOrderRequest body, ICommandHandler<SaveWorkOrderCommand, SaveWorkOrderResult> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveWorkOrderCommand(id, body.Data, body.Dispatch), ct)).ToOk())
            .RequirePermission(Permissions.DispatchManage);

        group.MapPost("{id:guid}/status", async (Guid id, StatusRequest body, ICommandHandler<ChangeWorkOrderStatusCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new ChangeWorkOrderStatusCommand(id, body.Action, body.Reason), ct)).ToNoContent())
            .RequirePermission(Permissions.DispatchManage, Permissions.DispatchReadOwn, Permissions.DispatchRead);
    }

    internal sealed record SaveWorkOrderRequest(WorkOrderData Data, bool Dispatch);

    internal sealed record StatusRequest(WorkOrderAction Action, string? Reason);
}

internal sealed record IdsRequest(IReadOnlyList<Guid> Ids);

internal sealed record ReasonRequest(string Reason);
