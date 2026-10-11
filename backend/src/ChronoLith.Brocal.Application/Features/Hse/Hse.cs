using System.Linq.Expressions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Tickets;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Hse;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Hse;

// ---- Incidents ------------------------------------------------------------------------------------------

public sealed record IncidentSummaryDto(
    Guid Id, string Folio, DateTimeOffset OccurredAt, IncidentType Type, IncidentSeverity Severity, IncidentStatus Status, string? SiteName, string? UnitCode,
    string Description, int OpenActions, string ReportedByName);

public sealed record IncidentDetailDto(
    IncidentSummaryDto Summary, Guid? SiteId, Guid? UnitId, Guid? WorkOrderId, string? WorkOrderFolio, string? Location, IReadOnlyList<NamedRef> Employees,
    string? ImmediateActions, string? RootCause, int? LostDays, IReadOnlyList<CorrectiveAction> Actions, IReadOnlyList<TicketPhotoDto> Photos, DateTimeOffset? ClosedAt);

public sealed record SearchIncidentsQuery(string? Search, IncidentStatus? Status, IncidentType? Type, IncidentSeverity? Severity, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 25)
    : IQuery<PagedResult<IncidentSummaryDto>>;

internal sealed class SearchIncidentsQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<SearchIncidentsQuery, PagedResult<IncidentSummaryDto>>
{
    public async Task<Result<PagedResult<IncidentSummaryDto>>> Handle(SearchIncidentsQuery query, CancellationToken cancellationToken)
    {
        var request = new PageRequest(query.Search, query.Page, query.PageSize);
        var from = query.From is null ? (DateTimeOffset?)null : clock.StartOfDay(query.From.Value);
        var to = query.To is null ? (DateTimeOffset?)null : clock.StartOfDay(query.To.Value.AddDays(1));
        var incidents = db.Incidents.AsNoTracking()
            .WhereIf(query.Status is not null, i => i.Status == query.Status)
            .WhereIf(query.Type is not null, i => i.Type == query.Type)
            .WhereIf(query.Severity is not null, i => i.Severity == query.Severity)
            .WhereIf(from is not null, i => i.OccurredAt >= from)
            .WhereIf(to is not null, i => i.OccurredAt < to);

        if (request.Term is { } term)
        {
            incidents = incidents.Where(i => i.Folio.ToLower().Contains(term) || i.Description.ToLower().Contains(term));
        }

        var page = await incidents.OrderByDescending(i => i.OccurredAt).ToPagedAsync(request, cancellationToken);
        var items = await IncidentProjection.SummariesAsync(db, page.Items, cancellationToken);
        return new PagedResult<IncidentSummaryDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

internal static class IncidentProjection
{
    public static async Task<List<IncidentSummaryDto>> SummariesAsync(IApplicationDbContext db, IReadOnlyList<Incident> incidents, CancellationToken cancellationToken)
    {
        var sites = await CatalogNames.LoadAsync<Site>(db, incidents.Select(i => i.SiteId), cancellationToken);
        var units = await CatalogNames.LoadAsync<Unit>(db, incidents.Select(i => i.UnitId), cancellationToken);
        return incidents.Select(i => new IncidentSummaryDto(i.Id, i.Folio, i.OccurredAt, i.Type, i.Severity, i.Status, sites.NameOf(i.SiteId), units.CodeOf(i.UnitId),
            i.Description, i.Actions.Count(a => a.Status != ActionStatus.Done), i.ReportedByName)).ToList();
    }
}

public sealed record GetIncidentQuery(Guid Id) : IQuery<IncidentDetailDto>;

internal sealed class GetIncidentQueryHandler(IApplicationDbContext db, IFileService files) : IQueryHandler<GetIncidentQuery, IncidentDetailDto>
{
    public async Task<Result<IncidentDetailDto>> Handle(GetIncidentQuery query, CancellationToken cancellationToken)
    {
        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == query.Id, cancellationToken);
        if (incident is null)
        {
            return HseErrors.IncidentNotFound;
        }

        var summary = (await IncidentProjection.SummariesAsync(db, [incident], cancellationToken))[0];
        var employees = await CatalogNames.LoadAsync<Employee>(db, incident.EmployeeIds, cancellationToken);
        var order = incident.WorkOrderId is null ? null : await db.WorkOrders.AsNoTracking().Where(w => w.Id == incident.WorkOrderId).Select(w => w.Folio).FirstOrDefaultAsync(cancellationToken);
        return new IncidentDetailDto(summary, incident.SiteId, incident.UnitId, incident.WorkOrderId, order, incident.Location,
            incident.EmployeeIds.Where(employees.ContainsKey).Select(id => new NamedRef(id, employees[id].Code, employees[id].Name)).ToList(),
            incident.ImmediateActions, incident.RootCause, incident.LostDays, incident.Actions,
            incident.PhotoFileIds.Select(f => new TicketPhotoDto(f, f, files.GetUrl(f), null, incident.OccurredAt, null, null)).ToList(), incident.ClosedAt);
    }
}

public sealed record SaveIncidentCommand(Guid? Id, IncidentData Data) : ICommand<Guid>;

internal sealed class SaveIncidentCommandValidator : AbstractValidator<SaveIncidentCommand>
{
    public SaveIncidentCommandValidator()
    {
        RuleFor(c => c.Data.Description).NotEmpty().MaximumLength(4000);
        RuleFor(c => c.Data.LostDays).GreaterThanOrEqualTo(0).When(c => c.Data.LostDays is not null);
    }
}

internal sealed class SaveIncidentCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    IUserContext user,
    IFolioService folios,
    INotificationPublisher notifications) : ICommandHandler<SaveIncidentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveIncidentCommand command, CancellationToken cancellationToken)
    {
        var references = Result.FirstFailure(
            await References.EnsureAsync<Site>(db, command.Data.SiteId, SiteErrors.Catalog, cancellationToken),
            await References.EnsureAsync<Unit>(db, command.Data.UnitId, UnitErrors.Catalog, cancellationToken),
            await References.EnsureAllAsync<Employee>(db, command.Data.EmployeeIds, EmployeeErrors.Catalog, cancellationToken));
        if (references.IsFailure)
        {
            return references.Error;
        }

        if (command.Id is { } id)
        {
            if (!tenant.Has(Permissions.HseManage))
            {
                return Error.Forbidden("Hse.Forbidden", "Solo HSE puede editar la investigación del incidente.");
            }

            var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (incident is null)
            {
                return HseErrors.IncidentNotFound;
            }

            var updated = incident.Update(command.Data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            await db.SaveChangesAsync(cancellationToken);
            return incident.Id;
        }

        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var number = await folios.NextAsync(FolioKeys.Incident, ct);
            var reported = Incident.Report(FolioSequence.Format(tenant.Tenant.Settings.IncidentPrefix, number), number, command.Data, tenant.Membership?.Id, user.DisplayName);
            if (reported.IsFailure)
            {
                return Result.Failure<Guid>(reported.Error);
            }

            var incident = reported.Value;
            db.Incidents.Add(incident);
            await notifications.PublishAsync(new NotificationMessage(
                NotificationEvent.IncidentReported,
                incident.Severity >= IncidentSeverity.High ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                $"{incident.Folio}: {incident.Type} ({incident.Severity})",
                incident.Description,
                $"hse/incidentes/{incident.Id}"), ct);
            await db.SaveChangesAsync(ct);
            return Result.Success(incident.Id);
        }, cancellationToken);
    }
}

