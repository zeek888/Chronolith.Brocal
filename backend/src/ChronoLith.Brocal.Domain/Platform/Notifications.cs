using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Platform;

/// <summary>Business events that can notify people.</summary>
public enum NotificationEvent
{
    TicketSigned = 0,
    TicketCancelled = 1,
    WorkOrderAssigned = 2,
    UnitReleased = 3,
    PermitExpiring = 4,
    DocumentExpiring = 5,
    MaintenanceDue = 6,
    InvoiceStamped = 7,
    InvoiceOverdue = 8,
    IncidentReported = 9,
    QuoteAccepted = 10,
    ExpenseSubmitted = 11,
    FuelAnomaly = 12,
    LowStock = 13,
    UninvoicedTickets = 14
}

[Flags]
public enum NotificationChannels
{
    None = 0,
    InApp = 1,
    Email = 2,
    WhatsApp = 4
}

public enum NotificationSeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Critical = 3
}

/// <summary>Who is notified of an event and through which channels (configured per company).</summary>
public sealed class NotificationRule : TenantAggregateRoot
{
    private List<Guid> _roleIds = [];
    private List<string> _emails = [];
    private List<string> _phones = [];

    private NotificationRule()
    {
    }

    private NotificationRule(NotificationEvent @event) => Event = @event;

    public NotificationEvent Event { get; private set; }

    public bool IsEnabled { get; private set; } = true;

    public NotificationChannels Channels { get; private set; } = NotificationChannels.InApp;

    /// <summary>Users holding any of these roles.</summary>
    public IReadOnlyList<Guid> RoleIds => _roleIds;

    /// <summary>Extra e-mail addresses (e.g. a shared mailbox of the office).</summary>
    public IReadOnlyList<string> Emails => _emails;

    /// <summary>Extra WhatsApp numbers in international format (+52…).</summary>
    public IReadOnlyList<string> Phones => _phones;

    /// <summary>Also notify the contacts of the customer that opted in (ticket signed, invoice stamped…).</summary>
    public bool NotifyCustomerContacts { get; private set; }

    /// <summary>Also notify the people assigned to the record (operator of the order, owner of the document).</summary>
    public bool NotifyAssignees { get; private set; } = true;

    /// <summary>Anticipation for expiry events (permits, documents, maintenance by date).</summary>
    public int? DaysBefore { get; private set; }

    public static NotificationRule Create(NotificationEvent @event, NotificationChannels channels, int? daysBefore = null)
    {
        var rule = new NotificationRule(@event) { Channels = channels, DaysBefore = daysBefore };
        return rule;
    }

    public Result Update(
        bool isEnabled,
        NotificationChannels channels,
        IEnumerable<Guid> roleIds,
        IEnumerable<string> emails,
        IEnumerable<string> phones,
        bool notifyCustomerContacts,
        bool notifyAssignees,
        int? daysBefore)
    {
        var mailList = new List<string>();
        foreach (var email in emails.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            var normalized = TenantMembership.NormalizeEmail(email);
            if (normalized is null)
            {
                return MembershipErrors.InvalidEmail;
            }

            mailList.Add(normalized);
        }

        var phoneList = new List<string>();
        foreach (var phone in phones.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var normalized = PhoneNumbers.Normalize(phone);
            if (normalized is null)
            {
                return NotificationErrors.InvalidPhone;
            }

            phoneList.Add(normalized);
        }

        if (daysBefore is < 0 or > 365)
        {
            return NotificationErrors.InvalidDays;
        }

        IsEnabled = isEnabled;
        Channels = channels;
        _roleIds = roleIds.Distinct().ToList();
        _emails = mailList.Distinct().ToList();
        _phones = phoneList.Distinct().ToList();
        NotifyCustomerContacts = notifyCustomerContacts;
        NotifyAssignees = notifyAssignees;
        DaysBefore = daysBefore;

        return Result.Success();
    }

    public void RemoveRole(Guid roleId) => _roleIds.Remove(roleId);
}

