using System.Globalization;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChronoLith.Brocal.Application.Features.Automation;

/// <summary>A document at a guarded moment of its life cycle, with the data the conditions are tested on.</summary>
public sealed record WorkflowSubject(AutomationDocument Document, Guid DocumentId, string Reference, IReadOnlyDictionary<string, object?> Facts);

/// <summary>Where a held action registers its approval request.</summary>
public enum GatePersistence
{
    /// <summary>In its own unit of work: the caller fails (and may roll back) but the request stays.</summary>
    Isolated = 0,

    /// <summary>In the caller's unit of work: the caller goes on (e.g. saves the draft) and saves it.</summary>
    CurrentUnitOfWork = 1
}

public sealed record WorkflowGateResult(bool Passed, Guid? InstanceId, string? Message)
{
    public static WorkflowGateResult Pass { get; } = new(true, null, null);

    public Error ToError() => new ApprovalPendingError(InstanceId ?? Guid.Empty, Message ?? string.Empty);
}

/// <summary>
/// Approval workflows at run time. "Unlock" documents (dispatch, send, stamp, close) pass their action through
/// <see cref="GateAsync"/>; "execute" documents (expense reports, tickets) start their request with
/// <see cref="StartAsync"/> and are approved or rejected when the last step decides.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Passes when no workflow applies or an approval was given for the same data; otherwise registers the request
    /// (or keeps the one already pending) and answers with the message for the user.
    /// </summary>
    Task<WorkflowGateResult> GateAsync(WorkflowSubject subject, GatePersistence persistence, CancellationToken cancellationToken);

    /// <summary>Starts the request of an "execute" document in the current unit of work; null when no workflow applies.</summary>
    Task<WorkflowInstance?> StartAsync(WorkflowSubject subject, CancellationToken cancellationToken);

    Task<WorkflowInstance?> PendingAsync(AutomationDocument document, Guid documentId, CancellationToken cancellationToken);

    /// <summary>Status of the latest request of each document (documents without requests are not returned).</summary>
    Task<IReadOnlyDictionary<Guid, WorkflowStatus>> LatestStatusesAsync(AutomationDocument document, IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken);

    /// <summary>The document was cancelled, reopened or deleted: its pending request is closed (current unit of work).</summary>
    Task CancelPendingAsync(AutomationDocument document, Guid documentId, string reason, CancellationToken cancellationToken);
}

