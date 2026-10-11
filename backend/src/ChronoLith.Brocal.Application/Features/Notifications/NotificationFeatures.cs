using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Notifications;

// ---- My notifications (bell) ----------------------------------------------------------------------------

public sealed record UserNotificationDto(Guid Id, NotificationEvent Event, NotificationSeverity Severity, string Title, string Body, string? Link, DateTimeOffset CreatedAt, bool IsRead);

public sealed record MyNotificationsDto(int Unread, IReadOnlyList<UserNotificationDto> Items);

public sealed record GetMyNotificationsQuery(bool UnreadOnly, int Take = 30) : IQuery<MyNotificationsDto>;

internal sealed class GetMyNotificationsQueryHandler(IApplicationDbContext db, ITenantContext tenant) : IQueryHandler<GetMyNotificationsQuery, MyNotificationsDto>
{
    public async Task<Result<MyNotificationsDto>> Handle(GetMyNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (tenant.Membership is not { } membership)
        {
            return new MyNotificationsDto(0, []);
        }

        var mine = db.UserNotifications.AsNoTracking().Where(n => n.MembershipId == membership.Id);
        var unread = await mine.CountAsync(n => n.ReadAt == null, cancellationToken);
        var items = await mine.WhereIf(query.UnreadOnly, n => n.ReadAt == null)
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(query.Take, 1, 100))
            .Select(n => new UserNotificationDto(n.Id, n.Event, n.Severity, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(cancellationToken);
        return new MyNotificationsDto(unread, items);
    }
}

public sealed record MarkNotificationsReadCommand(IReadOnlyList<Guid>? Ids) : ICommand<int>;

internal sealed class MarkNotificationsReadCommandHandler(IApplicationDbContext db, ITenantContext tenant, TimeProvider timeProvider) : ICommandHandler<MarkNotificationsReadCommand, int>
{
    public async Task<Result<int>> Handle(MarkNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        if (tenant.Membership is not { } membership)
        {
            return 0;
        }

        var notifications = await db.UserNotifications
            .Where(n => n.MembershipId == membership.Id && n.ReadAt == null)
            .WhereIf(command.Ids is { Count: > 0 }, n => command.Ids!.Contains(n.Id))
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        notifications.ForEach(n => n.MarkAsRead(now));
        await db.SaveChangesAsync(cancellationToken);
        return notifications.Count;
    }
}

// ---- Rules ----------------------------------------------------------------------------------------------

public sealed record NotificationRuleDto(
    NotificationEvent Event, Guid? Id, bool IsEnabled, NotificationChannels Channels, IReadOnlyList<Guid> RoleIds, IReadOnlyList<string> Emails, IReadOnlyList<string> Phones,
    bool NotifyCustomerContacts, bool NotifyAssignees, int? DaysBefore, bool IsDefault);

/// <summary>Every event with its configured rule, or the default behavior when the company has not configured it.</summary>
public sealed record GetNotificationRulesQuery : IQuery<IReadOnlyList<NotificationRuleDto>>;

internal sealed class GetNotificationRulesQueryHandler(IApplicationDbContext db) : IQueryHandler<GetNotificationRulesQuery, IReadOnlyList<NotificationRuleDto>>
{
    public async Task<Result<IReadOnlyList<NotificationRuleDto>>> Handle(GetNotificationRulesQuery query, CancellationToken cancellationToken)
    {
        var rules = await db.NotificationRules.AsNoTracking().ToDictionaryAsync(r => r.Event, cancellationToken);
        return Enum.GetValues<NotificationEvent>().Select(e => rules.TryGetValue(e, out var r)
                ? new NotificationRuleDto(e, r.Id, r.IsEnabled, r.Channels, r.RoleIds, r.Emails, r.Phones, r.NotifyCustomerContacts, r.NotifyAssignees, r.DaysBefore, false)
                : new NotificationRuleDto(e, null, true, NotificationChannels.InApp, [], [], [], false, true, null, true))
            .ToList();
    }
}

public sealed record SaveNotificationRuleCommand(
    NotificationEvent Event, bool IsEnabled, NotificationChannels Channels, IReadOnlyList<Guid> RoleIds, IReadOnlyList<string> Emails, IReadOnlyList<string> Phones,
    bool NotifyCustomerContacts, bool NotifyAssignees, int? DaysBefore) : ICommand;

internal sealed class SaveNotificationRuleCommandHandler(IApplicationDbContext db) : ICommandHandler<SaveNotificationRuleCommand>
{
    public async Task<Result> Handle(SaveNotificationRuleCommand command, CancellationToken cancellationToken)
    {
        var roles = command.RoleIds.Distinct().ToList();
        if (roles.Count > 0 && await db.Roles.CountAsync(r => roles.Contains(r.Id), cancellationToken) != roles.Count)
        {
            return RoleErrors.NotFound;
        }

        var rule = await db.NotificationRules.FirstOrDefaultAsync(r => r.Event == command.Event, cancellationToken);
        if (rule is null)
        {
            rule = NotificationRule.Create(command.Event, command.Channels, command.DaysBefore);
            db.NotificationRules.Add(rule);
        }

        var result = rule.Update(command.IsEnabled, command.Channels, roles, command.Emails, command.Phones, command.NotifyCustomerContacts, command.NotifyAssignees, command.DaysBefore);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

// ---- Channels (SMTP / WhatsApp) -------------------------------------------------------------------------

public sealed record ChannelSettingsDto(
    bool UseOwnSmtp, string? SmtpHost, int SmtpPort, bool SmtpUseSsl, string? SmtpUser, bool HasSmtpPassword, string? FromAddress, string? FromName,
    bool WhatsAppEnabled, string? WhatsAppPhoneNumberId, bool HasWhatsAppToken, string WhatsAppTemplate, string WhatsAppLanguage);

public sealed record GetChannelSettingsQuery : IQuery<ChannelSettingsDto>;

internal sealed class GetChannelSettingsQueryHandler(IApplicationDbContext db) : IQueryHandler<GetChannelSettingsQuery, ChannelSettingsDto>
{
    public async Task<Result<ChannelSettingsDto>> Handle(GetChannelSettingsQuery query, CancellationToken cancellationToken)
    {
        var s = await db.NotificationChannels.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? NotificationChannelSettings.CreateDefault();
        return new ChannelSettingsDto(s.UseOwnSmtp, s.SmtpHost, s.SmtpPort, s.SmtpUseSsl, s.SmtpUser, !string.IsNullOrEmpty(s.SmtpPasswordProtected), s.FromAddress, s.FromName,
            s.WhatsAppEnabled, s.WhatsAppPhoneNumberId, !string.IsNullOrEmpty(s.WhatsAppTokenProtected), s.WhatsAppTemplate, s.WhatsAppLanguage);
    }
}

/// <summary>Secrets travel in plain text only on update and are stored encrypted; null keeps the stored one.</summary>
public sealed record SaveChannelSettingsCommand(
    bool UseOwnSmtp, string? SmtpHost, int SmtpPort, bool SmtpUseSsl, string? SmtpUser, string? SmtpPassword, string? FromAddress, string? FromName,
    bool WhatsAppEnabled, string? WhatsAppPhoneNumberId, string? WhatsAppToken, string WhatsAppTemplate, string WhatsAppLanguage) : ICommand;

internal sealed class SaveChannelSettingsCommandHandler(IApplicationDbContext db, ISecretProtector protector) : ICommandHandler<SaveChannelSettingsCommand>
{
    public async Task<Result> Handle(SaveChannelSettingsCommand command, CancellationToken cancellationToken)
    {
        var settings = await db.NotificationChannels.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = NotificationChannelSettings.CreateDefault();
            db.NotificationChannels.Add(settings);
        }

        var email = settings.UpdateEmail(command.UseOwnSmtp, command.SmtpHost, command.SmtpPort, command.SmtpUseSsl, command.SmtpUser,
            string.IsNullOrEmpty(command.SmtpPassword) ? null : protector.Protect(command.SmtpPassword), command.FromAddress, command.FromName);
        if (email.IsFailure)
        {
            return email;
        }

        var whatsapp = settings.UpdateWhatsApp(command.WhatsAppEnabled, command.WhatsAppPhoneNumberId,
            string.IsNullOrEmpty(command.WhatsAppToken) ? null : protector.Protect(command.WhatsAppToken), command.WhatsAppTemplate, command.WhatsAppLanguage);
        if (whatsapp.IsFailure)
        {
            return whatsapp;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---- Outbox monitor -------------------------------------------------------------------------------------

public sealed record OutboxMessageDto(Guid Id, OutboxChannel Channel, string Recipient, string Subject, NotificationEvent Event, OutboxStatus Status, int Attempts,
    DateTimeOffset CreatedAt, DateTimeOffset? SentAt, string? LastError);

public sealed record SearchOutboxQuery(OutboxStatus? Status, OutboxChannel? Channel, int Page = 1, int PageSize = 50) : IQuery<PagedResult<OutboxMessageDto>>;

internal sealed class SearchOutboxQueryHandler(IApplicationDbContext db) : IQueryHandler<SearchOutboxQuery, PagedResult<OutboxMessageDto>>
{
    public async Task<Result<PagedResult<OutboxMessageDto>>> Handle(SearchOutboxQuery query, CancellationToken cancellationToken) =>
        await db.OutboxMessages.AsNoTracking()
            .WhereIf(query.Status is not null, o => o.Status == query.Status)
            .WhereIf(query.Channel is not null, o => o.Channel == query.Channel)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OutboxMessageDto(o.Id, o.Channel, o.Recipient, o.Subject, o.Event, o.Status, o.Attempts, o.CreatedAt, o.SentAt, o.LastError))
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);
}

public sealed record RetryOutboxCommand(Guid Id) : ICommand;

internal sealed class RetryOutboxCommandHandler(IApplicationDbContext db, TimeProvider timeProvider) : ICommandHandler<RetryOutboxCommand>
{
    public async Task<Result> Handle(RetryOutboxCommand command, CancellationToken cancellationToken)
    {
        var message = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
        if (message is null)
        {
            return Error.NotFound("Outbox.NotFound", "El mensaje no existe.");
        }

        message.Retry(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Sends a test notification to the current user through every configured channel.</summary>
public sealed record SendTestNotificationCommand(string? Email, string? Phone) : ICommand;

internal sealed class SendTestNotificationCommandHandler(IApplicationDbContext db, ITenantContext tenant, TimeProvider timeProvider) : ICommandHandler<SendTestNotificationCommand>
{
    public async Task<Result> Handle(SendTestNotificationCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (tenant.Membership is { } membership)
        {
            db.UserNotifications.Add(new UserNotification(membership.Id, NotificationEvent.TicketSigned, NotificationSeverity.Info, "Notificación de prueba",
                "Si ves este aviso, las notificaciones en la aplicación funcionan.", null, now));
        }

        var email = TenantMembership.NormalizeEmail(command.Email ?? tenant.Membership?.Email);
        if (email is not null)
        {
            db.OutboxMessages.Add(new OutboxMessage(OutboxChannel.Email, email, $"{tenant.Tenant.Branding.AppName} · Prueba de correo",
                EmailTemplate.Build(tenant.Tenant, new NotificationMessage(NotificationEvent.TicketSigned, NotificationSeverity.Info, "Prueba de correo", "El canal de correo de Brocal está configurado.", null)),
                NotificationEvent.TicketSigned, null, now));
        }

        if (PhoneNumbers.Normalize(command.Phone) is { } phone)
        {
            db.OutboxMessages.Add(new OutboxMessage(OutboxChannel.WhatsApp, phone, "Prueba", "El canal de WhatsApp de Brocal está configurado.", NotificationEvent.TicketSigned, null, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
