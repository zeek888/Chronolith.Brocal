using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.Automation;

/// <summary>
/// People of a company chosen by role or permission. Global definitions (every company) can only use role
/// templates (<see cref="RoleTemplates"/>: "billing", "management"…), permissions and company administrators;
/// company definitions can also name their own roles.
/// </summary>
public sealed record Audience(IReadOnlyList<string> RoleTemplates, IReadOnlyList<Guid> RoleIds, IReadOnlyList<string> Permissions, bool TenantAdmins)
{
    public static Audience Empty { get; } = new([], [], [], false);

    public bool IsEmpty => RoleTemplates.Count == 0 && RoleIds.Count == 0 && Permissions.Count == 0 && !TenantAdmins;

    public Result<Audience> Normalize(bool isGlobal)
    {
        var templates = (RoleTemplates ?? []).Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (templates.Any(t => Platform.RoleTemplates.Find(t) is null))
        {
            return AutomationErrors.UnknownRoleTemplate;
        }

        var permissions = (Permissions ?? []).Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (permissions.Any(p => !BrocalModules.IsPermission(p)))
        {
            return AutomationErrors.UnknownPermission;
        }

        var roles = (RoleIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (isGlobal && roles.Count > 0)
        {
            return AutomationErrors.CompanyRolesInGlobal;
        }

        return new Audience(templates, roles, permissions, TenantAdmins);
    }
}

/// <summary>
/// One approval level: who approves, how many distinct approvals it needs, the time after which the approvers
/// are reminded, and whether the person who requested it may approve it too.
/// </summary>
public sealed record WorkflowStep(string Name, Audience Approvers, int RequiredApprovals, int? SlaHours, bool AllowRequester)
{
    public const int MaxSteps = 6;

    public Result<WorkflowStep> Normalize(bool isGlobal)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 80 || RequiredApprovals is < 1 or > 5 || SlaHours is < 1 or > 720)
        {
            return AutomationErrors.InvalidSteps;
        }

        var approvers = (Approvers ?? Audience.Empty).Normalize(isGlobal);
        if (approvers.IsFailure)
        {
            return approvers.Error;
        }

        if (approvers.Value.IsEmpty)
        {
            return AutomationErrors.InvalidSteps;
        }

        return this with { Name = Name.Trim(), Approvers = approvers.Value };
    }
}

public sealed record WorkflowDefinitionData(
    string Name,
    string? Description,
    AutomationDocument Document,
    bool IsEnabled,
    bool IsMandatory,
    int Priority,
    ConditionMatch Match,
    IReadOnlyList<RuleCondition>? Conditions,
    IReadOnlyList<WorkflowStep>? Steps);

/// <summary>
/// Approval workflow of a kind of document: when its conditions match, the document needs the approval of every
/// step, in order, before the guarded action happens. <see cref="TenantId"/> null makes it global (every company):
/// the platform administrator decides whether companies may switch it off (<see cref="IsMandatory"/>).
/// </summary>
public sealed class WorkflowDefinition : AggregateRoot, IAuditReference
{
    private List<RuleCondition> _conditions = [];
    private List<WorkflowStep> _steps = [];

    private WorkflowDefinition()
    {
    }

    /// <summary>Company that owns it, or null for a global definition.</summary>
    public Guid? TenantId { get; private set; }

    public bool IsGlobal => TenantId is null;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public AutomationDocument Document { get; private set; }

    public bool IsEnabled { get; private set; } = true;

    /// <summary>Global definition that companies cannot switch off.</summary>
    public bool IsMandatory { get; private set; }

    /// <summary>Evaluation order (lower first); the first workflow whose conditions match is the one applied.</summary>
    public int Priority { get; private set; }

    public ConditionMatch Match { get; private set; }

    public IReadOnlyList<RuleCondition> Conditions => _conditions;

    public IReadOnlyList<WorkflowStep> Steps => _steps;

    public string AuditReference => Name;

    public static Result<WorkflowDefinition> Create(Guid? tenantId, WorkflowDefinitionData data)
    {
        var definition = new WorkflowDefinition { TenantId = tenantId };
        var result = definition.Update(data);
        return result.IsFailure ? result.Error : definition;
    }