/// <summary>In-app notification of one user of a company (bell of the toolbar).</summary>
public sealed class UserNotification : Entity, ITenantOwned
{
    private UserNotification()
    {
    }

    public UserNotification(
        Guid membershipId,
        NotificationEvent @event,
        NotificationSeverity severity,
        string title,
        string body,
        string? link,
        DateTimeOffset createdAt,
        string? deduplicationKey = null)
    {
        MembershipId = membershipId;
        Event = @event;
        Severity = severity;
        Title = title.Length > 200 ? title[..200] : title;
        Body = body.Length > 2000 ? body[..2000] : body;
        Link = link;
        CreatedAt = createdAt;
        DeduplicationKey = deduplicationKey;
    }

    /// <summary>Same reminder is not repeated (expiry scans run every day).</summary>
    public string? DeduplicationKey { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid MembershipId { get; private set; }

    public NotificationEvent Event { get; private set; }

    public NotificationSeverity Severity { get; private set; }

    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    /// <summary>Route inside Brocal (<c>boletas/…</c>).</summary>
    public string? Link { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkAsRead(DateTimeOffset now) => ReadAt ??= now;
}

public enum OutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Simulated = 3
}

public enum OutboxChannel
{
    Email = 0,
    WhatsApp = 1
}

/// <summary>
/// E-mail or WhatsApp message waiting to be delivered (transactional outbox): it is saved with the business change
/// and a background worker delivers it with retries.
/// </summary>
public sealed class OutboxMessage : Entity, ITenantOwned
{
    public const int MaxAttempts = 6;

    private OutboxMessage()
    {
    }

    public OutboxMessage(
        OutboxChannel channel,
        string recipient,
        string subject,
        string body,
        NotificationEvent @event,
        string? deduplicationKey,
        DateTimeOffset createdAt,
        string? templateName = null,
        string? templateParameters = null,
        Guid? attachmentFileId = null)
    {
        Channel = channel;
        Recipient = recipient;
        Subject = subject.Length > 250 ? subject[..250] : subject;
        Body = body;
        Event = @event;
        DeduplicationKey = deduplicationKey;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
        TemplateName = templateName;
        TemplateParameters = templateParameters;
        AttachmentFileId = attachmentFileId;
    }

    public Guid TenantId { get; private set; }

    public OutboxChannel Channel { get; private set; }

    public string Recipient { get; private set; } = null!;

    public string Subject { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    public NotificationEvent Event { get; private set; }

    /// <summary>Prevents sending the same reminder twice (<c>permit:{id}:{date}:{recipient}</c>).</summary>
    public string? DeduplicationKey { get; private set; }

    /// <summary>WhatsApp template (business-initiated messages require an approved template).</summary>
    public string? TemplateName { get; private set; }

    /// <summary>JSON array with the template body parameters.</summary>
    public string? TemplateParameters { get; private set; }

    public Guid? AttachmentFileId { get; private set; }

    public OutboxStatus Status { get; private set; } = OutboxStatus.Pending;

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string? LastError { get; private set; }

    public void MarkSent(DateTimeOffset now, bool simulated)
    {
        Status = simulated ? OutboxStatus.Simulated : OutboxStatus.Sent;
        SentAt = now;
        Attempts++;
        LastError = null;
    }

    /// <summary>Exponential back-off (1, 2, 4, 8… minutes) until <see cref="MaxAttempts"/>.</summary>
    public void MarkFailed(DateTimeOffset now, string error)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        if (Attempts >= MaxAttempts)
        {
            Status = OutboxStatus.Failed;
            return;
        }

        NextAttemptAt = now.AddMinutes(Math.Pow(2, Attempts - 1));
    }

    public void Retry(DateTimeOffset now)
    {
        Status = OutboxStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
    }
}

/// <summary>E-mail (SMTP) and WhatsApp Cloud API accounts of a company. Secrets are stored encrypted.</summary>
public sealed class NotificationChannelSettings : TenantAggregateRoot
{
    private NotificationChannelSettings()
    {
    }

