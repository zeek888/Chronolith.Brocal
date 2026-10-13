using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Notifications;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Notifications;
using ChronoLith.Brocal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Jobs;

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Background jobs (outbox delivery and scheduled reviews). Disable them in secondary replicas or tests.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How often the reviews run (they are idempotent thanks to deduplication keys).</summary>
    public int ScanIntervalMinutes { get; init; } = 60;
}

/// <summary>Runs the scheduled reviews every <see cref="JobsOptions.ScanIntervalMinutes"/>.</summary>
internal sealed class ScheduledJobsService(IScheduledJobsRunner runner, ILogger<ScheduledJobsService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the API finish starting (migrations, registration) before the first run.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(runner.IntervalMinutes));
        do
        {
            try
            {
                await runner.RunAsync(ScheduledJobsTexts.Scheduled, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Error en las revisiones programadas");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// Reviews every operational company inside its tenant scope (expiries, reminders, approvals past their deadline)
/// and records the run. A PostgreSQL advisory lock keeps a single run at a time across API instances; a failure in
/// one company does not stop the others.
/// </summary>
internal sealed class ScheduledJobsRunner(IServiceScopeFactory scopeFactory, IOptions<JobsOptions> options, TimeProvider timeProvider, ILogger<ScheduledJobsRunner> logger)
    : IScheduledJobsRunner
{
    private const long LockKey = 7_100_421_002;

    /// <summary>Runs kept in the log.</summary>
    private const int KeptRuns = 500;

    public bool BackgroundEnabled => options.Value.Enabled;

    public int IntervalMinutes => Math.Max(5, options.Value.ScanIntervalMinutes);

    public async Task<ScheduledJobRunDto?> RunAsync(string triggeredBy, CancellationToken cancellationToken)
    {
        await using var lockScope = scopeFactory.CreateAsyncScope();
        var db = lockScope.ServiceProvider.GetRequiredService<BrocalDbContext>();
        if (!await AdvisoryLock.TryAcquireAsync(db, LockKey, cancellationToken))
        {
            return null;
        }

        try
        {
            var started = timeProvider.GetUtcNow();
            var today = DateOnly.FromDateTime(started.UtcDateTime);
            var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
            var results = new List<JobCompanyResultDto>();

            foreach (var tenant in tenants.Where(t => t.IsOperational(today)))
            {
                results.Add(await RunCompanyAsync(tenant, cancellationToken));
            }

            var run = new ScheduledJobRun(ScheduledJobRun.RemindersJob, triggeredBy, started);
            run.Finish(timeProvider.GetUtcNow(), results.Count, results.Sum(r => r.Counts.Values.Sum()), results.Count(r => r.Error is not null),
                JsonSerializer.Serialize(results, ScheduledJobRunDto.Json));
            db.ScheduledJobRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);

            var obsolete = await db.ScheduledJobRuns.OrderByDescending(r => r.StartedAt).Skip(KeptRuns).Select(r => r.Id).ToListAsync(cancellationToken);
            if (obsolete.Count > 0)
            {
                await db.ScheduledJobRuns.Where(r => obsolete.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
            }

            if (run.Notices > 0 || run.Failures > 0)
            {
                logger.LogInformation("Revisión programada: {Companies} empresas, {Notices} avisos, {Failures} con error", run.Companies, run.Notices, run.Failures);
            }

            return ScheduledJobRunDto.From(run);
        }
        finally
        {
            await AdvisoryLock.ReleaseAsync(db, LockKey);
        }
    }

    private async Task<JobCompanyResultDto> RunCompanyAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using (tenantContext.BeginScope(TenantSnapshot.From(tenant)))
            {
                var result = await scope.ServiceProvider.GetRequiredService<IDailyScanService>().RunAsync(cancellationToken);
                return new JobCompanyResultDto(tenant.Slug, tenant.Name, result.Counts, null);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Error en la revisión programada de {Tenant}", tenant.Slug);
            return new JobCompanyResultDto(tenant.Slug, tenant.Name, new Dictionary<string, int>(), ex.Message);
        }
    }
}