    public Result Update(WorkflowDefinitionData data)
    {
        if (string.IsNullOrWhiteSpace(data.Name) || data.Name.Trim().Length > 120 || data.Description is { Length: > 500 })
        {
            return AutomationErrors.InvalidName;
        }

        if (data.Priority is < 0 or > 999)
        {
            return AutomationErrors.InvalidPriority;
        }

        var document = AutomationCatalog.Of(data.Document);
        if (document.Workflow is null)
        {
            return AutomationErrors.NoWorkflow;
        }

        var conditions = RuleConditions.Normalize(document, data.Conditions, IsGlobal);
        if (conditions.IsFailure)
        {
            return conditions.Error;
        }

        var steps = new List<WorkflowStep>();
        foreach (var step in data.Steps ?? [])
        {
            var normalized = step.Normalize(IsGlobal);
            if (normalized.IsFailure)
            {
                return normalized.Error;
            }

            steps.Add(normalized.Value);
        }

        if (steps.Count is 0 or > WorkflowStep.MaxSteps)
        {
            return AutomationErrors.InvalidSteps;
        }

        Name = data.Name.Trim();
        Description = string.IsNullOrWhiteSpace(data.Description) ? null : data.Description.Trim();
        Document = data.Document;
        IsEnabled = data.IsEnabled;
        IsMandatory = IsGlobal && data.IsMandatory;
        Priority = data.Priority;
        Match = data.Match;
        _conditions = conditions.Value.ToList();
        _steps = steps;
        return Result.Success();
    }
}

public enum RuleEffect
{
    /// <summary>The operation is rejected with the message of the rule.</summary>
    Block = 0,

    /// <summary>The operation goes on and the recipients are notified.</summary>
    Notify = 1
}

/// <summary>Who hears about a rule: an audience, extra e-mail addresses and the person who did the operation.</summary>
public sealed record RuleRecipients(Audience Audience, IReadOnlyList<string> Emails, bool NotifyActor)
{
    public static RuleRecipients None { get; } = new(Audience.Empty, [], false);

    public bool IsEmpty => Audience.IsEmpty && Emails.Count == 0 && !NotifyActor;

    public Result<RuleRecipients> Normalize(bool isGlobal)
    {
        var audience = (Audience ?? Audience.Empty).Normalize(isGlobal);
        if (audience.IsFailure)
        {
            return audience.Error;
        }

        var emails = new List<string>();
        foreach (var email in (Emails ?? []).Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            var normalized = TenantMembership.NormalizeEmail(email);
            if (normalized is null)
            {
                return MembershipErrors.InvalidEmail;
            }

            emails.Add(normalized);
        }

        return new RuleRecipients(audience.Value, emails.Distinct().Take(20).ToList(), NotifyActor);
    }
}

public sealed record BusinessRuleData(
    string Name,
    string? Description,
    AutomationDocument Document,
    IReadOnlyList<AutomationTrigger>? Triggers,
    bool IsEnabled,
    bool IsMandatory,
    int Priority,
    ConditionMatch Match,
    IReadOnlyList<RuleCondition>? Conditions,
    RuleEffect Effect,
    string Message,
    NotificationSeverity Severity,
    RuleRecipients? Recipients);

/// <summary>
/// Business rule of a kind of document: at the chosen moments of its life cycle, when the conditions match, the
/// operation is blocked with the message or the recipients are notified. Global rules (<see cref="TenantId"/> null)
/// apply to every company; the platform administrator decides whether companies may switch them off.
/// </summary>
public sealed class BusinessRule : AggregateRoot, IAuditReference
{
    private List<AutomationTrigger> _triggers = [];
    private List<RuleCondition> _conditions = [];

    private BusinessRule()
    {
    }

    public Guid? TenantId { get; private set; }

    public bool IsGlobal => TenantId is null;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public AutomationDocument Document { get; private set; }

    public IReadOnlyList<AutomationTrigger> Triggers => _triggers;

