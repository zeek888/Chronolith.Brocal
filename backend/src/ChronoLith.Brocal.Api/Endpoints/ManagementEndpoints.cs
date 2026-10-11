using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Management;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>Tablero de facturación, dashboard gerencial, reportes mensuales y portal del cliente.</summary>
internal sealed class ManagementEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("billing/board", async (DateOnly from, DateOnly to, Guid? customerId, IQueryHandler<GetBillingBoardQuery, BillingBoardDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetBillingBoardQuery(from, to, customerId), ct)).ToOk())
            .WithTags("Tablero de facturación")
            .RequirePermission(Permissions.BillingRead, Permissions.DashboardRead)
            .WithSummary("Ejecutado vs facturado vs cobrado, por mes y por cliente, con las boletas que esperan factura.");

        var dashboard = app.MapGroup("dashboard").WithTags("Dashboard gerencial");

        dashboard.MapGet("", async (DateOnly? from, DateOnly? to, IQueryHandler<GetDashboardQuery, DashboardDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetDashboardQuery(from, to), ct)).ToOk())
            .RequirePermission(Permissions.DashboardRead)
            .WithSummary("Utilización e ingreso por unidad, días promedio de boleta a factura, cartera vencida y servicios del mes.");

        dashboard.MapGet("month-summary", async (IQueryHandler<GetMonthSummaryQuery, MonthSummaryDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetMonthSummaryQuery(), ct)).ToOk())
            .RequirePermission(Permissions.DashboardRead, Permissions.BillingRead, Permissions.TicketsRead)
            .WithSummary("Resumen del mes (widget del dashboard de ChronoLith).");

        var reports = app.MapGroup("reports").WithTags("Reportes");

        reports.MapGet("monthly", async (int year, int month, Guid? customerId, string? field, ReportFormat? format,
                IQueryHandler<GetMonthlyReportQuery, FileContent> handler, CancellationToken ct) =>
                (await handler.Handle(new GetMonthlyReportQuery(year, month, customerId, field, format ?? ReportFormat.Pdf), ct)).ToFile())
            .RequirePermission(Permissions.ReportsRead, Permissions.PortalAccess)
            .WithSummary("Reporte mensual por cliente o campo, en PDF o Excel, con la marca de la empresa.");

        reports.MapGet("fields", async (IQueryHandler<GetReportFieldsQuery, IReadOnlyList<string>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetReportFieldsQuery(), ct)).ToOk())
            .RequirePermission(Permissions.ReportsRead, Permissions.PortalAccess);

        app.MapGet("portal/summary", async (Guid? customerId, IQueryHandler<GetPortalSummaryQuery, PortalSummaryDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetPortalSummaryQuery(customerId), ct)).ToOk())
            .WithTags("Portal del cliente")
            .RequirePermission(Permissions.PortalAccess, Permissions.CatalogsRead)
            .WithSummary("Inicio del portal: servicios del mes, órdenes programadas y estado de cuenta del cliente.");
    }
}
