using ChronoLith.Brocal.Application.Features.Indicators;
using ChronoLith.Brocal.Infrastructure.Notifications;
using ChronoLith.Brocal.Infrastructure.Persistence;
using ChronoLith.Brocal.Infrastructure.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Jobs;

/// <summary>
/// Publishes the indicators to the ChronoLith Shell every <see cref="IndicatorOptions.DispatchSeconds"/>: declares the
/// definitions at startup (retried on every tick until the Shell takes them, again every day and as soon as the Shell
/// answers 404 to a publication because it lost the source), queues the daily
/// republication once per business day and processes the outbox. It never blocks the startup of the API.
/// </summary>
internal sealed class IndicatorPublishingService(IndicatorPublishingRunner runner, IOptions<IndicatorOptions> options, ILogger<IndicatorPublishingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the API finish starting (migrations, the first poll of the shared catalogs) before the first run.
        await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(options.Value.DispatchSeconds, 10, 3600)));
        do
        {
            try
            {
                await runner.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Error al publicar los indicadores de Brocal en el Shell");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// One round of the indicator publication. A PostgreSQL advisory lock keeps a single round at a time across API
/// instances. The registration and the daily republication are remembered per process: a restart registers and
/// republishes again, which is harmless because both are idempotent in the Shell.
/// </summary>
internal sealed class IndicatorPublishingRunner(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<IndicatorPublishingRunner> logger)
{
    private const long LockKey = 7_100_421_004;

    private DateOnly? _registeredOn;
    private DateOnly? _republishedOn;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IndicatorPublisher>();
        if (!publisher.Enabled)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<BrocalDbContext>();
        if (!await AdvisoryLock.TryAcquireAsync(db, LockKey, cancellationToken))
        {
            return;
        }

        try
        {
            var today = BusinessCalendar.Today(timeProvider);
            if (_registeredOn != today && await publisher.RegisterAsync(cancellationToken))
            {
                _registeredOn = today;
            }

            if (_republishedOn != today)
            {
                await publisher.EnqueueDailyAsync(cancellationToken);
                var purged = await publisher.PurgeAsync(cancellationToken);
                _republishedOn = today;
                logger.LogInformation("Indicadores de Brocal: republicación del {Day} en cola ({Purged} mensajes viejos depurados).", today, purged);
            }

            var queued = await publisher.PrepareAsync(cancellationToken);

            // Values of undeclared indicators would be rejected (400): deliver only after the definitions were taken.
            if (_registeredOn is null)
            {
                return;
            }

            var summary = await publisher.DeliverAsync(cancellationToken);
            if (summary.NeedsRegistration)
            {
                // The Shell lost the source (404 to a publication): declare the definitions again in the next round
                // before delivering anything else.
                _registeredOn = null;
            }

            if (queued > 0 || summary.Sent + summary.Rejected + summary.Failed > 0)
            {
                logger.LogInformation("Indicadores de Brocal: {Queued} valores calculados, {Sent} envíos, {Rejected} rechazados, {Failed} con error, {Pending} pendientes.",
                    queued, summary.Sent, summary.Rejected, summary.Failed, summary.Pending);
            }
        }
        finally
        {
            await AdvisoryLock.ReleaseAsync(db, LockKey);
        }
    }
}
