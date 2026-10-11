using System.Net;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Notifications;

/// <summary>
/// Resolves who must hear about an event (rule roles, assignees, extra addresses and the customer contacts that
/// opted in) and adds in-app notifications plus e-mail/WhatsApp outbox messages to the current unit of work.
/// </summary>
internal sealed class NotificationPublisher(IApplicationDbContext db, ITenantContext tenantContext, TimeProvider timeProvider) : INotificationPublisher
{
    /// <summary>Default audience when the company has no rule for the event yet.</summary>
    private static readonly Dictionary<NotificationEvent, string[]> DefaultRoleTemplates = new()
    {
        [NotificationEvent.TicketSigned] = ["office", "billing"],
        [NotificationEvent.TicketCancelled] = ["office", "billing"],
        [NotificationEvent.WorkOrderAssigned] = [],
        [NotificationEvent.UnitReleased] = ["office", "workshop"],
        [NotificationEvent.PermitExpiring] = ["workshop", "hse"],
        [NotificationEvent.DocumentExpiring] = ["hse"],
        [NotificationEvent.MaintenanceDue] = ["workshop"],
        [NotificationEvent.InvoiceStamped] = ["billing"],
        [NotificationEvent.InvoiceOverdue] = ["billing", "management"],
        [NotificationEvent.IncidentReported] = ["hse", "management"],
        [NotificationEvent.QuoteAccepted] = ["office"],
        [NotificationEvent.ExpenseSubmitted] = ["billing"],
        [NotificationEvent.FuelAnomaly] = ["workshop", "management"],
        [NotificationEvent.LowStock] = ["warehouse"],
        [NotificationEvent.UninvoicedTickets] = ["billing"]
    };

    public async Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        if (!tenantContext.HasTenant)
        {
            return;
        }

        var rule = await db.NotificationRules.AsNoTracking().FirstOrDefaultAsync(r => r.Event == message.Event, cancellationToken);
        if (rule is { IsEnabled: false })
        {
            return;
        }

        // Without the notifications module (e-mail/WhatsApp) only in-app notices are produced.
        var channels = rule?.Channels ?? NotificationChannels.InApp;
        if (!tenantContext.Tenant.IsModuleEnabled(BrocalModules.Notifications))
        {
            channels = NotificationChannels.InApp;
        }

        var memberships = await ResolveMembershipsAsync(rule, message, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (channels.HasFlag(NotificationChannels.InApp))
        {
            var alreadyNotified = message.DeduplicationKey is not null &&
                                  await db.UserNotifications.AnyAsync(n => n.DeduplicationKey == message.DeduplicationKey, cancellationToken);
            if (!alreadyNotified)
            {
                foreach (var membership in memberships)
                {
                    db.UserNotifications.Add(new UserNotification(membership.Id, message.Event, message.Severity, message.Title, message.Body, message.Link, now, message.DeduplicationKey));
                }
            }
        }

        if (channels.HasFlag(NotificationChannels.Email))
        {
            var emails = memberships.Select(m => m.Email).Concat(rule?.Emails ?? []).ToList();
            if (rule?.NotifyCustomerContacts == true && message.CustomerId is not null)
            {
                emails.AddRange(await CustomerContactsAsync(message, c => c.Email, cancellationToken));
            }

            foreach (var email in emails.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await EnqueueAsync(OutboxChannel.Email, email, message, now, cancellationToken);
            }
        }

        if (channels.HasFlag(NotificationChannels.WhatsApp))
        {
            var phones = (rule?.Phones ?? []).ToList();
            if (rule?.NotifyCustomerContacts == true && message.CustomerId is not null)
            {
                phones.AddRange(await CustomerContactsAsync(message, c => c.Phone, cancellationToken));
            }

            foreach (var phone in phones.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
            {
                await EnqueueAsync(OutboxChannel.WhatsApp, phone, message, now, cancellationToken);
            }
        }
    }

    private async Task<List<(Guid Id, string Email)>> ResolveMembershipsAsync(NotificationRule? rule, NotificationMessage message, CancellationToken cancellationToken)
    {
        var roleIds = rule?.RoleIds.ToList() ?? [];
        if (rule is null && DefaultRoleTemplates.TryGetValue(message.Event, out var templates) && templates.Length > 0)
        {
            roleIds = await db.Roles.AsNoTracking()
                .Where(r => r.TemplateKey != null && templates.Contains(r.TemplateKey))
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);
        }

        var active = await db.Memberships.AsNoTracking()
            .Where(m => m.Status == MembershipStatus.Active)
            .Select(m => new { m.Id, m.Email, m.RoleIds, m.IsTenantAdmin, m.CustomerId })
            .ToListAsync(cancellationToken);

        var assignees = rule?.NotifyAssignees ?? true ? message.AssigneeMembershipIds ?? [] : [];

        return active
            .Where(m => m.CustomerId is null)
            .Where(m => assignees.Contains(m.Id) || m.RoleIds.Any(roleIds.Contains) || rule is null && roleIds.Count == 0 && m.IsTenantAdmin && assignees.Count == 0)
            .Select(m => (m.Id, m.Email))
            .ToList();
    }

