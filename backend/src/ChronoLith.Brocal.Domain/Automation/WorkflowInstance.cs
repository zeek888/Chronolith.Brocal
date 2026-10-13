using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.Automation;

public enum WorkflowStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}

/// <summary>Approval or rejection of one person in one step.</summary>
public sealed record WorkflowDecision(int Step, Guid? MembershipId, string Name, bool Approved, string? Comment, DateTimeOffset At);

/// <summary>A request was created: the approvers of the first step must hear about it.</summary>
public sealed record WorkflowRequestedEvent(Guid InstanceId) : IDomainEvent;

/// <summary>A step was approved and the request moved to the next one.</summary>
public sealed record WorkflowStepAdvancedEvent(Guid InstanceId) : IDomainEvent;

/// <summary>The request was approved, rejected or cancelled.</summary>
public sealed record WorkflowCompletedEvent(Guid InstanceId, AutomationDocument Document, Guid DocumentId, WorkflowStatus Status, Guid? DecidedById, string DecidedBy, string? Comment) : IDomainEvent;

/// <summary>
/// Approval request of one document. It copies the steps of its workflow (later edits of the workflow do not change
/// requests in progress) and the data the decision is taken on: an approval stays valid while the document keeps
/// the same data (<see cref="FactsHash"/>).
/// </summary>
public sealed class WorkflowInstance : TenantAggregateRoot, IAuditReference
{
    private List<WorkflowStep> _steps = [];
    private List<WorkflowDecision> _decisions = [];
    private List<RuleCondition> _conditions = [];
    private Dictionary<string, string?> _facts = [];

    private WorkflowInstance()
    {
    }

    public Guid DefinitionId { get; private set; }

    public string DefinitionName { get; private set; } = null!;

    public bool FromGlobalDefinition { get; private set; }

    public AutomationDocument Document { get; private set; }

    public Guid DocumentId { get; private set; }

    /// <summary>Folio of the document.</summary>
    public string DocumentReference { get; private set; } = null!;

    public WorkflowCompletion Completion { get; private set; }

    public string FactsHash { get; private set; } = null!;

    /// <summary>Data of the document when it was requested (shown to the approvers).</summary>
    public IReadOnlyDictionary<string, string?> Facts => _facts;

    /// <summary>Conditions of the workflow that made it apply.</summary>
    public IReadOnlyList<RuleCondition> Conditions => _conditions;

    public ConditionMatch Match { get; private set; }

    public IReadOnlyList<WorkflowStep> Steps => _steps;

    public IReadOnlyList<WorkflowDecision> Decisions => _decisions;

    /// <summary>Membership that requested it (null when the system started it).</summary>
    public Guid? RequestedById { get; private set; }

    public string RequestedByName { get; private set; } = null!;

    public DateTimeOffset RequestedAt { get; private set; }

    public WorkflowStatus Status { get; private set; } = WorkflowStatus.Pending;

    /// <summary>Index of the step waiting for decisions.</summary>
    public int CurrentStep { get; private set; }

    public DateTimeOffset StepStartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Reason of the rejection or the cancellation.</summary>
    public string? ClosingComment { get; private set; }

    public string AuditReference => $"{DocumentReference} · {DefinitionName}";

    public bool IsPending => Status == WorkflowStatus.Pending;

    public WorkflowStep? Step => IsPending ? _steps[CurrentStep] : null;

    public int ApprovalsInCurrentStep => _decisions.Count(d => d.Step == CurrentStep && d.Approved);