public sealed record CloseIncidentCommand(Guid Id, bool Reopen = false) : ICommand;

internal sealed class CloseIncidentCommandHandler(IApplicationDbContext db, ITenantClock clock, IAuditLog audit) : ICommandHandler<CloseIncidentCommand>
{
    public async Task<Result> Handle(CloseIncidentCommand command, CancellationToken cancellationToken)
    {
        var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken);
        if (incident is null)
        {
            return HseErrors.IncidentNotFound;
        }

        if (command.Reopen)
        {
            incident.Reopen();
        }
        else
        {
            var closed = incident.Close(clock.UtcNow);
            if (closed.IsFailure)
            {
                return closed;
            }
        }

        audit.Record(AuditAction.StatusChanged, nameof(Incident), incident.Id, $"{incident.Folio} → {incident.Status}");
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---- Inspection templates and inspections ---------------------------------------------------------------

public sealed record InspectionTemplateData(string Code, string Name, InspectionCategory Category, IReadOnlyList<ChecklistItem> Items, string? Notes);

public sealed record InspectionTemplateDto(Guid Id, string Code, string Name, InspectionCategory Category, IReadOnlyList<ChecklistItem> Items, string? Notes, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

internal sealed class InspectionTemplateDataValidator : AbstractValidator<InspectionTemplateData>
{
    public InspectionTemplateDataValidator()
    {
        RuleFor(t => t.Code).BusinessCode();
        RuleFor(t => t.Name).RequiredName();
        RuleFor(t => t.Items).NotEmpty().WithMessage("Agrega al menos una pregunta.");
    }
}

internal sealed class InspectionTemplateSpec(IApplicationDbContext db) : ICatalogSpec<InspectionTemplate, InspectionTemplateData, InspectionTemplateDto>
{
    public string Name => "InspectionTemplates";

    public Expression<Func<InspectionTemplate, InspectionTemplateDto>> Projection => t => new InspectionTemplateDto(t.Id, t.Code, t.Name, t.Category, t.Items, t.Notes, t.IsActive, t.CreatedAt, t.UpdatedAt);

    public string CodeOf(InspectionTemplateData data) => data.Code;

    public Result<InspectionTemplate> Create(InspectionTemplateData data) => InspectionTemplate.Create(data.Code, data.Name, data.Category, data.Items, data.Notes);

    public Result Update(InspectionTemplate entity, InspectionTemplateData data) => entity.Update(data.Code, data.Name, data.Category, data.Items, data.Notes);

    public Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) => db.Inspections.AnyAsync(i => i.TemplateId == id, cancellationToken);
}

public sealed record InspectionSummaryDto(Guid Id, string TemplateName, InspectionCategory Category, DateTimeOffset PerformedAt, string InspectorName, string? UnitCode, string? SiteName,
    decimal Score, int Findings, int CriticalFindings, bool Passed);

public sealed record InspectionDetailDto(InspectionSummaryDto Summary, Guid TemplateId, Guid? UnitId, Guid? SiteId, Guid? WorkOrderId, IReadOnlyList<InspectionAnswer> Answers,
    IReadOnlyDictionary<Guid, string> PhotoUrls, string? Notes);

public sealed record SearchInspectionsQuery(InspectionCategory? Category, Guid? UnitId, bool? OnlyFailed, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 25)
    : IQuery<PagedResult<InspectionSummaryDto>>;

internal sealed class SearchInspectionsQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<SearchInspectionsQuery, PagedResult<InspectionSummaryDto>>
{
    public async Task<Result<PagedResult<InspectionSummaryDto>>> Handle(SearchInspectionsQuery query, CancellationToken cancellationToken)
    {
        var from = query.From is null ? (DateTimeOffset?)null : clock.StartOfDay(query.From.Value);
        var to = query.To is null ? (DateTimeOffset?)null : clock.StartOfDay(query.To.Value.AddDays(1));
        return await db.Inspections.AsNoTracking()
            .WhereIf(query.Category is not null, i => i.Category == query.Category)
            .WhereIf(query.UnitId is not null, i => i.UnitId == query.UnitId)
            .WhereIf(query.OnlyFailed == true, i => i.CriticalFindings > 0)
            .WhereIf(from is not null, i => i.PerformedAt >= from)
            .WhereIf(to is not null, i => i.PerformedAt < to)
            .OrderByDescending(i => i.PerformedAt)
            .Select(i => new InspectionSummaryDto(i.Id, i.TemplateName, i.Category, i.PerformedAt, i.InspectorName,
                db.Units.Where(u => u.Id == i.UnitId).Select(u => u.Code).FirstOrDefault(), db.Sites.Where(s => s.Id == i.SiteId).Select(s => s.Name).FirstOrDefault(),
                i.Score, i.Findings, i.CriticalFindings, i.CriticalFindings == 0))
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);
    }
}

