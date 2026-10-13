using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ChronoLith.Brocal.Application.Features.Automation;

/// <summary>A document at a moment of its life cycle, with the data the rules are tested on.</summary>
public sealed record RuleSubject(AutomationDocument Document, Guid DocumentId, string? Reference, IReadOnlyDictionary<string, object?> Facts);

/// <summary>Business rules of the company (and the global ones) at run time.</summary>
public interface IBusinessRules
{
    /// <summary>
    /// Evaluates the rules of the document for the moment. A matching blocking rule returns its message as a failure
    /// (its recipients are notified even though the operation does not happen); notifying rules add their notices to
    /// the current unit of work.
    /// </summary>
    Task<Result> EnforceAsync(RuleSubject subject, AutomationTrigger trigger, CancellationToken cancellationToken);
}

internal sealed class BusinessRuleEngine(
    ITenantContext tenant,
    IUserContext user,
    AutomationDefinitions definitions,
    INotificationPublisher notifications,
    AudienceResolver audiences,
    IIsolatedWriter isolated) : IBusinessRules
{
    public const string BlockedCode = "Rules.Blocked";

    public async Task<Result> EnforceAsync(RuleSubject subject, AutomationTrigger trigger, CancellationToken cancellationToken)
    {
        if (!tenant.HasTenant)
        {
            return Result.Success();
        }

        var matched = (await definitions.RulesAsync(subject.Document, trigger, cancellationToken))
            .Where(r => RuleConditions.Matches(r.Conditions, r.Match, subject.Facts))
            .ToList();
        if (matched.Count == 0)
        {
            return Result.Success();
        }

        var blocking = matched.Where(r => r.Effect == RuleEffect.Block).ToList();
        if (blocking.Count > 0)
        {
            var notify = blocking.Where(r => !r.Recipients.IsEmpty).ToList();
            if (notify.Count > 0)
            {
                // The operation is rejected (and may roll back): the notice of the attempt is saved on its own.
                var actor = tenant.Membership?.Id;
                var actorName = tenant.Membership?.FullName ?? user.DisplayName;
                await isolated.RunAsync(async (services, token) =>
                {
                    var publisher = services.GetRequiredService<INotificationPublisher>();
                    var resolver = services.GetRequiredService<AudienceResolver>();
                    foreach (var rule in notify)
                    {
                        await NotifyAsync(publisher, resolver, rule, subject, trigger, true, actor, actorName, token);
                    }
                }, cancellationToken);
            }

            return Error.Failure(BlockedCode, string.Join(" ", blocking.Select(r => r.Render(subject.Reference, subject.Facts))));
        }

        foreach (var rule in matched)
        {
            await NotifyAsync(notifications, audiences, rule, subject, trigger, false, tenant.Membership?.Id, tenant.Membership?.FullName ?? user.DisplayName, cancellationToken);
        }

        return Result.Success();
    }

    private static async Task NotifyAsync(
        INotificationPublisher publisher,
        AudienceResolver resolver,
        BusinessRule rule,
        RuleSubject subject,
        AutomationTrigger trigger,
        bool blocked,
        Guid? actor,
        string actorName,
        CancellationToken cancellationToken)
    {
        var recipients = (await resolver.ResolveAsync(rule.Recipients.Audience, cancellationToken)).Select(a => a.MembershipId).ToList();
        if (rule.Recipients.NotifyActor && actor is { } id)
        {
            recipients.Add(id);
        }

        if (recipients.Count == 0 && rule.Recipients.Emails.Count == 0)
        {
            return;
        }

        var document = AutomationCatalog.Of(subject.Document);
        var moment = AutomationCatalog.TriggerNames[trigger].Name.ToLowerInvariant();
        var reference = string.IsNullOrWhiteSpace(subject.Reference) ? document.Name : $"{document.Name} {subject.Reference}";
        var hash = RuleConditions.Hash(document, subject.Facts);

        await publisher.PublishAsync(new NotificationMessage(
            NotificationEvent.BusinessRuleAlert,
            blocked && rule.Severity < NotificationSeverity.Warning ? NotificationSeverity.Warning : rule.Severity,
            blocked ? $"Operación bloqueada: {rule.Name}" : rule.Name,
            $"{rule.Render(subject.Reference, subject.Facts)}\n{reference} · {moment} · {actorName}",
            document.LinkOf(subject.DocumentId),
            recipients.Distinct().ToList(),
            DeduplicationKey: $"rule:{rule.Id}:{subject.DocumentId}:{trigger}:{hash}",
            ExtraEmails: rule.Recipients.Emails), cancellationToken);
    }
}