    public bool IsEnabled { get; private set; } = true;

    public bool IsMandatory { get; private set; }

    public int Priority { get; private set; }

    public ConditionMatch Match { get; private set; }

    public IReadOnlyList<RuleCondition> Conditions => _conditions;

    public RuleEffect Effect { get; private set; }

    /// <summary>Shown when blocking and sent when notifying; <c>{folio}</c> and <c>{total}</c>-like placeholders take the values of the document.</summary>
    public string Message { get; private set; } = null!;

    public NotificationSeverity Severity { get; private set; } = NotificationSeverity.Warning;

    public RuleRecipients Recipients { get; private set; } = RuleRecipients.None;

    public string AuditReference => Name;

    public static Result<BusinessRule> Create(Guid? tenantId, BusinessRuleData data)
    {
        var rule = new BusinessRule { TenantId = tenantId };
        var result = rule.Update(data);
        return result.IsFailure ? result.Error : rule;
    }

    public Result Update(BusinessRuleData data)
    {
        if (string.IsNullOrWhiteSpace(data.Name) || data.Name.Trim().Length > 120 || data.Description is { Length: > 500 })
        {
            return AutomationErrors.InvalidName;
        }

        if (data.Priority is < 0 or > 999)
        {
            return AutomationErrors.InvalidPriority;
        }

        if (string.IsNullOrWhiteSpace(data.Message) || data.Message.Trim().Length > 500)
        {
            return AutomationErrors.InvalidMessage;
        }

        var document = AutomationCatalog.Of(data.Document);
        var triggers = (data.Triggers ?? []).Distinct().ToList();
        if (triggers.Count == 0 || triggers.Any(t => !document.Triggers.Contains(t)))
        {
            return AutomationErrors.InvalidTriggers;
        }

        var conditions = RuleConditions.Normalize(document, data.Conditions, IsGlobal);
        if (conditions.IsFailure)
        {
            return conditions.Error;
        }

        if (conditions.Value.Count == 0)
        {
            return AutomationErrors.ConditionsRequired;
        }

        var recipients = (data.Recipients ?? RuleRecipients.None).Normalize(IsGlobal);
        if (recipients.IsFailure)
        {
            return recipients.Error;
        }

        if (data.Effect == RuleEffect.Notify && recipients.Value.IsEmpty)
        {
            return AutomationErrors.RecipientsRequired;
        }

        Name = data.Name.Trim();
        Description = string.IsNullOrWhiteSpace(data.Description) ? null : data.Description.Trim();
        Document = data.Document;
        _triggers = triggers.Order().ToList();
        IsEnabled = data.IsEnabled;
        IsMandatory = IsGlobal && data.IsMandatory;
        Priority = data.Priority;
        Match = data.Match;
        _conditions = conditions.Value.ToList();
        Effect = data.Effect;
        Message = data.Message.Trim();
        Severity = data.Severity;
        Recipients = recipients.Value;
        return Result.Success();
    }

    /// <summary>The message with the values of the document: <c>{folio}</c> and any fact key between braces.</summary>
    public string Render(string? reference, IReadOnlyDictionary<string, object?> facts)
    {
        var text = Message.Replace("{folio}", reference ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        foreach (var (key, value) in facts)
        {
            text = text.Replace($"{{{key}}}", RuleConditions.Format(value) ?? "—", StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }
}

public enum AutomationKind
{
    Workflow = 0,
    Rule = 1
}

/// <summary>A company switched off an optional global workflow or rule.</summary>
public sealed class AutomationOptOut : Entity, ITenantOwned
{
    private AutomationOptOut()
    {
    }

    public AutomationOptOut(AutomationKind kind, Guid definitionId, string changedBy, DateTimeOffset changedAt)
    {
        Kind = kind;
        DefinitionId = definitionId;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
    }

    public Guid TenantId { get; private set; }

    public AutomationKind Kind { get; private set; }

    public Guid DefinitionId { get; private set; }

    public string ChangedBy { get; private set; } = null!;

    public DateTimeOffset ChangedAt { get; private set; }
}
