using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.Hse;

public enum IncidentType
{
    Incident = 0,
    Accident = 1,
    NearMiss = 2,
    UnsafeCondition = 3,
    UnsafeAct = 4,
    Spill = 5,
    EquipmentDamage = 6,
    Environmental = 7,
    VehicleAccident = 8
}

public enum IncidentSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum IncidentStatus
{
    Reported = 0,
    Investigating = 1,
    Closed = 2
}

public enum ActionStatus
{
    Pending = 0,
    InProgress = 1,
    Done = 2
}

/// <summary>Acción correctiva o preventiva derivada de un incidente.</summary>
public sealed record CorrectiveAction(string Description, string? Responsible, DateOnly? DueOn, ActionStatus Status, DateOnly? CompletedOn);

public sealed record IncidentData(
    DateTimeOffset OccurredAt,
    IncidentType Type,
    IncidentSeverity Severity,
    Guid? SiteId,
    Guid? UnitId,
    Guid? WorkOrderId,
    string? Location,
    IReadOnlyList<Guid>? EmployeeIds,
    string Description,
    string? ImmediateActions,
    string? RootCause,
    int? LostDays,
    IReadOnlyList<CorrectiveAction>? Actions,
    IReadOnlyList<Guid>? PhotoFileIds);

public sealed record IncidentReportedEvent(Guid IncidentId) : IDomainEvent;

/// <summary>Reporte de incidente, accidente o casi accidente con su investigación y acciones correctivas.</summary>
public sealed class Incident : TenantAggregateRoot, IAuditReference
{
    private List<Guid> _employeeIds = [];
    private List<CorrectiveAction> _actions = [];
    private List<Guid> _photoFileIds = [];

    private Incident()
    {
    }

    public string Folio { get; private set; } = null!;

