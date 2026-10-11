using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Notifications;
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

    /// <summary>Background jobs (outbox delivery and daily scans). Disable them in secondary replicas or tests.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How often the reminders scan runs (it is idempotent thanks to deduplication keys).</summary>
    public int ScanIntervalMinutes { get; init; } = 60;
}

/// <summary>Runs the daily scans of every operational company inside its tenant scope.</summary>
internal sealed class ScheduledJobsService(IServiceScopeFactory scopeFactory, IOptions<JobsOptions> options, TimeProvider timeProvider, ILogger<ScheduledJobsService> logger)
    : BackgroundService
{
    private const long LockKey = 7_100_421_002;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the API finish starting (migrations, registration) before the first scan.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, options.Value.ScanIntervalMinutes)));
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Error en el escaneo diario de recordatorios");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var lockScope = scopeFactory.CreateAsyncScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<BrocalDbContext>();
        if (!await AdvisoryLock.TryAcquireAsync(lockDb, LockKey, cancellationToken))
        {
            return;
        }

        try
        {
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            var tenants = await lockDb.Tenants.AsNoTracking().ToListAsync(cancellationToken);
            foreach (var tenant in tenants.Where(t => t.IsOperational(today)))
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                using (tenantContext.BeginScope(TenantSnapshot.From(tenant)))
                {
                    var reminders = await scope.ServiceProvider.GetRequiredService<IDailyScanService>().RunAsync(cancellationToken);
                    if (reminders > 0)
                    {
                        logger.LogInformation("Recordatorios de {Tenant}: {Count}", tenant.Slug, reminders);
                    }
                }
            }
        }
        finally
        {
            await AdvisoryLock.ReleaseAsync(lockDb, LockKey);
        }
    }
}
