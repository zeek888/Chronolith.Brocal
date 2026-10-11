using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Invoicing;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>Facturación CFDI 4.0: drafts from signed tickets, stamping through the PAC, cancellation and files.</summary>
internal sealed class InvoiceEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("invoices").WithTags("Facturación");
        string[] read = [Permissions.InvoicingRead, Permissions.InvoicingIssue, Permissions.CollectionsRead, Permissions.BillingRead, Permissions.PortalAccess];

        group.MapGet("", async (string? search, InvoiceStatus? status, Guid? customerId, DateOnly? from, DateOnly? to, bool? onlyWithBalance, bool? onlyOverdue, int? page, int? pageSize,
                IQueryHandler<SearchInvoicesQuery, PagedResult<InvoiceSummaryDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchInvoicesQuery(search, status, customerId, from, to, onlyWithBalance, onlyOverdue, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("invoiceable-tickets", async (Guid? customerId, IQueryHandler<GetInvoiceableTicketsQuery, IReadOnlyList<InvoiceableTicketDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetInvoiceableTicketsQuery(customerId), ct)).ToOk())
            .RequirePermission(Permissions.InvoicingIssue, Permissions.InvoicingRead, Permissions.BillingRead)
            .WithSummary("Boletas firmadas o validadas pendientes de facturar.");

        group.MapGet("{id:guid}", async (Guid id, IQueryHandler<GetInvoiceQuery, InvoiceDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetInvoiceQuery(id), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("{id:guid}/pdf", async (Guid id, IQueryHandler<GetInvoiceFileQuery, FileContent> handler, CancellationToken ct) =>
                (await handler.Handle(new GetInvoiceFileQuery(id, false), ct)).ToFile())
            .RequirePermission(read);

        group.MapGet("{id:guid}/xml", async (Guid id, IQueryHandler<GetInvoiceFileQuery, FileContent> handler, CancellationToken ct) =>
                (await handler.Handle(new GetInvoiceFileQuery(id, true), ct)).ToFile())
            .RequirePermission(read);

        group.MapPost("from-tickets", async (CreateInvoiceFromTicketsCommand command, ICommandHandler<CreateInvoiceFromTicketsCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToCreated("/api/invoices"))
            .RequirePermission(Permissions.InvoicingIssue)
            .WithSummary("La boleta firmada se convierte en factura en un clic (con stamp = true se timbra de inmediato).");

        group.MapPost("", async (InvoiceDraftRequest body, ICommandHandler<SaveInvoiceDraftCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(body.ToCommand(null), ct)).ToCreated("/api/invoices"))
            .RequirePermission(Permissions.InvoicingIssue);

        group.MapPut("{id:guid}", async (Guid id, InvoiceDraftRequest body, ICommandHandler<SaveInvoiceDraftCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(body.ToCommand(id), ct)).ToNoContent())
            .RequirePermission(Permissions.InvoicingIssue);

        group.MapDelete("{id:guid}", async (Guid id, ICommandHandler<DeleteInvoiceDraftCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new DeleteInvoiceDraftCommand(id), ct)).ToNoContent())
            .RequirePermission(Permissions.InvoicingIssue);

        group.MapPost("{id:guid}/stamp", async (Guid id, ICommandHandler<StampInvoiceCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new StampInvoiceCommand(id), ct)).ToOk())
            .RequirePermission(Permissions.InvoicingIssue)
            .WithSummary("Timbra el CFDI con el PAC configurado de la empresa.");

        group.MapPost("{id:guid}/cancel", async (Guid id, CancelInvoiceRequest body, ICommandHandler<CancelInvoiceCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new CancelInvoiceCommand(id, body.Motive, body.SubstitutionUuid), ct)).ToNoContent())
            .RequirePermission(Permissions.InvoicingCancel)
            .WithSummary("Cancela ante el SAT (motivos 01-04; el 01 requiere el UUID que la sustituye).");
    }

    internal sealed record InvoiceDraftRequest(Guid CustomerId, string? CfdiUse, string PaymentForm, string PaymentMethod, string? Currency, decimal? ExchangeRate,
        DateOnly? IssueDate, IReadOnlyList<InvoiceLineData> Lines, string? CopadeNumber, string? Notes)
    {
        public SaveInvoiceDraftCommand ToCommand(Guid? id) =>
            new(id, CustomerId, CfdiUse, PaymentForm, PaymentMethod, Currency ?? "MXN", ExchangeRate ?? 1, IssueDate, Lines, CopadeNumber, Notes);
    }

    internal sealed record CancelInvoiceRequest(string Motive, string? SubstitutionUuid);
}