public sealed record GetInspectionQuery(Guid Id) : IQuery<InspectionDetailDto>;

internal sealed class GetInspectionQueryHandler(IApplicationDbContext db, IFileService files) : IQueryHandler<GetInspectionQuery, InspectionDetailDto>
{
    public async Task<Result<InspectionDetailDto>> Handle(GetInspectionQuery query, CancellationToken cancellationToken)
    {
        var inspection = await db.Inspections.AsNoTracking().FirstOrDefaultAsync(i => i.Id == query.Id, cancellationToken);
        if (inspection is null)
        {
            return HseErrors.InspectionNotFound;
        }

        var unit = await CatalogNames.LoadAsync<Unit>(db, [inspection.UnitId], cancellationToken);
        var site = await CatalogNames.LoadAsync<Site>(db, [inspection.SiteId], cancellationToken);
        var summary = new InspectionSummaryDto(inspection.Id, inspection.TemplateName, inspection.Category, inspection.PerformedAt, inspection.InspectorName, unit.CodeOf(inspection.UnitId),
            site.NameOf(inspection.SiteId), inspection.Score, inspection.Findings, inspection.CriticalFindings, inspection.Passed);
        var photos = inspection.Answers.Where(a => a.PhotoFileId is not null).ToDictionary(a => a.PhotoFileId!.Value, a => files.GetUrl(a.PhotoFileId!.Value));
        return new InspectionDetailDto(summary, inspection.TemplateId, inspection.UnitId, inspection.SiteId, inspection.WorkOrderId, inspection.Answers, photos, inspection.Notes);
    }
}

