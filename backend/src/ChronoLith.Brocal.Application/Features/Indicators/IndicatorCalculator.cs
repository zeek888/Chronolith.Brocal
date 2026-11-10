using System.Globalization;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Hse;

namespace ChronoLith.Brocal.Application.Features.Indicators;

/// <summary>
/// One required document of a person or unit deployed at the subject: the latest one captured of its type (or none).
/// The traffic light follows the company of the record, like the compliance matrix of Brocal.
/// </summary>
public sealed record DocumentCell(string HolderCode, string TypeName, bool Captured, DateOnly? ExpiresOn, int WarningDays, DateOnly Today)
{
    public ComplianceStatus Status => Captured ? ComplianceRules.Evaluate(ExpiresOn, Today, WarningDays) : ComplianceStatus.Missing;
}

/// <summary>What Brocal knows of one well or facility of the Shell, adding up the sites of every company linked to it.</summary>
public sealed record SubjectFacts(
    string SubjectType,
    Guid SubjectId,
    bool IsActive,
    int OpenWorkOrders,
    int UnsignedTickets,
    IReadOnlyDictionary<DateOnly, int> TicketsByDay,
    IReadOnlyDictionary<DateOnly, int> IncidentsByDay,
    int OpenIncidents,
    IncidentSeverity? WorstOpenIncident,
    int DeployedEmployees,
    int DeployedUnits,
    IReadOnlyList<DocumentCell> Documents);

/// <summary>
/// A subject that stopped belonging to Brocal (no site links to it, only inactive sites or companies that are not
/// operational, or the element is inactive in the replica): its values are retired from the Shell.
/// </summary>
public sealed record RetiredSubject(string SubjectType, Guid SubjectId);

public sealed record IndicatorDataSet(IReadOnlyList<SubjectFacts> Subjects, IReadOnlyList<RetiredSubject> Retired);

/// <summary>One element of <c>values</c> in <c>PUT /api/indicators/values</c>.</summary>
public sealed record IndicatorValue(
    string Indicator,
    string SubjectType,
    Guid SubjectId,
    DateOnly Period,
    decimal? Value,
    decimal? Planned,
    DateOnly? Date,
    string? Text,
    string? Severity,
    string? Note,
    string? Link,
    DateTime ObservedAt);