    public static NotificationChannelSettings CreateDefault() => new();

    /// <summary>When false the platform SMTP account is used.</summary>
    public bool UseOwnSmtp { get; private set; }

    public string? SmtpHost { get; private set; }

    public int SmtpPort { get; private set; } = 587;

    public bool SmtpUseSsl { get; private set; } = true;

    public string? SmtpUser { get; private set; }

    public string? SmtpPasswordProtected { get; private set; }

    public string? FromAddress { get; private set; }

    public string? FromName { get; private set; }

    public bool WhatsAppEnabled { get; private set; }

    /// <summary>Phone number id of the WhatsApp Business account (Meta Cloud API).</summary>
    public string? WhatsAppPhoneNumberId { get; private set; }

    public string? WhatsAppTokenProtected { get; private set; }

    /// <summary>Approved template used for every business-initiated message (body with {{1}} title and {{2}} detail).</summary>
    public string WhatsAppTemplate { get; private set; } = "brocal_aviso";

    public string WhatsAppLanguage { get; private set; } = "es_MX";

    public Result UpdateEmail(bool useOwnSmtp, string? host, int port, bool useSsl, string? user, string? passwordProtected, string? fromAddress, string? fromName)
    {
        if (useOwnSmtp && (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65535))
        {
            return NotificationErrors.InvalidSmtp;
        }

        var from = string.IsNullOrWhiteSpace(fromAddress) ? null : TenantMembership.NormalizeEmail(fromAddress);
        if (!string.IsNullOrWhiteSpace(fromAddress) && from is null)
        {
            return MembershipErrors.InvalidEmail;
        }

        UseOwnSmtp = useOwnSmtp;
        SmtpHost = host?.Trim();
        SmtpPort = port;
        SmtpUseSsl = useSsl;
        SmtpUser = user?.Trim();
        if (passwordProtected is not null)
        {
            SmtpPasswordProtected = passwordProtected;
        }

        FromAddress = from;
        FromName = string.IsNullOrWhiteSpace(fromName) ? null : fromName.Trim();
        return Result.Success();
    }

    public Result UpdateWhatsApp(bool enabled, string? phoneNumberId, string? tokenProtected, string template, string language)
    {
        if (enabled && string.IsNullOrWhiteSpace(phoneNumberId))
        {
            return NotificationErrors.InvalidWhatsApp;
        }

        WhatsAppEnabled = enabled;
        WhatsAppPhoneNumberId = phoneNumberId?.Trim();
        if (tokenProtected is not null)
        {
            WhatsAppTokenProtected = tokenProtected;
        }

        WhatsAppTemplate = string.IsNullOrWhiteSpace(template) ? "brocal_aviso" : template.Trim();
        WhatsAppLanguage = string.IsNullOrWhiteSpace(language) ? "es_MX" : language.Trim();
        return Result.Success();
    }
}

public static class PhoneNumbers
{
    /// <summary>International format without spaces (+5299312345678). 10-digit Mexican numbers get +52.</summary>
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 10)
        {
            digits = "52" + digits;
        }

        return digits.Length is >= 11 and <= 15 ? "+" + digits : null;
    }
}

public static class NotificationErrors
{
    public static readonly Error RuleNotFound = Error.NotFound("Notifications.RuleNotFound", "La regla de notificación no existe.");

    public static readonly Error InvalidPhone = Error.Validation("Notifications.InvalidPhone", "El teléfono debe tener 10 dígitos o formato internacional (+52…).");

    public static readonly Error InvalidDays = Error.Validation("Notifications.InvalidDays", "La anticipación va de 0 a 365 días.");

    public static readonly Error InvalidSmtp = Error.Validation("Notifications.InvalidSmtp", "Indica el servidor y el puerto SMTP.");

    public static readonly Error InvalidWhatsApp = Error.Validation("Notifications.InvalidWhatsApp", "Indica el identificador del número de WhatsApp Business.");
}