internal sealed class WorkflowEngine(
    IApplicationDbContext db,
    ITenantContext tenant,
    IUserContext user,
    ITenantClock clock,
    AutomationDefinitions definitions,
    IIsolatedWriter isolated) : IWorkflowEngine
{
    public async Task<WorkflowGateResult> GateAsync(WorkflowSubject subject, GatePersistence persistence, CancellationToken cancellationToken)
    {
        if (!tenant.HasTenant)
        {
            return WorkflowGateResult.Pass;
        }

        var definition = await definitions.ApplicableWorkflowAsync(subject.Document, subject.Facts, cancellationToken);
        var requests = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Document == subject.Document && i.DocumentId == subject.DocumentId)
            .OrderByDescending(i => i.RequestedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        var pending = requests.Where(i => i.IsPending).ToList();

        if (definition is null)
        {
            await CancelAsync(pending.Select(p => p.Id).ToList(), WorkflowTexts.NoLongerApplies, persistence, cancellationToken);
            return WorkflowGateResult.Pass;
        }

        var hash = RuleConditions.Hash(AutomationCatalog.Of(subject.Document), subject.Facts);
        if (requests.FirstOrDefault() is { Status: WorkflowStatus.Approved } approved && approved.FactsHash == hash)
        {
            return WorkflowGateResult.Pass;
        }

        var current = pending.FirstOrDefault(p => p.FactsHash == hash && p.DefinitionId == definition.Id);
        if (current is not null)
        {
            // Two requests registered at the same time: only the newest one stays.
            await CancelAsync(pending.Where(p => p.Id != current.Id).Select(p => p.Id).ToList(), WorkflowTexts.Duplicated, persistence, cancellationToken);
            return new WorkflowGateResult(false, current.Id, WorkflowTexts.Waiting(current));
        }

        var instance = WorkflowInstance.Request(definition, subject.DocumentId, subject.Reference, subject.Facts, tenant.Membership?.Id, RequesterName, clock.UtcNow);
        var stale = pending.Select(p => p.Id).ToList();
        if (persistence == GatePersistence.Isolated)
        {
            await isolated.RunAsync(async (services, token) =>
            {
                var scoped = services.GetRequiredService<IApplicationDbContext>();
                await CancelInAsync(scoped, stale, WorkflowTexts.DataChanged, token);
                scoped.WorkflowInstances.Add(instance);
            }, cancellationToken);
        }
        else
        {
            await CancelInAsync(db, stale, WorkflowTexts.DataChanged, cancellationToken);
            db.WorkflowInstances.Add(instance);
        }

        return new WorkflowGateResult(false, instance.Id, WorkflowTexts.Requested(instance));
    }

    public async Task<WorkflowInstance?> StartAsync(WorkflowSubject subject, CancellationToken cancellationToken)
    {
        if (!tenant.HasTenant)
        {
            return null;
        }

        var stale = await db.WorkflowInstances
            .Where(i => i.Document == subject.Document && i.DocumentId == subject.DocumentId && i.Status == WorkflowStatus.Pending)
            .ToListAsync(cancellationToken);
        stale.ForEach(i => i.Cancel(null, WorkflowTexts.System, WorkflowTexts.RequestedAgain, clock.UtcNow));

        var definition = await definitions.ApplicableWorkflowAsync(subject.Document, subject.Facts, cancellationToken);
        if (definition is null)
        {
            return null;
        }

        var instance = WorkflowInstance.Request(definition, subject.DocumentId, subject.Reference, subject.Facts, tenant.Membership?.Id, RequesterName, clock.UtcNow);
        db.WorkflowInstances.Add(instance);
        return instance;
    }

    public Task<WorkflowInstance?> PendingAsync(AutomationDocument document, Guid documentId, CancellationToken cancellationToken) =>
        db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Document == document && i.DocumentId == documentId && i.Status == WorkflowStatus.Pending)
            .OrderByDescending(i => i.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, WorkflowStatus>> LatestStatusesAsync(AutomationDocument document, IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return new Dictionary<Guid, WorkflowStatus>();
        }

        var ids = documentIds.ToList();
        var rows = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Document == document && ids.Contains(i.DocumentId))
            .Select(i => new { i.DocumentId, i.Status, i.RequestedAt })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.DocumentId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RequestedAt).First().Status);
    }

    public async Task CancelPendingAsync(AutomationDocument document, Guid documentId, string reason, CancellationToken cancellationToken)
    {
        var pending = await db.WorkflowInstances
            .Where(i => i.Document == document && i.DocumentId == documentId && i.Status == WorkflowStatus.Pending)
            .ToListAsync(cancellationToken);
        pending.ForEach(i => i.Cancel(null, WorkflowTexts.System, reason, clock.UtcNow));
    }

    private string RequesterName => tenant.Membership?.FullName ?? user.DisplayName;

    private async Task CancelAsync(IReadOnlyList<Guid> ids, string reason, GatePersistence persistence, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        if (persistence == GatePersistence.Isolated)
        {
            await isolated.RunAsync((services, token) => CancelInAsync(services.GetRequiredService<IApplicationDbContext>(), ids, reason, token), cancellationToken);
        }
        else
        {
            await CancelInAsync(db, ids, reason, cancellationToken);
        }
    }

    private async Task CancelInAsync(IApplicationDbContext context, IReadOnlyList<Guid> ids, string reason, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var instances = await context.WorkflowInstances.Where(i => ids.Contains(i.Id) && i.Status == WorkflowStatus.Pending).ToListAsync(cancellationToken);
        instances.ForEach(i => i.Cancel(null, WorkflowTexts.System, reason, clock.UtcNow));
    }
}

/// <summary>Texts of the requests (Spanish, like the rest of the notifications of the API).</summary>
internal static class WorkflowTexts
{
    /// <summary>Name recorded when the application closes a request by itself.</summary>
    public const string System = "Sistema";

    public const string NoLongerApplies = "El flujo de autorización ya no aplica al documento.";

    public const string DataChanged = "Los datos del documento cambiaron: se pidió una nueva autorización.";

    public const string Duplicated = "Solicitud duplicada.";

    public const string RequestedAgain = "Se volvió a solicitar la autorización.";

    public static string StepLabel(WorkflowInstance instance) =>
        instance.Step is { } step ? $"paso {instance.CurrentStep + 1} de {instance.Steps.Count}: {step.Name}" : string.Empty;

    public static string Requested(WorkflowInstance instance) => $"Se envió a autorización ({instance.DefinitionName}, {StepLabel(instance)}).";

    public static string Waiting(WorkflowInstance instance) => $"Pendiente de autorización ({instance.DefinitionName}, {StepLabel(instance)}).";

    /// <summary>What the requester can do once an "unlock" workflow is approved.</summary>
    public static string Unlocked(AutomationDocument document) => document switch
    {
        AutomationDocument.WorkOrder => "Ya se puede despachar.",
        AutomationDocument.Quote => "Ya se puede enviar al cliente.",
        AutomationDocument.Invoice => "Ya se puede timbrar.",
        AutomationDocument.MaintenanceOrder => "Ya se puede cerrar.",
        _ => string.Empty
    };

    /// <summary>Amount of the request for messages and lists (total, cost or amount, whichever the document has).</summary>
    public static decimal? Amount(IReadOnlyDictionary<string, string?> facts)
    {
        foreach (var key in new[] { "total", "totalCost", "amount" })
        {
            if (facts.TryGetValue(key, out var text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
