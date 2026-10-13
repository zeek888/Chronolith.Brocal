using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Automation;

public enum ApprovalView
{
    /// <summary>Pending requests the user can decide now.</summary>
    Mine = 0,

    /// <summary>Requests the user asked for.</summary>
    Requested = 1,

    /// <summary>Every request of the company (<c>automation.read</c>).</summary>
    All = 2
}

public sealed record ApprovalSummaryDto(
    Guid Id, AutomationDocument Document, Guid DocumentId, string Reference, string Link, string DefinitionName, bool FromGlobalDefinition, WorkflowStatus Status,
    int CurrentStep, int StepCount, string? StepName, string RequestedByName, DateTimeOffset RequestedAt, DateTimeOffset StepStartedAt, DateTimeOffset? CompletedAt,
    string? ClosingComment, decimal? Amount, bool Overdue, bool CanDecide);

/// <summary>Step of a request: "approved", "current", "waiting", "rejected" or "cancelled", with the people that approve it.</summary>
public sealed record ApprovalStepDto(int Index, string Name, int RequiredApprovals, int? SlaHours, bool AllowRequester, Audience Approvers, string State, IReadOnlyList<string> ApproverNames);

public sealed record ApprovalDetailDto(
    ApprovalSummaryDto Summary, IReadOnlyList<ApprovalStepDto> Steps, IReadOnlyList<WorkflowDecision> Decisions, IReadOnlyDictionary<string, string?> Facts,
    IReadOnlyList<RuleCondition> Conditions, ConditionMatch Match, bool CanCancel);

/// <summary>Workflow panel of a document: the latest request, the previous ones and whether it can be requested.</summary>
public sealed record DocumentWorkflowDto(
    AutomationDocument Document, Guid DocumentId, bool AtGate, bool Applies, string? DefinitionName, ApprovalDetailDto? Current, IReadOnlyList<ApprovalSummaryDto> History, bool CanRequest);

public sealed record RequestApprovalResultDto(bool Required, Guid? InstanceId, string Message);

/// <summary>Builds the views of the requests with what the current user can do on them.</summary>
internal sealed class ApprovalViews(ITenantContext tenant, AudienceResolver audiences, ITenantClock clock)
{
    public async Task<ApprovalSummaryDto> SummaryAsync(WorkflowInstance instance, CancellationToken cancellationToken)
    {
        var document = AutomationCatalog.Of(instance.Document);
        var overdue = instance is { IsPending: true, Step.SlaHours: { } sla } && (clock.UtcNow - instance.StepStartedAt).TotalHours >= sla;
        return new ApprovalSummaryDto(
            instance.Id, instance.Document, instance.DocumentId, instance.DocumentReference, document.LinkOf(instance.DocumentId), instance.DefinitionName,
            instance.FromGlobalDefinition, instance.Status, instance.CurrentStep, instance.Steps.Count, instance.Step?.Name, instance.RequestedByName, instance.RequestedAt,
            instance.StepStartedAt, instance.CompletedAt, instance.ClosingComment, WorkflowTexts.Amount(instance.Facts), overdue,
            await audiences.CanDecideAsync(instance, tenant.Membership, cancellationToken));
    }

    public async Task<ApprovalDetailDto> DetailAsync(WorkflowInstance instance, CancellationToken cancellationToken)
    {
        var steps = new List<ApprovalStepDto>();
        for (var index = 0; index < instance.Steps.Count; index++)
        {
            var step = instance.Steps[index];
            var decisions = instance.Decisions.Where(d => d.Step == index).ToList();
            var state = instance.Status switch
            {
                _ when decisions.Any(d => !d.Approved) => "rejected",
                WorkflowStatus.Approved => "approved",
                WorkflowStatus.Pending when index < instance.CurrentStep => "approved",
                WorkflowStatus.Pending when index == instance.CurrentStep => "current",
                WorkflowStatus.Pending => "waiting",
                _ when index < instance.CurrentStep => "approved",
                _ => "cancelled"
            };

            var names = state is "approved" or "rejected"
                ? decisions.Select(d => d.Name).ToList()
                : (await audiences.ApproversAsync(step, cancellationToken)).Where(a => step.AllowRequester || a.MembershipId != instance.RequestedById).Select(a => a.Name).ToList();
            steps.Add(new ApprovalStepDto(index, step.Name, step.RequiredApprovals, step.SlaHours, step.AllowRequester, step.Approvers, state, names));
        }

        var canCancel = instance.IsPending && (tenant.Membership?.Id is { } me && me == instance.RequestedById || tenant.Has(Permissions.AutomationManage));
        return new ApprovalDetailDto(await SummaryAsync(instance, cancellationToken), steps, instance.Decisions, instance.Facts, instance.Conditions, instance.Match, canCancel);
    }

