using System.Globalization;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Automation;

/// <summary>
/// Who hears about a request: the approvers of each step when it reaches them, and the requester when it is
/// approved, rejected or cancelled by someone else. Runs inside the unit of work that changed the request.
/// </summary>
internal sealed class WorkflowNotifier(IApplicationDbContext db, INotificationPublisher notifications, AudienceResolver audiences)
    : IDomainEventHandler<WorkflowRequestedEvent>, IDomainEventHandler<WorkflowStepAdvancedEvent>, IDomainEventHandler<WorkflowCompletedEvent>
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public Task Handle(WorkflowRequestedEvent domainEvent, CancellationToken cancellationToken) => NotifyApproversAsync(domainEvent.InstanceId, cancellationToken);

    public Task Handle(WorkflowStepAdvancedEvent domainEvent, CancellationToken cancellationToken) => NotifyApproversAsync(domainEvent.InstanceId, cancellationToken);

    public async Task Handle(WorkflowCompletedEvent domainEvent, CancellationToken cancellationToken)
    {
        var instance = await db.WorkflowInstances.FindAsync([domainEvent.InstanceId], cancellationToken);

        // Closed by the application (document cancelled, data changed) or by the requester: nobody else needs a notice.
        if (instance?.RequestedById is not { } requester || domainEvent.DecidedById is null || domainEvent.DecidedById == requester)
        {
            return;
        }

        var document = AutomationCatalog.Of(instance.Document);
        var (title, severity) = domainEvent.Status switch
        {
            WorkflowStatus.Approved => ($"{document.Name} {instance.DocumentReference} autorizada", NotificationSeverity.Success),
            WorkflowStatus.Rejected => ($"{document.Name} {instance.DocumentReference} rechazada", NotificationSeverity.Warning),
            _ => ($"Autorización cancelada: {document.Name} {instance.DocumentReference}", NotificationSeverity.Info)
        };

        var verb = domainEvent.Status switch
        {
            WorkflowStatus.Approved => "autorizó",
            WorkflowStatus.Rejected => "rechazó",
            _ => "canceló"
        };

        var body = $"{instance.DefinitionName}: la {verb} {domainEvent.DecidedBy}." +
                   (domainEvent.Comment is null ? string.Empty : $"\n{domainEvent.Comment}") +
                   (domainEvent.Status == WorkflowStatus.Approved && instance.Completion == WorkflowCompletion.Unlock ? $"\n{WorkflowTexts.Unlocked(instance.Document)}" : string.Empty);

        await notifications.PublishAsync(new NotificationMessage(
            NotificationEvent.ApprovalDecided,
            severity,
            title,
            body,
            document.LinkOf(instance.DocumentId),
            [requester],
            DeduplicationKey: $"wf-done:{instance.Id}"), cancellationToken);
    }

    private async Task NotifyApproversAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var instance = await db.WorkflowInstances.FindAsync([instanceId], cancellationToken);
        if (instance?.Step is not { } step)
        {
            return;
        }

        var approvers = (await audiences.ApproversAsync(step, cancellationToken))
            .Where(a => step.AllowRequester || a.MembershipId != instance.RequestedById)
            .Select(a => a.MembershipId)
            .ToList();
        if (approvers.Count == 0)
        {
            return;
        }

        var document = AutomationCatalog.Of(instance.Document);
        var amount = WorkflowTexts.Amount(instance.Facts) is { } value ? $" por {value.ToString("C2", Mx)}" : string.Empty;
        await notifications.PublishAsync(new NotificationMessage(
            NotificationEvent.ApprovalRequested,
            NotificationSeverity.Info,
            $"Autorización pendiente: {document.Name} {instance.DocumentReference}",
            $"{instance.DefinitionName} · {WorkflowTexts.StepLabel(instance)}.\nSolicitó {instance.RequestedByName}{amount}.",
            document.LinkOf(instance.DocumentId),
            approvers,
            DeduplicationKey: $"wf:{instance.Id}:{instance.CurrentStep}"), cancellationToken);
    }
}

/// <summary>"Execute" workflows: the decision of the last step is the transition of the document.</summary>
internal sealed class WorkflowCompletions(IApplicationDbContext db, ITenantClock clock, IAuditLog audit) : IDomainEventHandler<WorkflowCompletedEvent>
{
    public async Task Handle(WorkflowCompletedEvent domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.Status is not (WorkflowStatus.Approved or WorkflowStatus.Rejected))
        {
            return;
        }

        switch (domainEvent.Document)
        {
            case AutomationDocument.ExpenseReport:
            {
                var report = await db.ExpenseReports.FirstOrDefaultAsync(r => r.Id == domainEvent.DocumentId, cancellationToken);
                if (report is { Status: ExpenseStatus.Submitted })
                {
                    _ = domainEvent.Status == WorkflowStatus.Approved
                        ? report.Approve(domainEvent.DecidedBy, clock.UtcNow)
                        : report.Reject(domainEvent.DecidedBy, clock.UtcNow, domainEvent.Comment ?? "Rechazada en autorización.");
                }

                break;
            }

            case AutomationDocument.ServiceTicket when domainEvent.Status == WorkflowStatus.Approved:
            {
                var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == domainEvent.DocumentId, cancellationToken);
                if (ticket is { Status: TicketStatus.Signed } && ticket.Approve(domainEvent.DecidedBy, clock.UtcNow).IsSuccess)
                {
                    audit.Record(AuditAction.StatusChanged, nameof(ServiceTicket), ticket.Id, $"{ticket.Folio} → validada (flujo de autorización)");
                }

                break;
            }
        }
    }
}

/// <summary>Reminds the approvers of steps past their deadline; from the second deadline on, the company administrators too.</summary>
internal sealed class WorkflowReminders(IApplicationDbContext db, INotificationPublisher notifications, AudienceResolver audiences, ITenantClock clock)
{
    public async Task<int> RemindAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var pending = await db.WorkflowInstances.AsNoTracking().Where(i => i.Status == WorkflowStatus.Pending).ToListAsync(cancellationToken);
        var count = 0;

        foreach (var instance in pending)
        {
            if (instance.Step is not { SlaHours: { } sla } step)
            {
                continue;
            }

            var elapsed = (now - instance.StepStartedAt).TotalHours;
            if (elapsed < sla)
            {
                continue;
            }

            var round = (int)(elapsed / sla);
            var recipients = (await audiences.ApproversAsync(step, cancellationToken))
                .Where(a => (step.AllowRequester || a.MembershipId != instance.RequestedById) && !instance.HasDecided(a.MembershipId))
                .Select(a => a.MembershipId)
                .ToList();
            if (round >= 2)
            {
                recipients.AddRange((await audiences.AdministratorsAsync(cancellationToken)).Select(a => a.MembershipId));
            }

            if (recipients.Count == 0)
            {
                continue;
            }

            var document = AutomationCatalog.Of(instance.Document);
            await notifications.PublishAsync(new NotificationMessage(
                NotificationEvent.ApprovalRequested,
                NotificationSeverity.Warning,
                $"Autorización atrasada: {document.Name} {instance.DocumentReference}",
                $"{instance.DefinitionName} · {WorkflowTexts.StepLabel(instance)} lleva {elapsed:N0} h esperando (plazo de {sla} h). Solicitó {instance.RequestedByName}.",
                document.LinkOf(instance.DocumentId),
                recipients.Distinct().ToList(),
                DeduplicationKey: $"wf-sla:{instance.Id}:{instance.CurrentStep}:{round}"), cancellationToken);
            count++;
        }

        return count;
    }
}