/// <summary>Turns the facts of a subject into the values of the day (pure: no database, no clock).</summary>
public static class IndicatorCalculator
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    /// <param name="today">Business day of the snapshot indicators.</param>
    /// <param name="days">Days of the daily indicators to (re)publish; today is always included, future days never.</param>
    public static IReadOnlyList<IndicatorValue> Calculate(SubjectFacts facts, DateOnly today, IEnumerable<DateOnly> days, DateTimeOffset observedAt, IndicatorPublishingSettings settings)
    {
        var observed = observedAt.UtcDateTime;
        var values = new List<IndicatorValue>();

        IndicatorValue Number(string key, DateOnly period, decimal value, string? severity = null, string? note = null, string? path = null) =>
            new(key, facts.SubjectType, facts.SubjectId, period, value, null, null, null, severity, note, Link(settings, path), observed);

        // ---- Documents of the personnel and units deployed (open work orders) ----
        var statuses = facts.Documents.Select(d => d.Status).ToList();
        var valid = statuses.Count(s => s is ComplianceStatus.Valid or ComplianceStatus.NoExpiry);
        var expiring = statuses.Count(s => s == ComplianceStatus.ExpiringSoon);
        var expired = statuses.Count(s => s == ComplianceStatus.Expired);
        var missing = statuses.Count(s => s == ComplianceStatus.Missing);
        const string documentsPath = "personal/matriz";

        values.Add(Number(BrocalIndicators.DocumentsValid, today, valid, path: documentsPath));
        values.Add(Number(BrocalIndicators.DocumentsExpiring, today, expiring, expiring > 0 ? "warning" : null, path: documentsPath));
        values.Add(Number(BrocalIndicators.DocumentsExpired, today, expired, expired > 0 ? "error" : null, path: documentsPath));
        values.Add(Number(BrocalIndicators.DocumentsMissing, today, missing, missing > 0 ? "warning" : null, path: documentsPath));

        if (statuses.Count > 0)
        {
            var compliance = Math.Round((decimal)(valid + expiring) * 100m / statuses.Count, 1);
            var severity = expired > 0 ? "error" : expiring + missing > 0 ? "warning" : "success";
            values.Add(Number(BrocalIndicators.DocumentsCompliance, today, compliance, severity, DeployedNote(facts), documentsPath));
        }
        else
        {
            // Nothing required (no one deployed): always published, so the last percentage (and its severity) does not
            // stay current in the Shell after the orders close.
            values.Add(Number(BrocalIndicators.DocumentsCompliance, today, 100m, null, NothingDeployedNote, documentsPath));
        }

        // Without a next expiry nothing is published: the publisher retires the indicator (a date cannot be null).
        var next = NextExpiry(facts);
        if (next is not null)
        {
            values.Add(new IndicatorValue(BrocalIndicators.DocumentsNextExpiry, facts.SubjectType, facts.SubjectId, today, null, null, next.ExpiresOn, null,
                next.Status == ComplianceStatus.ExpiringSoon ? "warning" : null, Clip($"{next.TypeName} · {next.HolderCode}", 300), Link(settings, documentsPath), observed));
        }

        // ---- Tickets, work orders and HSE ----
        foreach (var day in DaysToPublish(days, today))
        {
            values.Add(Number(BrocalIndicators.TicketsServices, day, facts.TicketsByDay.GetValueOrDefault(day), path: "boletas"));
            values.Add(Number(BrocalIndicators.HseIncidents, day, facts.IncidentsByDay.GetValueOrDefault(day), path: "hse/incidentes"));
        }

        values.Add(Number(BrocalIndicators.TicketsUnsigned, today, facts.UnsignedTickets, path: "boletas"));
        values.Add(Number(BrocalIndicators.WorkOrdersOpen, today, facts.OpenWorkOrders, path: "ordenes"));

        var openSeverity = facts.OpenIncidents == 0 ? null : facts.WorstOpenIncident >= IncidentSeverity.High ? "error" : "warning";
        values.Add(Number(BrocalIndicators.HseOpenIncidents, today, facts.OpenIncidents, openSeverity, path: "hse/incidentes"));

        return values;
    }

    /// <summary>Today plus the days that changed, never in the future (the current value is the latest period).</summary>
    public static IReadOnlyList<DateOnly> DaysToPublish(IEnumerable<DateOnly> days, DateOnly today) =>
        days.Where(d => d <= today && d >= today.AddYears(-3)).Append(today).Distinct().Order().ToList();

    /// <summary>Note of <c>documents.compliance</c> when nothing is required.</summary>
    public const string NothingDeployedNote = "Sin personal ni unidades desplegados";

    /// <summary>The valid (or expiring) document that expires first; null when none has an expiry.</summary>
    public static DocumentCell? NextExpiry(SubjectFacts facts) => facts.Documents
        .Where(d => d.Status is ComplianceStatus.Valid or ComplianceStatus.ExpiringSoon && d.ExpiresOn is not null)
        .OrderBy(d => d.ExpiresOn)
        .FirstOrDefault();

    private static string DeployedNote(SubjectFacts facts) =>
        string.Create(Mx, $"{facts.DeployedEmployees} persona(s) y {facts.DeployedUnits} unidad(es) en {facts.OpenWorkOrders} orden(es) abierta(s)");

    private static string? Link(IndicatorPublishingSettings settings, string? path) =>
        settings.LinkBase is null || path is null ? null : settings.LinkBase + path;

    private static string Clip(string text, int max) => text.Length > max ? text[..max] : text;
}