/// <summary>Cobranza: payments with complement (REP), aging, collection notes and Pemex estimates/COPADEs.</summary>
internal sealed class CollectionEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("collections").WithTags("Cobranza");
        string[] read = [Permissions.CollectionsRead, Permissions.CollectionsManage];

        group.MapGet("payments", async (Guid? customerId, DateOnly? from, DateOnly? to, int? page, int? pageSize,
                IQueryHandler<SearchPaymentsQuery, PagedResult<PaymentSummaryDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchPaymentsQuery(customerId, from, to, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission(read);

        group.MapPost("payments", async (RegisterPaymentCommand command, ICommandHandler<RegisterPaymentCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToCreated("/api/collections/payments"))
            .RequirePermission(Permissions.CollectionsManage)
            .WithSummary("Registra un cobro y lo aplica a una o varias facturas.");

        group.MapDelete("payments/{id:guid}", async (Guid id, ICommandHandler<DeletePaymentCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new DeletePaymentCommand(id), ct)).ToNoContent())
            .RequirePermission(Permissions.CollectionsManage);

        group.MapPost("payments/{id:guid}/complement", async (Guid id, ICommandHandler<StampPaymentComplementCommand, string> handler, CancellationToken ct) =>
                (await handler.Handle(new StampPaymentComplementCommand(id), ct)).ToOk())
            .RequirePermission(Permissions.CollectionsManage, Permissions.InvoicingIssue)
            .WithSummary("Timbra el complemento de recepción de pagos 2.0 de las facturas PPD.");

        group.MapGet("aging", async (IQueryHandler<GetAgingQuery, AgingDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetAgingQuery(), ct)).ToOk())
            .RequirePermission([.. read, Permissions.DashboardRead])
            .WithSummary("Antigüedad de saldos por cliente (por vencer, 1-30, 31-60, 61-90, +90).");

        group.MapGet("notes", async (Guid? customerId, Guid? invoiceId, int? page, int? pageSize,
                IQueryHandler<SearchCollectionNotesQuery, PagedResult<CollectionNoteDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchCollectionNotesQuery(customerId, invoiceId, page ?? 1, pageSize ?? 50), ct)).ToOk())
            .RequirePermission(read);

        group.MapPost("notes", async (CollectionNoteData data, ICommandHandler<AddCollectionNoteCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new AddCollectionNoteCommand(data), ct)).ToCreated("/api/collections/notes"))
            .RequirePermission(Permissions.CollectionsManage)
            .WithSummary("Bitácora de gestión de cobro (llamadas, correos, promesas de pago con recordatorio).");

        group.MapGet("estimates", async (EstimateStatus? status, Guid? customerId, int? page, int? pageSize,
                IQueryHandler<SearchEstimatesQuery, PagedResult<EstimateDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchEstimatesQuery(status, customerId, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission([.. read, Permissions.InvoicingRead]);

        group.MapPost("estimates", async (EstimateData data, ICommandHandler<SaveEstimateCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveEstimateCommand(null, data), ct)).ToCreated("/api/collections/estimates"))
            .RequirePermission(Permissions.CollectionsManage);

        group.MapPut("estimates/{id:guid}", async (Guid id, EstimateData data, ICommandHandler<SaveEstimateCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveEstimateCommand(id, data), ct)).ToNoContent())
            .RequirePermission(Permissions.CollectionsManage);

        group.MapPost("estimates/{id:guid}/status", async (Guid id, EstimateStatusRequest body, ICommandHandler<ChangeEstimateStatusCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new ChangeEstimateStatusCommand(id, body.Action, body.Reason, body.CopadeNumber), ct)).ToNoContent())
            .RequirePermission(Permissions.CollectionsManage)
            .WithSummary("Flujo de la estimación: enviar, aprobar (con número de COPADE), rechazar.");

        group.MapPost("estimates/{id:guid}/invoice", async (Guid id, bool? stamp, ICommandHandler<InvoiceEstimateCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new InvoiceEstimateCommand(id, stamp ?? false), ct)).ToOk())
            .RequirePermission(Permissions.InvoicingIssue);
    }

    internal sealed record EstimateStatusRequest(EstimateAction Action, string? Reason, string? CopadeNumber);
}

/// <summary>Carta Porte 3.1 per tanker trip (traslado CFDI linked to unit, operator and route).</summary>
internal sealed class CartaPorteEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("cartaporte/trips").WithTags("Carta Porte");
        string[] read = [Permissions.CartaPorteRead, Permissions.CartaPorteManage];

        group.MapGet("", async (TripStatus? status, Guid? unitId, DateOnly? from, DateOnly? to, int? page, int? pageSize,
                IQueryHandler<SearchTripsQuery, PagedResult<TripSummaryDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchTripsQuery(status, unitId, from, to, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("{id:guid}", async (Guid id, IQueryHandler<GetTripQuery, TripDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetTripQuery(id), ct)).ToOk())
            .RequirePermission(read);

        group.MapPost("", async (TripData data, ICommandHandler<SaveTripCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveTripCommand(null, data), ct)).ToCreated("/api/cartaporte/trips"))
            .RequirePermission(Permissions.CartaPorteManage);

        group.MapPut("{id:guid}", async (Guid id, TripData data, ICommandHandler<SaveTripCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveTripCommand(id, data), ct)).ToNoContent())
            .RequirePermission(Permissions.CartaPorteManage);

        group.MapPost("{id:guid}/stamp", async (Guid id, ICommandHandler<StampTripCommand, string> handler, CancellationToken ct) =>
                (await handler.Handle(new StampTripCommand(id), ct)).ToOk())
            .RequirePermission(Permissions.CartaPorteManage)
            .WithSummary("Valida (unidad, permiso SCT, seguros, operador con licencia vigente, ruta) y timbra el CFDI de traslado con Carta Porte.");

        group.MapPost("{id:guid}/cancel", async (Guid id, ReasonRequest body, ICommandHandler<CancelTripCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new CancelTripCommand(id, body.Reason), ct)).ToNoContent())
            .RequirePermission(Permissions.CartaPorteManage);
    }
}