    /// <summary>Requester, approvers and people with a decision in it see a request; otherwise <c>automation.read</c> or access to the document.</summary>
    public async Task<bool> CanSeeAsync(WorkflowInstance instance, CancellationToken cancellationToken)
    {
        if (tenant.Membership is { CustomerId: not null })
        {
            return false;
        }

        var me = tenant.Membership?.Id;
        return me is not null && (instance.RequestedById == me || instance.Decisions.Any(d => d.MembershipId == me))
               || tenant.HasAny(Permissions.AutomationRead, Permissions.AutomationManage)
               || tenant.HasAny(AutomationAccess.Read[instance.Document])
               || await audiences.CanDecideAsync(instance, tenant.Membership, cancellationToken);
    }
}

// ---- Lists --------------------------------------------------------------------------------------------------

public sealed record GetApprovalsQuery(ApprovalView View, WorkflowStatus? Status, AutomationDocument? Document, int Page = 1, int PageSize = 25)
    : IQuery<PagedResult<ApprovalSummaryDto>>;

internal sealed class GetApprovalsQueryHandler(IApplicationDbContext db, ITenantContext tenant, AudienceResolver audiences, ApprovalViews views)
    : IQueryHandler<GetApprovalsQuery, PagedResult<ApprovalSummaryDto>>
{
    public async Task<Result<PagedResult<ApprovalSummaryDto>>> Handle(GetApprovalsQuery query, CancellationToken cancellationToken)
    {
        var request = new PageRequest(null, query.Page, query.PageSize);
        var instances = db.WorkflowInstances.AsNoTracking().WhereIf(query.Document is not null, i => i.Document == query.Document);

        if (query.View == ApprovalView.Mine)
        {
            var pending = await instances.Where(i => i.Status == WorkflowStatus.Pending).OrderBy(i => i.RequestedAt).ToListAsync(cancellationToken);
            var mine = new List<WorkflowInstance>();
            foreach (var instance in pending)
            {
                if (await audiences.CanDecideAsync(instance, tenant.Membership, cancellationToken))
                {
                    mine.Add(instance);
                }
            }

            var page = mine.Skip((request.NormalizedPage - 1) * request.NormalizedPageSize).Take(request.NormalizedPageSize).ToList();
            return new PagedResult<ApprovalSummaryDto>(await SummariesAsync(page, cancellationToken), request.NormalizedPage, request.NormalizedPageSize, mine.Count);
        }

        if (query.View == ApprovalView.Requested)
        {
            var me = tenant.Membership?.Id;
            instances = instances.Where(i => i.RequestedById == me);
        }
        else if (!tenant.HasAny(Permissions.AutomationRead, Permissions.AutomationManage))
        {
            return Error.Forbidden("Workflow.Forbidden", "No tienes permiso para consultar todas las autorizaciones.");
        }

        var paged = await instances.WhereIf(query.Status is not null, i => i.Status == query.Status).OrderByDescending(i => i.RequestedAt).ToPagedAsync(request, cancellationToken);
        return new PagedResult<ApprovalSummaryDto>(await SummariesAsync(paged.Items, cancellationToken), paged.Page, paged.PageSize, paged.TotalCount);
    }

    private async Task<IReadOnlyList<ApprovalSummaryDto>> SummariesAsync(IEnumerable<WorkflowInstance> instances, CancellationToken cancellationToken)
    {
        var list = new List<ApprovalSummaryDto>();
        foreach (var instance in instances)
        {
            list.Add(await views.SummaryAsync(instance, cancellationToken));
        }

        return list;
    }
}

/// <summary>How many requests wait for the current user (badge of the menu and the home page).</summary>
public sealed record GetMyPendingApprovalsQuery : IQuery<int>;

