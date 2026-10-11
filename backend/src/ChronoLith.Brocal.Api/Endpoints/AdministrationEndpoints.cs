using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Fleet;
using ChronoLith.Brocal.Application.Features.Integrations;
using ChronoLith.Brocal.Application.Features.Notifications;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Application.Features.Tickets;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>Bitácora de auditoría: who captured, edited, signed, cancelled or exported what.</summary>
internal sealed class AuditEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("audit").WithTags("Bitácora").RequirePermission(Permissions.AuditRead);

        group.MapGet("", async (string? entityType, Guid? entityId, string? user, AuditAction? action, DateOnly? from, DateOnly? to, string? search, int? page, int? pageSize,
            IQueryHandler<SearchAuditQuery, PagedResult<AuditEntryDto>> handler, CancellationToken ct) =>
            (await handler.Handle(new SearchAuditQuery(entityType, entityId, user, action, from, to, search, page ?? 1, pageSize ?? 50), ct)).ToOk());

        group.MapGet("entity-types", async (IQueryHandler<GetAuditEntityTypesQuery, IReadOnlyList<string>> handler, CancellationToken ct) =>
            (await handler.Handle(new GetAuditEntityTypesQuery(), ct)).ToOk());
    }
}

/// <summary>Notificaciones: in-app inbox, rules per event, channels (email/WhatsApp) and the delivery monitor.</summary>
internal sealed class NotificationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("me/notifications").WithTags("Notificaciones").RequireTenant();

        mine.MapGet("", async (bool? unreadOnly, int? take, IQueryHandler<GetMyNotificationsQuery, MyNotificationsDto> handler, CancellationToken ct) =>
            (await handler.Handle(new GetMyNotificationsQuery(unreadOnly ?? false, take ?? 30), ct)).ToOk());

        mine.MapPost("read", async (ReadRequest body, ICommandHandler<MarkNotificationsReadCommand, int> handler, CancellationToken ct) =>
                (await handler.Handle(new MarkNotificationsReadCommand(body.Ids), ct)).ToOk())
            .WithSummary("Marca como leídas las notificaciones indicadas (o todas si no se envían ids).");

        var group = app.MapGroup("notifications").WithTags("Notificaciones").RequirePermission(Permissions.NotificationsManage);

        group.MapGet("rules", async (IQueryHandler<GetNotificationRulesQuery, IReadOnlyList<NotificationRuleDto>> handler, CancellationToken ct) =>
            (await handler.Handle(new GetNotificationRulesQuery(), ct)).ToOk());

        group.MapPut("rules", async (SaveNotificationRuleCommand command, ICommandHandler<SaveNotificationRuleCommand> handler, CancellationToken ct) =>
            (await handler.Handle(command, ct)).ToNoContent());

        group.MapGet("channels", async (IQueryHandler<GetChannelSettingsQuery, ChannelSettingsDto> handler, CancellationToken ct) =>
            (await handler.Handle(new GetChannelSettingsQuery(), ct)).ToOk());

        group.MapPut("channels", async (SaveChannelSettingsCommand command, ICommandHandler<SaveChannelSettingsCommand> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToNoContent())
            .WithSummary("SMTP propio y WhatsApp Cloud API de la empresa (las contraseñas y tokens se guardan cifrados).");

        group.MapGet("outbox", async (OutboxStatus? status, OutboxChannel? channel, int? page, int? pageSize,
            IQueryHandler<SearchOutboxQuery, PagedResult<OutboxMessageDto>> handler, CancellationToken ct) =>
            (await handler.Handle(new SearchOutboxQuery(status, channel, page ?? 1, pageSize ?? 50), ct)).ToOk());

        group.MapPost("outbox/{id:guid}/retry", async (Guid id, ICommandHandler<RetryOutboxCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new RetryOutboxCommand(id), ct)).ToNoContent());

        group.MapPost("test", async (SendTestNotificationCommand command, ICommandHandler<SendTestNotificationCommand> handler, CancellationToken ct) =>
            (await handler.Handle(command, ct)).ToNoContent());
    }

    internal sealed record ReadRequest(IReadOnlyList<Guid>? Ids);
}

/// <summary>
/// Integraciones: API keys, GPS positions, exports to Excel and accounting vouchers (CONTPAQi/Aspel). The
/// <c>integrations/gps</c> and <c>integrations/feeds</c> routes are machine-to-machine: they authenticate with the
/// <c>X-Api-Key</c> header instead of a Keycloak session.
/// </summary>
internal sealed class IntegrationEndpoints : IEndpoint
{
    public const string ApiKeyHeader = "X-Api-Key";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("integrations").WithTags("Integraciones");
        string[] export = [Permissions.IntegrationsExport, Permissions.IntegrationsManage];

        var keys = group.MapGroup("api-keys").RequirePermission(Permissions.IntegrationsManage);

        keys.MapGet("", async (IQueryHandler<GetApiKeysQuery, IReadOnlyList<ApiKeyDto>> handler, CancellationToken ct) =>
            (await handler.Handle(new GetApiKeysQuery(), ct)).ToOk());

