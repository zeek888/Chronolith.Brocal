using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.People;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>Personal, cuadrillas y asistencia en campo (insumo para nómina y costeo de boletas).</summary>
internal sealed class PeopleEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapCatalog<Employee, EmployeeData, EmployeeDto>("personnel/employees", "Personal",
            [Permissions.PersonnelRead, Permissions.PersonnelManage, Permissions.CrewsRead, Permissions.CrewsManage, Permissions.DispatchManage, Permissions.HseRead],
            Permissions.PersonnelManage);
        app.MapCatalog<Crew, CrewData, CrewDto>("crews", "Cuadrillas",
            [Permissions.CrewsRead, Permissions.CrewsManage, Permissions.CrewsAttendance, Permissions.DispatchManage, Permissions.PersonnelRead],
            Permissions.CrewsManage);

        var group = app.MapGroup("attendance").WithTags("Cuadrillas");

        group.MapGet("", async (DateOnly date, Guid? crewId, IQueryHandler<GetAttendanceSheetQuery, IReadOnlyList<AttendanceRowDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetAttendanceSheetQuery(date, crewId), ct)).ToOk())
            .RequirePermission(Permissions.CrewsAttendance, Permissions.CrewsRead, Permissions.CrewsManage)
            .WithSummary("Lista de asistencia del día por cuadrilla (precargada con las órdenes programadas).");

        group.MapPost("", async (AttendanceRequest body, ICommandHandler<SaveAttendanceCommand, int> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveAttendanceCommand(body.Records), ct)).ToOk())
            .RequirePermission(Permissions.CrewsAttendance, Permissions.CrewsManage);

        group.MapGet("payroll", async (DateOnly from, DateOnly to, Guid? crewId, IQueryHandler<GetPayrollSummaryQuery, IReadOnlyList<PayrollRowDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetPayrollSummaryQuery(from, to, crewId), ct)).ToOk())
            .RequirePermission(Permissions.CrewsRead, Permissions.CrewsManage)
            .WithSummary("Pre-nómina: días trabajados, horas, extras y días en campo por persona.");

        app.MapGet("tickets/{id:guid}/cost", async (Guid id, IQueryHandler<GetTicketCostQuery, TicketCostDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetTicketCostQuery(id), ct)).ToOk())
            .WithTags("Boletas")
            .RequirePermission(Permissions.TicketsRead, Permissions.CrewsRead, Permissions.BillingRead, Permissions.DashboardRead)
            .WithSummary("Costeo de la boleta: mano de obra (asistencia), combustible y refacciones contra el importe cobrado.");
    }

    internal sealed record AttendanceRequest(IReadOnlyList<AttendanceData> Records);
}

/// <summary>Viáticos y gastos: reports with receipt photos, approval and settlement.</summary>
internal sealed class ExpenseEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("expenses").WithTags("Viáticos");
        string[] read = [Permissions.ExpensesCapture, Permissions.ExpensesRead, Permissions.ExpensesApprove];

        group.MapGet("", async (ExpenseStatus? status, Guid? employeeId, int? page, int? pageSize,
                IQueryHandler<SearchExpenseReportsQuery, PagedResult<ExpenseReportDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchExpenseReportsQuery(status, employeeId, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .RequirePermission(read);

        group.MapGet("{id:guid}", async (Guid id, IQueryHandler<GetExpenseReportQuery, ExpenseReportDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetExpenseReportQuery(id), ct)).ToOk())
            .RequirePermission(read);

        group.MapPost("", async (ExpenseReportData data, ICommandHandler<SaveExpenseReportCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveExpenseReportCommand(null, data), ct)).ToCreated("/api/expenses"))
            .RequirePermission(Permissions.ExpensesCapture, Permissions.ExpensesApprove);

        group.MapPut("{id:guid}", async (Guid id, ExpenseReportData data, ICommandHandler<SaveExpenseReportCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveExpenseReportCommand(id, data), ct)).ToNoContent())
            .RequirePermission(Permissions.ExpensesCapture, Permissions.ExpensesApprove);

        group.MapPost("{id:guid}/status", async (Guid id, ExpenseStatusRequest body, ICommandHandler<ChangeExpenseStatusCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new ChangeExpenseStatusCommand(id, body.Action, body.Reason), ct)).ToNoContent())
            .RequirePermission(Permissions.ExpensesCapture, Permissions.ExpensesApprove)
            .WithSummary("Enviar, aprobar, rechazar o liquidar la comprobación.");
    }

    internal sealed record ExpenseStatusRequest(ExpenseAction Action, string? Reason);
}