public sealed record InspectionAnswerInput(string Key, CheckResult Result, string? Comment, Guid? PhotoFileId);

public sealed record RecordInspectionCommand(Guid TemplateId, DateTimeOffset? PerformedAt, Guid? UnitId, Guid? SiteId, Guid? WorkOrderId, IReadOnlyList<InspectionAnswerInput> Answers, string? Notes)
    : ICommand<InspectionSummaryDto>;

internal sealed class RecordInspectionCommandHandler(IApplicationDbContext db, ITenantContext tenant, IUserContext user, ITenantClock clock, INotificationPublisher notifications)
    : ICommandHandler<RecordInspectionCommand, InspectionSummaryDto>
{
    public async Task<Result<InspectionSummaryDto>> Handle(RecordInspectionCommand command, CancellationToken cancellationToken)
    {
        var template = await db.InspectionTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == command.TemplateId, cancellationToken);
        if (template is null)
        {
            return CatalogErrors.ReferenceNotFound("InspectionTemplates");
        }

        var references = Result.FirstFailure(
            await References.EnsureAsync<Unit>(db, command.UnitId, UnitErrors.Catalog, cancellationToken),
            await References.EnsureAsync<Site>(db, command.SiteId, SiteErrors.Catalog, cancellationToken));
        if (references.IsFailure)
        {
            return references.Error;
        }

        var answers = command.Answers.ToDictionary(a => a.Key, a => (a.Result, a.Comment, a.PhotoFileId));
        var recorded = Inspection.Record(template, command.PerformedAt ?? clock.UtcNow, tenant.Membership?.Id, user.DisplayName, command.UnitId, command.SiteId, command.WorkOrderId, answers, command.Notes);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        var inspection = recorded.Value;
        db.Inspections.Add(inspection);
        if (inspection.CriticalFindings > 0)
        {
            var unit = command.UnitId is null ? null : await db.Units.AsNoTracking().Where(u => u.Id == command.UnitId).Select(u => u.Code).FirstOrDefaultAsync(cancellationToken);
            await notifications.PublishAsync(new NotificationMessage(NotificationEvent.IncidentReported, NotificationSeverity.Warning,
                $"Inspección no aprobada: {template.Name}",
                $"{inspection.CriticalFindings} hallazgo(s) crítico(s){(unit is null ? string.Empty : $" en {unit}")}: " +
                string.Join("; ", inspection.Answers.Where(a => a.IsCritical && a.Result == CheckResult.NotOk).Select(a => a.Question)),
                $"hse/inspecciones/{inspection.Id}"), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new InspectionSummaryDto(inspection.Id, inspection.TemplateName, inspection.Category, inspection.PerformedAt, inspection.InspectorName, null, null,
            inspection.Score, inspection.Findings, inspection.CriticalFindings, inspection.Passed);
    }
}

// ---- Safety talks ---------------------------------------------------------------------------------------

public sealed record SafetyTalkDto(
    Guid Id, DateTimeOffset HeldAt, string Topic, int DurationMinutes, Guid? SiteId, string? SiteName, Guid? WorkOrderId, string Instructor, IReadOnlyList<NamedRef> Attendees,
    string? ExternalAttendees, IReadOnlyList<string> EvidenceUrls, IReadOnlyList<Guid> EvidenceFileIds, string? Notes);

public sealed record SearchSafetyTalksQuery(DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 25) : IQuery<PagedResult<SafetyTalkDto>>;

internal sealed class SearchSafetyTalksQueryHandler(IApplicationDbContext db, ITenantClock clock, IFileService files) : IQueryHandler<SearchSafetyTalksQuery, PagedResult<SafetyTalkDto>>
{
    public async Task<Result<PagedResult<SafetyTalkDto>>> Handle(SearchSafetyTalksQuery query, CancellationToken cancellationToken)
    {
        var from = query.From is null ? (DateTimeOffset?)null : clock.StartOfDay(query.From.Value);
        var to = query.To is null ? (DateTimeOffset?)null : clock.StartOfDay(query.To.Value.AddDays(1));
        var page = await db.SafetyTalks.AsNoTracking()
            .WhereIf(from is not null, t => t.HeldAt >= from)
            .WhereIf(to is not null, t => t.HeldAt < to)
            .OrderByDescending(t => t.HeldAt)
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);

        var employees = await CatalogNames.LoadAsync<Employee>(db, page.Items.SelectMany(t => t.AttendeeIds), cancellationToken);
        var sites = await CatalogNames.LoadAsync<Site>(db, page.Items.Select(t => t.SiteId), cancellationToken);
        var items = page.Items.Select(t => new SafetyTalkDto(t.Id, t.HeldAt, t.Topic, t.DurationMinutes, t.SiteId, sites.NameOf(t.SiteId), t.WorkOrderId, t.Instructor,
            t.AttendeeIds.Where(employees.ContainsKey).Select(id => new NamedRef(id, employees[id].Code, employees[id].Name)).ToList(), t.ExternalAttendees,
            t.EvidenceFileIds.Select(f => files.GetUrl(f)).ToList(), t.EvidenceFileIds, t.Notes)).ToList();
        return new PagedResult<SafetyTalkDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

public sealed record SaveSafetyTalkCommand(Guid? Id, SafetyTalkData Data) : ICommand<Guid>;

internal sealed class SaveSafetyTalkCommandHandler(IApplicationDbContext db) : ICommandHandler<SaveSafetyTalkCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveSafetyTalkCommand command, CancellationToken cancellationToken)
    {
        var references = Result.FirstFailure(
            await References.EnsureAsync<Site>(db, command.Data.SiteId, SiteErrors.Catalog, cancellationToken),
            await References.EnsureAllAsync<Employee>(db, command.Data.AttendeeIds, EmployeeErrors.Catalog, cancellationToken));
        if (references.IsFailure)
        {
            return references.Error;
        }

        if (command.Id is { } id)
        {
            var talk = await db.SafetyTalks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (talk is null)
            {
                return HseErrors.TalkNotFound;
            }

            var updated = talk.Update(command.Data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            await db.SaveChangesAsync(cancellationToken);
            return talk.Id;
        }

        var created = SafetyTalk.Create(command.Data);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.SafetyTalks.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value.Id;
    }
}

// ---- HSE indicators -------------------------------------------------------------------------------------

public sealed record HseStatsDto(
    int Incidents, int Accidents, int NearMisses, int LostDays, int OpenIncidents, int OpenActions, int Inspections, decimal AverageScore, int FailedInspections,
    int SafetyTalks, int TalkAttendees, int DaysWithoutLostTimeAccident, IReadOnlyList<KpiDto> ByType);

/// <summary>Indicadores HSE del periodo (evidencia para auditorías e ISO 45001).</summary>
public sealed record GetHseStatsQuery(DateOnly From, DateOnly To) : IQuery<HseStatsDto>;

internal sealed class GetHseStatsQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<GetHseStatsQuery, HseStatsDto>
{
    public async Task<Result<HseStatsDto>> Handle(GetHseStatsQuery query, CancellationToken cancellationToken)
    {
        var from = clock.StartOfDay(query.From);
        var to = clock.StartOfDay(query.To.AddDays(1));
        var incidents = await db.Incidents.AsNoTracking().Where(i => i.OccurredAt >= from && i.OccurredAt < to).ToListAsync(cancellationToken);
        var open = await db.Incidents.AsNoTracking().Where(i => i.Status != IncidentStatus.Closed).ToListAsync(cancellationToken);
        var inspections = await db.Inspections.AsNoTracking().Where(i => i.PerformedAt >= from && i.PerformedAt < to).Select(i => new { i.Score, i.CriticalFindings }).ToListAsync(cancellationToken);
        var talks = await db.SafetyTalks.AsNoTracking().Where(t => t.HeldAt >= from && t.HeldAt < to).Select(t => t.AttendeeIds).ToListAsync(cancellationToken);
        var lastLostTime = await db.Incidents.AsNoTracking().Where(i => i.LostDays > 0).OrderByDescending(i => i.OccurredAt).Select(i => (DateTimeOffset?)i.OccurredAt).FirstOrDefaultAsync(cancellationToken);

        return new HseStatsDto(
            incidents.Count,
            incidents.Count(i => i.Type is IncidentType.Accident or IncidentType.VehicleAccident),
            incidents.Count(i => i.Type == IncidentType.NearMiss),
            incidents.Sum(i => i.LostDays ?? 0),
            open.Count,
            open.Sum(i => i.Actions.Count(a => a.Status != ActionStatus.Done)),
            inspections.Count,
            inspections.Count == 0 ? 0 : Math.Round(inspections.Average(i => i.Score), 1),
            inspections.Count(i => i.CriticalFindings > 0),
            talks.Count,
            talks.Sum(a => a.Count),
            lastLostTime is null ? -1 : (int)(clock.UtcNow - lastLostTime.Value).TotalDays,
            incidents.GroupBy(i => i.Type).Select(g => new KpiDto(g.Key.ToString(), g.Count())).ToList());
    }
}