internal sealed class GetMyPendingApprovalsQueryHandler(IApplicationDbContext db, ITenantContext tenant, AudienceResolver audiences) : IQueryHandler<GetMyPendingApprovalsQuery, int>
{
    public async Task<Result<int>> Handle(GetMyPendingApprovalsQuery query, CancellationToken cancellationToken)
    {
        if (tenant.Membership is null or { CustomerId: not null })
        {
            return 0;
        }

        var count = 0;
        foreach (var instance in await db.WorkflowInstances.AsNoTracking().Where(i => i.Status == WorkflowStatus.Pending).ToListAsync(cancellationToken))
        {
            if (await audiences.CanDecideAsync(instance, tenant.Membership, cancellationToken))
            {
                count++;
            }
        }

        return count;
    }
}

public sealed record GetApprovalQuery(Guid Id) : IQuery<ApprovalDetailDto>;

internal sealed class GetApprovalQueryHandler(IApplicationDbContext db, ApprovalViews views) : IQueryHandler<GetApprovalQuery, ApprovalDetailDto>
{
    public async Task<Result<ApprovalDetailDto>> Handle(GetApprovalQuery query, CancellationToken cancellationToken)
    {
        var instance = await db.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == query.Id, cancellationToken);
        if (instance is null || !await views.CanSeeAsync(instance, cancellationToken))
        {
            return WorkflowErrors.NotFound;
        }

        return await views.DetailAsync(instance, cancellationToken);
    }
}

public sealed record GetDocumentWorkflowQuery(AutomationDocument Document, Guid DocumentId) : IQuery<DocumentWorkflowDto>;

internal sealed class GetDocumentWorkflowQueryHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    DocumentLoader documents,
    AutomationDefinitions definitions,
    ApprovalViews views) : IQueryHandler<GetDocumentWorkflowQuery, DocumentWorkflowDto>
{
    public async Task<Result<DocumentWorkflowDto>> Handle(GetDocumentWorkflowQuery query, CancellationToken cancellationToken)
    {
        if (tenant.Membership is { CustomerId: not null } || !tenant.HasAny([.. AutomationAccess.Read[query.Document], Permissions.AutomationRead, Permissions.AutomationManage]))
        {
            return Error.Forbidden("Workflow.Forbidden", "No tienes acceso a las autorizaciones de este documento.");
        }

        var snapshot = await documents.LoadAsync(query.Document, query.DocumentId, cancellationToken);
        if (snapshot is null)
        {
            return WorkflowErrors.NotFound;
        }

        var instances = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Document == query.Document && i.DocumentId == query.DocumentId)
            .OrderByDescending(i => i.RequestedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        var latest = instances.FirstOrDefault();
        var applicable = await definitions.ApplicableWorkflowAsync(query.Document, snapshot.Facts, cancellationToken);
        var hash = RuleConditions.Hash(AutomationCatalog.Of(query.Document), snapshot.Facts);
        var authorized = latest is { Status: WorkflowStatus.Approved } && latest.FactsHash == hash;
        var canRequest = snapshot.AtGate && applicable is not null && latest is not { IsPending: true } && !authorized
                         && AutomationAccess.Request.TryGetValue(query.Document, out var request) && tenant.HasAny(request);

        var history = new List<ApprovalSummaryDto>();
        foreach (var instance in instances.Skip(1))
        {
            history.Add(await views.SummaryAsync(instance, cancellationToken));
        }

        return new DocumentWorkflowDto(query.Document, query.DocumentId, snapshot.AtGate, applicable is not null, applicable?.Name,
            latest is null ? null : await views.DetailAsync(latest, cancellationToken), history, canRequest);
    }
}

// ---- Decisions ----------------------------------------------------------------------------------------------

public sealed record DecideApprovalCommand(Guid Id, bool Approve, string? Comment) : ICommand<ApprovalDetailDto>;