    private async Task<IEnumerable<string>> CustomerContactsAsync(NotificationMessage message, Func<Domain.Commercial.CustomerContact, string?> selector, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == message.CustomerId, cancellationToken);
        if (customer is null)
        {
            return [];
        }

        return customer.Contacts
            .Where(c => message.Event switch
            {
                NotificationEvent.TicketSigned or NotificationEvent.TicketCancelled => c.NotifyTickets,
                NotificationEvent.InvoiceStamped => c.NotifyInvoices,
                NotificationEvent.InvoiceOverdue => c.NotifyCollections,
                _ => false
            })
            .Select(selector)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!);
    }

    private async Task EnqueueAsync(OutboxChannel channel, string recipient, NotificationMessage message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = message.DeduplicationKey is null ? null : $"{message.DeduplicationKey}:{channel}:{recipient}";
        if (key is not null && await db.OutboxMessages.AnyAsync(o => o.DeduplicationKey == key, cancellationToken))
        {
            return;
        }

        var body = channel == OutboxChannel.Email ? EmailTemplate.Build(tenantContext.Tenant, message) : message.Body;
        db.OutboxMessages.Add(new OutboxMessage(channel, recipient, $"{tenantContext.Tenant.Branding.AppName} · {message.Title}", body, message.Event, key, now,
            attachmentFileId: message.AttachmentFileId));
    }
}

/// <summary>Simple branded HTML e-mail (inline styles for mail clients).</summary>
internal static class EmailTemplate
{
    public static string Build(TenantSnapshot tenant, NotificationMessage message)
    {
        var color = tenant.Branding.Palette.Primary;
        var accent = tenant.Branding.Palette.Tertiary;
        var name = WebUtility.HtmlEncode(tenant.Branding.AppName);
        var title = WebUtility.HtmlEncode(message.Title);
        var body = WebUtility.HtmlEncode(message.Body).Replace("\n", "<br>");

        return $"""
            <div style="font-family:Segoe UI,Roboto,Arial,sans-serif;max-width:600px;margin:0 auto;border:1px solid #e3e6ea;border-radius:12px;overflow:hidden">
              <div style="background:{color};color:#fff;padding:16px 20px;font-size:18px;font-weight:700">{name}</div>
              <div style="height:3px;background:{accent}"></div>
              <div style="padding:20px">
                <h2 style="margin:0 0 12px;font-size:18px;color:#1d2329">{title}</h2>
                <p style="margin:0;color:#3d4650;line-height:1.5">{body}</p>
              </div>
              <div style="padding:12px 20px;background:#f5f7f9;color:#6b747d;font-size:12px">Mensaje automático de Brocal · {WebUtility.HtmlEncode(tenant.Name)}</div>
            </div>
            """;
    }
}
