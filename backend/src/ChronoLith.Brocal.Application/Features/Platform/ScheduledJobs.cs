using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Platform;

/// <summary>Result of the reviews of one company in a run (notices by kind, or the error that stopped them).</summary>
public sealed record JobCompanyResultDto(string Slug, string Name, IReadOnlyDictionary<string, int> Counts, string? Error);

public sealed record ScheduledJobRunDto(
    Guid Id, string Job, string TriggeredBy, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int Companies, int Notices, int Failures, IReadOnlyList<JobCompanyResultDto> Details)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ScheduledJobRunDto From(ScheduledJobRun run) => new(
        run.Id, run.Job, run.TriggeredBy, run.StartedAt, run.FinishedAt, run.Companies, run.Notices, run.Failures,
        string.IsNullOrEmpty(run.Details) ? [] : JsonSerializer.Deserialize<List<JobCompanyResultDto>>(run.Details, Json) ?? []);
}

/// <summary>The scheduled reviews of every operational company (expiries, reminders, approvals past their deadline).</summary>
public interface IScheduledJobsRunner
{
    /// <summary>Background runs enabled in this instance (<c>Jobs:Enabled</c>).</summary>
    bool BackgroundEnabled { get; }

    int IntervalMinutes { get; }

    /// <summary>Runs the reviews now and records the run; null when another run (this or another instance) is in progress.</summary>
    Task<ScheduledJobRunDto?> RunAsync(string triggeredBy, CancellationToken cancellationToken);
}

public sealed record ScheduledJobsDto(bool BackgroundEnabled, int IntervalMinutes, DateTimeOffset? NextRunAt, IReadOnlyList<ScheduledJobRunDto> Runs);

public sealed record GetScheduledJobsQuery(int Take = 30) : IQuery<ScheduledJobsDto>;

internal sealed class GetScheduledJobsQueryHandler(IApplicationDbContext db, IScheduledJobsRunner runner) : IQueryHandler<GetScheduledJobsQuery, ScheduledJobsDto>
{
    public async Task<Result<ScheduledJobsDto>> Handle(GetScheduledJobsQuery query, CancellationToken cancellationToken)
    {
        var runs = await db.ScheduledJobRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(Math.Clamp(query.Take, 1, 200)).ToListAsync(cancellationToken);
        var lastScheduled = runs.FirstOrDefault(r => r.TriggeredBy == ScheduledJobsTexts.Scheduled);
        var next = runner.BackgroundEnabled && lastScheduled is not null ? lastScheduled.StartedAt.AddMinutes(runner.IntervalMinutes) : (DateTimeOffset?)null;
        return new ScheduledJobsDto(runner.BackgroundEnabled, runner.IntervalMinutes, next, runs.Select(ScheduledJobRunDto.From).ToList());
    }
}

/// <summary>"Ejecutar ahora" of the platform administrator.</summary>
public sealed record RunScheduledJobsCommand : ICommand<ScheduledJobRunDto>;

internal sealed class RunScheduledJobsCommandHandler(IScheduledJobsRunner runner, IUserContext user) : ICommandHandler<RunScheduledJobsCommand, ScheduledJobRunDto>
{
    public async Task<Result<ScheduledJobRunDto>> Handle(RunScheduledJobsCommand command, CancellationToken cancellationToken) =>
        await runner.RunAsync(user.DisplayName, cancellationToken) is { } run
            ? run
            : Error.Conflict("Jobs.Running", "Ya hay una revisión en curso; intenta en unos minutos.");
}

public static class ScheduledJobsTexts
{
    /// <summary><see cref="ScheduledJobRun.TriggeredBy"/> of the runs started by the timer.</summary>
    public const string Scheduled = "Programada";
}