    public static WorkflowInstance Request(
        WorkflowDefinition definition,
        Guid documentId,
        string reference,
        IReadOnlyDictionary<string, object?> facts,
        Guid? requestedById,
        string requestedByName,
        DateTimeOffset now)
    {
        var document = AutomationCatalog.Of(definition.Document);
        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionName = definition.Name,
            FromGlobalDefinition = definition.IsGlobal,
            Document = definition.Document,
            DocumentId = documentId,
            DocumentReference = reference,
            Completion = document.Workflow!.Completion,
            FactsHash = RuleConditions.Hash(document, facts),
            _facts = facts.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => RuleConditions.Format(f.Value)),
            _conditions = definition.Conditions.ToList(),
            Match = definition.Match,
            _steps = definition.Steps.ToList(),
            RequestedById = requestedById,
            RequestedByName = requestedByName,
            RequestedAt = now,
            StepStartedAt = now
        };

        instance.Raise(new WorkflowRequestedEvent(instance.Id));
        return instance;
    }

    public bool HasDecided(Guid membershipId) => _decisions.Any(d => d.Step == CurrentStep && d.MembershipId == membershipId);

    /// <summary>
    /// Records the decision of an approver of the current step (who may decide is resolved by the application). A
    /// rejection ends the request; enough approvals move it to the next step or approve it.
    /// </summary>
    public Result Decide(Guid membershipId, string name, bool approve, string? comment, DateTimeOffset now)
    {
        if (!IsPending)
        {
            return WorkflowErrors.NotPending;
        }

        if (HasDecided(membershipId))
        {
            return WorkflowErrors.AlreadyDecided;
        }

        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (text is { Length: > 1000 })
        {
            return WorkflowErrors.CommentTooLong;
        }

        if (!approve && text is null)
        {
            return WorkflowErrors.ReasonRequired;
        }

        _decisions = [.. _decisions, new WorkflowDecision(CurrentStep, membershipId, name, approve, text, now)];

        if (!approve)
        {
            Finish(WorkflowStatus.Rejected, membershipId, name, text, now);
            return Result.Success();
        }

        if (ApprovalsInCurrentStep < _steps[CurrentStep].RequiredApprovals)
        {
            return Result.Success();
        }

        if (CurrentStep == _steps.Count - 1)
        {
            Finish(WorkflowStatus.Approved, membershipId, name, text, now);
            return Result.Success();
        }

        CurrentStep++;
        StepStartedAt = now;
        Raise(new WorkflowStepAdvancedEvent(Id));
        return Result.Success();
    }

    /// <summary>Withdrawn by the requester or an administrator, or closed by the system (document cancelled, data changed).</summary>
    public Result Cancel(Guid? byId, string by, string reason, DateTimeOffset now)
    {
        if (!IsPending)
        {
            return WorkflowErrors.NotPending;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return WorkflowErrors.ReasonRequired;
        }

        Finish(WorkflowStatus.Cancelled, byId, by, reason.Trim(), now);
        return Result.Success();
    }

    private void Finish(WorkflowStatus status, Guid? byId, string by, string? comment, DateTimeOffset now)
    {
        Status = status;
        CompletedAt = now;
        ClosingComment = comment is { Length: > 1000 } ? comment[..1000] : comment;
        Raise(new WorkflowCompletedEvent(Id, Document, DocumentId, status, byId, by, ClosingComment));
    }
}

/// <summary>
/// The guarded action was held: the document waits for the approval of its workflow. The API answers it as
/// <c>202 Accepted</c> (the request was registered), not as an error.
/// </summary>
public sealed record ApprovalPendingError(Guid InstanceId, string Message)
    : Error("Workflow.ApprovalPending", Message, ErrorType.Conflict);

public static class WorkflowErrors
{
    public static readonly Error NotFound = Error.NotFound("Workflow.NotFound", "La solicitud de autorización no existe.");

    public static readonly Error NotPending = Error.Conflict("Workflow.NotPending", "La solicitud ya fue resuelta o cancelada.");

    public static readonly Error AlreadyDecided = Error.Conflict("Workflow.AlreadyDecided", "Ya registraste tu decisión en este paso.");

    public static readonly Error ReasonRequired = Error.Validation("Workflow.ReasonRequired", "Indica el motivo.");

    public static readonly Error CommentTooLong = Error.Validation("Workflow.CommentTooLong", "El comentario admite hasta 1000 caracteres.");

    public static readonly Error NotApprover = Error.Forbidden("Workflow.NotApprover", "No eres aprobador del paso actual de esta solicitud.");

    public static readonly Error OwnRequest = Error.Forbidden("Workflow.OwnRequest", "Este paso no permite que quien solicitó la autorización la apruebe.");

    public static readonly Error CannotCancel = Error.Forbidden("Workflow.CannotCancel", "Solo quien la solicitó o un administrador puede cancelar la solicitud.");

    public static readonly Error NotApplicable = Error.Conflict("Workflow.NotApplicable", "Ningún flujo de autorización aplica al documento en su estado actual.");

    public static Error InProgress(string reference, string step) =>
        Error.Conflict("Workflow.InProgress", $"{reference} está en autorización (paso: {step}); la decisión se toma desde su solicitud.");

    public static Error Rejected(string reference) =>
        Error.Conflict("Workflow.Rejected", $"{reference} fue rechazada en autorización: corrígela y solicítala de nuevo.");
}
