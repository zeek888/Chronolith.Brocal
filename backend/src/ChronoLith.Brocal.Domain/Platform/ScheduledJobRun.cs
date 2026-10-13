using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Platform;

/// <summary>
/// One run of the scheduled reviews (expiries, reminders, pending approvals) over every operational company:
/// when it ran, who started it, how many notices it produced and the detail per company. Platform level (no tenant).
/// </summary>
public sealed class ScheduledJobRun : Entity
{
    public const string RemindersJob = "reminders";

    private ScheduledJobRun()
    {
    }

    public ScheduledJobRun(string job, string triggeredBy, DateTimeOffset startedAt)
    {
        Job = job;
        TriggeredBy = triggeredBy.Length > 150 ? triggeredBy[..150] : triggeredBy;
        StartedAt = startedAt;
    }

    public string Job { get; private set; } = null!;

    /// <summary>"Programada" or the name of the administrator who ran it.</summary>
    public string TriggeredBy { get; private set; } = null!;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public int Companies { get; private set; }

    public int Notices { get; private set; }

    public int Failures { get; private set; }

    /// <summary>JSON array with the result of each company (counts by kind of notice or the error).</summary>
    public string? Details { get; private set; }

    public void Finish(DateTimeOffset at, int companies, int notices, int failures, string details)
    {
        FinishedAt = at;
        Companies = companies;
        Notices = notices;
        Failures = failures;
        Details = details;
    }
}