        keys.MapPost("", async (CreateApiKeyCommand command, ICommandHandler<CreateApiKeyCommand, CreatedApiKeyDto> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToOk())
            .WithSummary("Crea una API key; el valor completo solo se muestra en esta respuesta.");

        keys.MapDelete("{id:guid}", async (Guid id, ICommandHandler<RevokeApiKeyCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new RevokeApiKeyCommand(id), ct)).ToNoContent());

        group.MapGet("exports", async (IQueryHandler<GetExportDatasetsQuery, IReadOnlyList<ExportDatasetDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetExportDatasetsQuery(), ct)).ToOk())
            .RequirePermission(export);

        group.MapGet("exports/{dataset}", async (string dataset, DateOnly? from, DateOnly? to, IQueryHandler<ExportDatasetQuery, FileContent> handler, CancellationToken ct) =>
                (await handler.Handle(new ExportDatasetQuery(dataset, from, to), ct)).ToFile())
            .RequirePermission(export)
            .WithSummary("Exporta a Excel cualquier conjunto de datos de los módulos activos.");

        group.MapGet("accounting", async (IQueryHandler<GetAccountingSettingsQuery, AccountingSettingsDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetAccountingSettingsQuery(), ct)).ToOk())
            .RequirePermission(Permissions.IntegrationsManage);

        group.MapPut("accounting", async (SaveAccountingSettingsCommand command, ICommandHandler<SaveAccountingSettingsCommand> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToNoContent())
            .RequirePermission(Permissions.IntegrationsManage);

        group.MapGet("accounting/export", async (DateOnly from, DateOnly to, IQueryHandler<ExportAccountingQuery, FileContent> handler, CancellationToken ct) =>
                (await handler.Handle(new ExportAccountingQuery(from, to), ct)).ToFile())
            .RequirePermission(export)
            .WithSummary("Pólizas de ingresos y cobranza para importar en CONTPAQi Contabilidad o Aspel COI.");

        // ---- Machine-to-machine (API key) ----

        group.MapPost("gps/positions", async (GpsRequest body, HttpRequest request, IApiKeyAuthenticator authenticator, ITenantContext tenant,
                ICommandHandler<IngestPositionsCommand, IngestPositionsResult> handler, CancellationToken ct) =>
            {
                var key = await authenticator.AuthenticateAsync(request.Headers[ApiKeyHeader], ApiScopes.GpsWrite, ct);
                if (key.IsFailure)
                {
                    return Unauthorized(key.Error);
                }

                using var scope = tenant.BeginScope(key.Value);
                return (await handler.Handle(new IngestPositionsCommand(string.IsNullOrWhiteSpace(body.Source) ? "gps" : body.Source.Trim(), body.Positions ?? []), ct)).ToOk();
            })
            .AllowAnonymous()
            .WithSummary("Recibe posiciones del proveedor de GPS de la flota (API key con alcance gps.write).");

        group.MapGet("feeds/tickets", async (DateOnly? from, DateOnly? to, TicketStatus[]? status, Guid? customerId, int? page, int? pageSize, HttpRequest request,
                IApiKeyAuthenticator authenticator, ITenantContext tenant, IQueryHandler<SearchTicketsQuery, PagedResult<TicketSummaryDto>> handler, CancellationToken ct) =>
            {
                var key = await authenticator.AuthenticateAsync(request.Headers[ApiKeyHeader], ApiScopes.TicketsRead, ct);
                if (key.IsFailure)
                {
                    return Unauthorized(key.Error);
                }

                using var scope = tenant.BeginScope(key.Value);
                return (await handler.Handle(new SearchTicketsQuery(null, status, customerId, null, null, null, null, from, to, null, page ?? 1, Math.Min(pageSize ?? 100, 500), null, true), ct))
                    .ToOk();
            })
            .AllowAnonymous()
            .WithSummary("Boletas para sistemas externos (API key con alcance tickets.read).");

        group.MapGet("feeds/exports/{dataset}", async (string dataset, DateOnly? from, DateOnly? to, HttpRequest request, IApiKeyAuthenticator authenticator, ITenantContext tenant,
                IQueryHandler<ExportDatasetQuery, FileContent> handler, CancellationToken ct) =>
            {
                var key = await authenticator.AuthenticateAsync(request.Headers[ApiKeyHeader], ApiScopes.ExportRead, ct);
                if (key.IsFailure)
                {
                    return Unauthorized(key.Error);
                }

                using var scope = tenant.BeginScope(key.Value);
                return (await handler.Handle(new ExportDatasetQuery(dataset, from, to), ct)).ToFile();
            })
            .AllowAnonymous()
            .WithSummary("Exportación a Excel para sistemas externos (API key con alcance export.read).");
    }

    private static IResult Unauthorized(Error error) =>
        Results.Problem(title: "API key inválida", detail: error.Description, statusCode: StatusCodes.Status401Unauthorized, extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    internal sealed record GpsRequest(string? Source, IReadOnlyList<PositionInput>? Positions);
}