internal sealed class DecideApprovalCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    ITenantClock clock,
    AudienceResolver audiences,
    DocumentLoader documents,
    IBusinessRules rules,
    ApprovalViews views) : ICommandHandler<DecideApprovalCommand, ApprovalDetailDto>
{
    public async Task<Result<ApprovalDetailDto>> Handle(DecideApprovalCommand command, CancellationToken cancellationToken)
    {
        var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken);
        if (instance is null)
        {
            return WorkflowErrors.NotFound;
        }

        if (instance.Step is not { } step)
        {
            return WorkflowErrors.NotPending;
        }

        if (tenant.Membership is not { } me || (await audiences.ApproversAsync(step, cancellationToken)).All(a => a.MembershipId != me.Id))
        {
            return WorkflowErrors.NotApprover;
        }

        if (!step.AllowRequester && instance.RequestedById == me.Id)
        {
            return WorkflowErrors.OwnRequest;
        }

        // The last approval of an "execute" workflow is the approval of the document: its rules apply.
        var completes = command.Approve && instance.CurrentStep == instance.Steps.Count - 1 && instance.ApprovalsInCurrentStep + 1 >= step.RequiredApprovals;
        if (completes && instance.Completion == WorkflowCompletion.Execute && AutomationCatalog.Of(instance.Document).Triggers.Contains(AutomationTrigger.Approve))
        {
            var snapshot = await documents.LoadAsync(instance.Document, instance.DocumentId, cancellationToken, applyVisibility: false);
            if (snapshot is not null)
            {
                var check = await rules.EnforceAsync(new RuleSubject(instance.Document, instance.DocumentId, instance.DocumentReference, snapshot.Facts), AutomationTrigger.Approve, cancellationToken);
                if (check.IsFailure)
                {
                    return check.Error;
                }
            }
        }

        var decided = instance.Decide(me.Id, me.FullName, command.Approve, command.Comment, clock.UtcNow);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await views.DetailAsync(instance, cancellationToken);
    }
}

public sealed record CancelApprovalCommand(Guid Id, string Reason) : ICommand;

internal sealed class CancelApprovalCommandHandler(IApplicationDbContext db, ITenantContext tenant, IUserContext user, ITenantClock clock) : ICommandHandler<CancelApprovalCommand>
{
    public async Task<Result> Handle(CancelApprovalCommand command, CancellationToken cancellationToken)
    {
        var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken);
        if (instance is null)
        {
            return WorkflowErrors.NotFound;
        }

        var me = tenant.Membership?.Id;
        if (!(me is not null && me == instance.RequestedById || tenant.Has(Permissions.AutomationManage)))
        {
            return WorkflowErrors.CannotCancel;
        }

        // Guid.Empty: a platform administrator without membership (the requester still hears about it).
        var cancelled = instance.Cancel(me ?? Guid.Empty, tenant.Membership?.FullName ?? user.DisplayName, command.Reason, clock.UtcNow);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Asks for the authorization of a document at its guarded moment without doing the action: an invoice before
/// stamping, a quote before sending it, or again after a rejection.
/// </summary>
public sealed record RequestApprovalCommand(AutomationDocument Document, Guid DocumentId) : ICommand<RequestApprovalResultDto>;

internal sealed class RequestApprovalCommandHandler(IApplicationDbContext db, ITenantContext tenant, DocumentLoader documents, IWorkflowEngine workflows)
    : ICommandHandler<RequestApprovalCommand, RequestApprovalResultDto>
{
    public async Task<Result<RequestApprovalResultDto>> Handle(RequestApprovalCommand command, CancellationToken cancellationToken)
    {
        if (!AutomationAccess.Request.TryGetValue(command.Document, out var permissions) || !tenant.HasAny(permissions))
        {
            return Error.Forbidden("Workflow.Forbidden", "No tienes permiso para solicitar la autorización de este documento.");
        }

        var snapshot = await documents.LoadAsync(command.Document, command.DocumentId, cancellationToken);
        if (snapshot is null)
        {
            return WorkflowErrors.NotFound;
        }

        if (!snapshot.AtGate)
        {
            return Error.Conflict("Workflow.NotAtGate", "El documento no está en un estado que requiera autorización.");
        }

        var subject = new WorkflowSubject(command.Document, command.DocumentId, snapshot.Reference, snapshot.Facts);
        if (AutomationCatalog.Of(command.Document).Workflow?.Completion == WorkflowCompletion.Unlock)
        {
            var gate = await workflows.GateAsync(subject, GatePersistence.CurrentUnitOfWork, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return gate.Passed
                ? new RequestApprovalResultDto(false, null, "El documento no requiere autorización o ya está autorizado.")
                : new RequestApprovalResultDto(true, gate.InstanceId, gate.Message!);
        }

        if (await workflows.PendingAsync(command.Document, command.DocumentId, cancellationToken) is { } pending)
        {
            return new RequestApprovalResultDto(true, pending.Id, WorkflowTexts.Waiting(pending));
        }

        var started = await workflows.StartAsync(subject, cancellationToken);
        if (started is null)
        {
            return WorkflowErrors.NotApplicable;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new RequestApprovalResultDto(true, started.Id, WorkflowTexts.Requested(started));
    }
}
