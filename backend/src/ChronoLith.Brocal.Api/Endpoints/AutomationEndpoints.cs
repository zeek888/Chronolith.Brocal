using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Automation;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>
/// Flujos de autorización, reglas de negocio y solicitudes. Company definitions need <c>automation.manage</c>;
/// global ones (every company) are created and changed only by the platform administrator. Any member works with
/// the requests addressed to them.
/// </summary>
internal sealed class AutomationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("automation").WithTags("Flujos y reglas");
        string[] read = [Permissions.AutomationRead, Permissions.AutomationManage];

        group.MapGet("catalog", async (IQueryHandler<GetAutomationCatalogQuery, AutomationCatalogDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetAutomationCatalogQuery(), ct)).ToOk())
            .RequireTenant()
            .WithSummary("Documentos, momentos y datos disponibles para las condiciones.");

        // ---- Workflows ----
        var workflows = group.MapGroup("workflows");

        workflows.MapGet("", async (IQueryHandler<GetWorkflowDefinitionsQuery, IReadOnlyList<WorkflowDefinitionDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetWorkflowDefinitionsQuery(), ct)).ToOk())
            .RequirePermission(read)
            .WithSummary("Flujos de la empresa y globales.");

        workflows.MapPost("", async (WorkflowRequest body, ICommandHandler<SaveWorkflowDefinitionCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveWorkflowDefinitionCommand(null, body.Global, body.ToData()), ct)).ToCreated("/api/automation/workflows"))
            .RequirePermission(Permissions.AutomationManage)
            .WithSummary("Crea un flujo de la empresa o, con global=true, uno para todas las empresas (administrador de la plataforma).");

        workflows.MapPut("{id:guid}", async (Guid id, WorkflowRequest body, ICommandHandler<SaveWorkflowDefinitionCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveWorkflowDefinitionCommand(id, body.Global, body.ToData()), ct)).ToOk())
            .RequirePermission(Permissions.AutomationManage);

        workflows.MapDelete("{id:guid}", async (Guid id, ICommandHandler<DeleteWorkflowDefinitionCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new DeleteWorkflowDefinitionCommand(id), ct)).ToNoContent())
            .RequirePermission(Permissions.AutomationManage);

        workflows.MapPut("{id:guid}/opt-out", async (Guid id, OptOutRequest body, ICommandHandler<SetAutomationOptOutCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new SetAutomationOptOutCommand(AutomationKind.Workflow, id, body.OptOut), ct)).ToNoContent())
            .RequirePermission(Permissions.AutomationManage)
            .WithSummary("Desactiva (o vuelve a activar) para la empresa un flujo global opcional.");

        // ---- Business rules ----
        var rules = group.MapGroup("rules");

        rules.MapGet("", async (IQueryHandler<GetBusinessRulesQuery, IReadOnlyList<BusinessRuleDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetBusinessRulesQuery(), ct)).ToOk())
            .RequirePermission(read);

        rules.MapPost("", async (RuleRequest body, ICommandHandler<SaveBusinessRuleCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveBusinessRuleCommand(null, body.Global, body.ToData()), ct)).ToCreated("/api/automation/rules"))
            .RequirePermission(Permissions.AutomationManage);

        rules.MapPut("{id:guid}", async (Guid id, RuleRequest body, ICommandHandler<SaveBusinessRuleCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveBusinessRuleCommand(id, body.Global, body.ToData()), ct)).ToOk())
            .RequirePermission(Permissions.AutomationManage);

        rules.MapDelete("{id:guid}", async (Guid id, ICommandHandler<DeleteBusinessRuleCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new DeleteBusinessRuleCommand(id), ct)).ToNoContent())
            .RequirePermission(Permissions.AutomationManage);

        rules.MapPut("{id:guid}/opt-out", async (Guid id, OptOutRequest body, ICommandHandler<SetAutomationOptOutCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new SetAutomationOptOutCommand(AutomationKind.Rule, id, body.OptOut), ct)).ToNoContent())
            .RequirePermission(Permissions.AutomationManage);

        // ---- Approval requests ----
        var approvals = group.MapGroup("approvals").RequireTenant();

        approvals.MapGet("", async (ApprovalView? view, WorkflowStatus? status, AutomationDocument? document, int? page, int? pageSize,
                IQueryHandler<GetApprovalsQuery, PagedResult<ApprovalSummaryDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetApprovalsQuery(view ?? ApprovalView.Mine, status, document, page ?? 1, pageSize ?? 25), ct)).ToOk())
            .WithSummary("Solicitudes por decidir (mine), las que pedí (requested) o todas (all, automation.read).");

        approvals.MapGet("count", async (IQueryHandler<GetMyPendingApprovalsQuery, int> handler, CancellationToken ct) =>
            (await handler.Handle(new GetMyPendingApprovalsQuery(), ct)).ToOk());

        approvals.MapGet("{id:guid}", async (Guid id, IQueryHandler<GetApprovalQuery, ApprovalDetailDto> handler, CancellationToken ct) =>
            (await handler.Handle(new GetApprovalQuery(id), ct)).ToOk());

        approvals.MapPost("{id:guid}/decision", async (Guid id, DecisionRequest body, ICommandHandler<DecideApprovalCommand, ApprovalDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new DecideApprovalCommand(id, body.Approve, body.Comment), ct)).ToOk())
            .WithSummary("Autoriza o rechaza el paso actual (el rechazo necesita comentario).");

        approvals.MapPost("{id:guid}/cancel", async (Guid id, CancelRequest body, ICommandHandler<CancelApprovalCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new CancelApprovalCommand(id, body.Reason), ct)).ToNoContent());

        // ---- Workflow of a document ----
        var documents = group.MapGroup("documents").RequireTenant();

        documents.MapGet("{document}/{id:guid}", async (string document, Guid id, IQueryHandler<GetDocumentWorkflowQuery, DocumentWorkflowDto> handler, CancellationToken ct) =>
                Enum.TryParse<AutomationDocument>(document, ignoreCase: true, out var kind)
                    ? (await handler.Handle(new GetDocumentWorkflowQuery(kind, id), ct)).ToOk()
                    : Results.NotFound())
            .WithSummary("Solicitud actual, historial y si se puede solicitar la autorización del documento.");

        documents.MapPost("{document}/{id:guid}/request", async (string document, Guid id, ICommandHandler<RequestApprovalCommand, RequestApprovalResultDto> handler, CancellationToken ct) =>
                Enum.TryParse<AutomationDocument>(document, ignoreCase: true, out var kind)
                    ? (await handler.Handle(new RequestApprovalCommand(kind, id), ct)).ToOk()
                    : Results.NotFound())
            .WithSummary("Solicita la autorización sin ejecutar la acción (o la vuelve a solicitar tras un rechazo).");
    }

    internal sealed record WorkflowRequest(
        bool Global, string Name, string? Description, AutomationDocument Document, bool IsEnabled, bool IsMandatory, int Priority, ConditionMatch Match,
        IReadOnlyList<RuleCondition>? Conditions, IReadOnlyList<WorkflowStep>? Steps)
    {
        public WorkflowDefinitionData ToData() => new(Name, Description, Document, IsEnabled, IsMandatory, Priority, Match, Conditions, Steps);
    }

    internal sealed record RuleRequest(
        bool Global, string Name, string? Description, AutomationDocument Document, IReadOnlyList<AutomationTrigger>? Triggers, bool IsEnabled, bool IsMandatory, int Priority,
        ConditionMatch Match, IReadOnlyList<RuleCondition>? Conditions, RuleEffect Effect, string Message, NotificationSeverity Severity, RuleRecipients? Recipients)
    {
        public BusinessRuleData ToData() => new(Name, Description, Document, Triggers, IsEnabled, IsMandatory, Priority, Match, Conditions, Effect, Message, Severity, Recipients);
    }

    internal sealed record OptOutRequest(bool OptOut);

    internal sealed record DecisionRequest(bool Approve, string? Comment);

    internal sealed record CancelRequest(string Reason);
}