    public long FolioNumber { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public IncidentType Type { get; private set; }

    public IncidentSeverity Severity { get; private set; }

    public IncidentStatus Status { get; private set; } = IncidentStatus.Reported;

    public Guid? SiteId { get; private set; }

    public Guid? UnitId { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public string? Location { get; private set; }

    public IReadOnlyList<Guid> EmployeeIds => _employeeIds;

    public string Description { get; private set; } = null!;

    public string? ImmediateActions { get; private set; }

    public string? RootCause { get; private set; }

    /// <summary>Días perdidos (accidente incapacitante).</summary>
    public int? LostDays { get; private set; }

    public IReadOnlyList<CorrectiveAction> Actions => _actions;

    public IReadOnlyList<Guid> PhotoFileIds => _photoFileIds;

    public Guid? ReportedById { get; private set; }

    public string ReportedByName { get; private set; } = null!;

    public DateTimeOffset? ClosedAt { get; private set; }

    public string AuditReference => Folio;

    public static Result<Incident> Report(string folio, long folioNumber, IncidentData data, Guid? reportedById, string reportedByName)
    {
        var incident = new Incident { Folio = folio, FolioNumber = folioNumber, ReportedById = reportedById, ReportedByName = reportedByName };
        var result = incident.Apply(data);
        if (result.IsFailure)
        {
            return result.Error;
        }

        incident.Raise(new IncidentReportedEvent(incident.Id));
        return incident;
    }

    public Result Update(IncidentData data)
    {
        if (Status == IncidentStatus.Closed)
        {
            return HseErrors.IncidentClosed;
        }

        var result = Apply(data);
        if (result.IsSuccess && Status == IncidentStatus.Reported && (RootCause is not null || _actions.Count > 0))
        {
            Status = IncidentStatus.Investigating;
        }

        return result;
    }

    public Result Close(DateTimeOffset at)
    {
        if (Status == IncidentStatus.Closed)
        {
            return HseErrors.IncidentClosed;
        }

        if (Severity >= IncidentSeverity.Medium && string.IsNullOrWhiteSpace(RootCause))
        {
            return HseErrors.RootCauseRequired;
        }

        if (_actions.Any(a => a.Status != ActionStatus.Done))
        {
            return HseErrors.OpenActions;
        }

        Status = IncidentStatus.Closed;
        ClosedAt = at;
        return Result.Success();
    }

    public void Reopen()
    {
        Status = IncidentStatus.Investigating;
        ClosedAt = null;
    }

    private Result Apply(IncidentData data)
    {
        if (string.IsNullOrWhiteSpace(data.Description) || data.Description.Trim().Length > 4000)
        {
            return HseErrors.DescriptionRequired;
        }

        if (data.LostDays is < 0)
        {
            return HseErrors.InvalidLostDays;
        }

        var actions = data.Actions ?? [];
        if (actions.Any(a => string.IsNullOrWhiteSpace(a.Description)))
        {
            return HseErrors.InvalidAction;
        }

        OccurredAt = data.OccurredAt;
        Type = data.Type;
        Severity = data.Severity;
        SiteId = data.SiteId;
        UnitId = data.UnitId;
        WorkOrderId = data.WorkOrderId;
        Location = string.IsNullOrWhiteSpace(data.Location) ? null : data.Location.Trim();
        _employeeIds = (data.EmployeeIds ?? []).Distinct().ToList();
        Description = data.Description.Trim();
        ImmediateActions = string.IsNullOrWhiteSpace(data.ImmediateActions) ? null : data.ImmediateActions.Trim();
        RootCause = string.IsNullOrWhiteSpace(data.RootCause) ? null : data.RootCause.Trim();
        LostDays = data.LostDays;
        _actions = actions.Select(a => a with
        {
            Description = a.Description.Trim(),
            Responsible = string.IsNullOrWhiteSpace(a.Responsible) ? null : a.Responsible.Trim(),
            CompletedOn = a.Status == ActionStatus.Done ? a.CompletedOn ?? DateOnly.FromDateTime(DateTime.UtcNow) : null
        }).ToList();
        _photoFileIds = (data.PhotoFileIds ?? []).Distinct().ToList();
        return Result.Success();
    }
}

public enum InspectionCategory
{
    UnitPreUse = 0,
    Ppe = 1,
    Site = 2,
    Audit = 3,
    Other = 4
}

/// <summary>Pregunta del checklist; las críticas en "no cumple" marcan la inspección como no aprobada.</summary>
public sealed record ChecklistItem(string Key, string Question, bool IsCritical);

/// <summary>Checklist reutilizable (pre-uso de unidad, EPP, sitio, auditoría).</summary>
public sealed class InspectionTemplate : CatalogAggregate
{
    private List<ChecklistItem> _items = [];

    private InspectionTemplate()
    {
    }

    public InspectionCategory Category { get; private set; }

    public IReadOnlyList<ChecklistItem> Items => _items;

    public static Result<InspectionTemplate> Create(string code, string name, InspectionCategory category, IReadOnlyList<ChecklistItem> items, string? notes)
    {
        var template = new InspectionTemplate();
        var result = template.Update(code, name, category, items, notes);
        return result.IsFailure ? result.Error : template;
    }

    public Result Update(string code, string name, InspectionCategory category, IReadOnlyList<ChecklistItem> items, string? notes)
    {
        var identity = SetIdentity(code, name, notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if (items.Count == 0 || items.Any(i => string.IsNullOrWhiteSpace(i.Question)))
        {
            return HseErrors.EmptyChecklist;
        }

        Category = category;
        _items = items.Select((i, index) => i with
        {
            Key = string.IsNullOrWhiteSpace(i.Key) ? $"q{index + 1}" : i.Key.Trim(),
            Question = i.Question.Trim()
        }).ToList();

        if (_items.GroupBy(i => i.Key).Any(g => g.Count() > 1))
        {
            return HseErrors.DuplicatedQuestion;
        }

        return Result.Success();
    }
}

public enum CheckResult
{
    Ok = 0,
    NotOk = 1,
    NotApplicable = 2
}

public sealed record InspectionAnswer(string Key, string Question, bool IsCritical, CheckResult Result, string? Comment, Guid? PhotoFileId);

/// <summary>Inspección realizada con un checklist; calcula el porcentaje de cumplimiento y los hallazgos.</summary>
public sealed class Inspection : TenantAggregateRoot
{
    private List<InspectionAnswer> _answers = [];

    private Inspection()
    {
    }

    public Guid TemplateId { get; private set; }

    public string TemplateName { get; private set; } = null!;

    public InspectionCategory Category { get; private set; }

    public DateTimeOffset PerformedAt { get; private set; }

    public Guid? InspectorId { get; private set; }

    public string InspectorName { get; private set; } = null!;

    public Guid? UnitId { get; private set; }

    public Guid? SiteId { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public IReadOnlyList<InspectionAnswer> Answers => _answers;

    /// <summary>Percentage of applicable questions answered OK.</summary>
    public decimal Score { get; private set; }

    public int Findings { get; private set; }

    public int CriticalFindings { get; private set; }

    public bool Passed => CriticalFindings == 0;

    public string? Notes { get; private set; }

    public static Result<Inspection> Record(
        InspectionTemplate template,
        DateTimeOffset performedAt,
        Guid? inspectorId,
        string inspectorName,
        Guid? unitId,
        Guid? siteId,
        Guid? workOrderId,
        IReadOnlyDictionary<string, (CheckResult Result, string? Comment, Guid? PhotoFileId)> answers,
        string? notes)
    {
        var list = new List<InspectionAnswer>();
        foreach (var item in template.Items)
        {
            if (!answers.TryGetValue(item.Key, out var answer))
            {
                return HseErrors.UnansweredQuestion(item.Question);
            }

            list.Add(new InspectionAnswer(item.Key, item.Question, item.IsCritical, answer.Result, string.IsNullOrWhiteSpace(answer.Comment) ? null : answer.Comment.Trim(), answer.PhotoFileId));
        }

        var applicable = list.Count(a => a.Result != CheckResult.NotApplicable);
        var ok = list.Count(a => a.Result == CheckResult.Ok);

        return new Inspection
        {
            TemplateId = template.Id,
            TemplateName = template.Name,
            Category = template.Category,
            PerformedAt = performedAt,
            InspectorId = inspectorId,
            InspectorName = inspectorName,
            UnitId = unitId,
            SiteId = siteId,
            WorkOrderId = workOrderId,
            _answers = list,
            Score = applicable == 0 ? 100 : Math.Round(ok * 100m / applicable, 1),
            Findings = list.Count(a => a.Result == CheckResult.NotOk),
            CriticalFindings = list.Count(a => a.Result == CheckResult.NotOk && a.IsCritical),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
    }
}

public sealed record SafetyTalkData(
    DateTimeOffset HeldAt,
    string Topic,
    int DurationMinutes,
    Guid? SiteId,
    Guid? WorkOrderId,
    string Instructor,
    IReadOnlyList<Guid>? AttendeeIds,
    string? ExternalAttendees,
    IReadOnlyList<Guid>? EvidenceFileIds,
    string? Notes);

/// <summary>Plática de seguridad (5 minutos, arranque de jornada) con lista de asistencia y evidencia.</summary>
public sealed class SafetyTalk : TenantAggregateRoot
{
    private List<Guid> _attendeeIds = [];
    private List<Guid> _evidenceFileIds = [];

    private SafetyTalk()
    {
    }

    public DateTimeOffset HeldAt { get; private set; }

    public string Topic { get; private set; } = null!;

    public int DurationMinutes { get; private set; }

    public Guid? SiteId { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public string Instructor { get; private set; } = null!;

    public IReadOnlyList<Guid> AttendeeIds => _attendeeIds;

    public string? ExternalAttendees { get; private set; }

    public IReadOnlyList<Guid> EvidenceFileIds => _evidenceFileIds;

    public string? Notes { get; private set; }

    public static Result<SafetyTalk> Create(SafetyTalkData data)
    {
        var talk = new SafetyTalk();
        var result = talk.Update(data);
        return result.IsFailure ? result.Error : talk;
    }

    public Result Update(SafetyTalkData data)
    {
        if (string.IsNullOrWhiteSpace(data.Topic) || data.Topic.Trim().Length > 200 || string.IsNullOrWhiteSpace(data.Instructor))
        {
            return HseErrors.TalkDataRequired;
        }

        if (data.DurationMinutes is < 1 or > 480)
        {
            return HseErrors.InvalidDuration;
        }

        if ((data.AttendeeIds?.Count ?? 0) == 0 && string.IsNullOrWhiteSpace(data.ExternalAttendees))
        {
            return HseErrors.AttendeesRequired;
        }

        HeldAt = data.HeldAt;
        Topic = data.Topic.Trim();
        DurationMinutes = data.DurationMinutes;
        SiteId = data.SiteId;
        WorkOrderId = data.WorkOrderId;
        Instructor = data.Instructor.Trim();
        _attendeeIds = (data.AttendeeIds ?? []).Distinct().ToList();
        ExternalAttendees = string.IsNullOrWhiteSpace(data.ExternalAttendees) ? null : data.ExternalAttendees.Trim();
        _evidenceFileIds = (data.EvidenceFileIds ?? []).Distinct().ToList();
        Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();
        return Result.Success();
    }
}

public static class HseErrors
{
    public static readonly Error IncidentNotFound = Error.NotFound("Hse.IncidentNotFound", "El reporte de incidente no existe.");

    public static readonly Error InspectionNotFound = Error.NotFound("Hse.InspectionNotFound", "La inspección no existe.");

    public static readonly Error TalkNotFound = Error.NotFound("Hse.TalkNotFound", "La plática de seguridad no existe.");

    public static readonly Error DescriptionRequired = Error.Validation("Hse.DescriptionRequired", "Describe lo ocurrido (máximo 4000 caracteres).");

    public static readonly Error InvalidLostDays = Error.Validation("Hse.InvalidLostDays", "Los días perdidos no pueden ser negativos.");

    public static readonly Error InvalidAction = Error.Validation("Hse.InvalidAction", "Cada acción correctiva necesita descripción.");

    public static readonly Error IncidentClosed = Error.Conflict("Hse.IncidentClosed", "El incidente ya está cerrado.");

    public static readonly Error RootCauseRequired = Error.Validation("Hse.RootCauseRequired", "Registra la causa raíz antes de cerrar un incidente de severidad media o mayor.");

    public static readonly Error OpenActions = Error.Validation("Hse.OpenActions", "Hay acciones correctivas pendientes.");

    public static readonly Error EmptyChecklist = Error.Validation("Hse.EmptyChecklist", "El checklist necesita al menos una pregunta.");

    public static readonly Error DuplicatedQuestion = Error.Validation("Hse.DuplicatedQuestion", "Hay preguntas con la misma clave.");

    public static readonly Error TalkDataRequired = Error.Validation("Hse.TalkDataRequired", "Tema (máximo 200 caracteres) e instructor son obligatorios.");

    public static readonly Error InvalidDuration = Error.Validation("Hse.InvalidDuration", "La duración va de 1 a 480 minutos.");

    public static readonly Error AttendeesRequired = Error.Validation("Hse.AttendeesRequired", "Registra a los asistentes.");

    public static Error UnansweredQuestion(string question) => Error.Validation("Hse.UnansweredQuestion", $"Falta responder: '{question}'.");
}
