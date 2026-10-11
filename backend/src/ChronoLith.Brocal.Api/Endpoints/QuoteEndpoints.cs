using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Quotes;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>Cotizaciones: priced from the price lists, sent as branded PDF and converted into a work order when accepted.</summary>
internal sealed class QuoteEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("quotes").WithTags("Cotizaciones");
        string[] read = [Permissions.QuotesRead, Permissions.QuotesManage];

        group.MapGet("", async (string? search, QuoteStatus? status, Guid? customerId, int? page, int? pageSize,
                IQueryHandler<SearchQuotesQuery, PagedResult<QuoteSummaryDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchQuotesQuery(search, status, customerId, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("{id:guid}", async (Guid id, IQueryHandler<GetQuoteQuery, QuoteDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetQuoteQuery(id), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("{id:guid}/pdf", async (Guid id, IQueryHandler<GetQuotePdfQuery, FileContent> handler, CancellationToken ct) =>
                (await handler.Handle(new GetQuotePdfQuery(id), ct)).ToFile())
            .RequirePermission(read);

        group.MapPost("", async (QuoteData data, ICommandHandler<SaveQuoteCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveQuoteCommand(null, data), ct)).ToCreated("/api/quotes"))
            .RequirePermission(Permissions.QuotesManage);

        group.MapPut("{id:guid}", async (Guid id, QuoteData data, ICommandHandler<SaveQuoteCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveQuoteCommand(id, data), ct)).ToNoContent())
            .RequirePermission(Permissions.QuotesManage);

        group.MapPost("{id:guid}/status", async (Guid id, QuoteStatusRequest body, ICommandHandler<ChangeQuoteStatusCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new ChangeQuoteStatusCommand(id, body.Action, body.Reason), ct)).ToNoContent())
            .RequirePermission(Permissions.QuotesManage);

        group.MapPost("{id:guid}/convert", async (Guid id, ConvertQuoteRequest body, ICommandHandler<ConvertQuoteCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new ConvertQuoteCommand(id, body.ScheduledStart, body.ScheduledEnd, body.SiteId, body.ServiceTypeId), ct)).ToOk())
            .RequirePermission(Permissions.QuotesManage)
            .WithSummary("Convierte la cotización aceptada en orden de trabajo (con sus conceptos y precios).");
    }

    internal sealed record QuoteStatusRequest(QuoteAction Action, string? Reason);

    internal sealed record ConvertQuoteRequest(DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd, Guid? SiteId, Guid? ServiceTypeId);
}
