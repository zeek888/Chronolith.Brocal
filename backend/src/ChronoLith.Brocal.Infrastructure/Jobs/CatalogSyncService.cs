using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.SharedCatalogs;
using ChronoLith.Brocal.Infrastructure.Notifications;
using ChronoLith.Brocal.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Jobs;

/// <summary>Polls the change feed of the shared catalogs every <see cref="JobsOptions.CatalogFeedMinutes"/> (and sends the outbox to the master first).</summary>
internal sealed class CatalogSyncService(ICatalogSyncRunner runner, ILogger<CatalogSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the API finish starting (migrations, registration) before the first poll.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(runner.IntervalMinutes));
        do
        {
            try
            {
                var summary = await runner.RunAsync(fullReload: false, stoppingToken);
                if (summary is { } s && (s.Applied > 0 || s.Errors > 0 || s.Pushed > 0 || s.FullSync))
                {
                    logger.LogInformation("Catálogos compartidos: {Applied} cambios aplicados, {Errors} con error, {Pushed} envíos al maestro, cursor {Cursor}{Full}.",
                        s.Applied, s.Errors, s.Pushed, s.Cursor, s.FullSync ? " (carga completa)" : string.Empty);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Error al sincronizar los catálogos compartidos de ChronoLith");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// One synchronization: pending pushes to the master with the service account, then the feed. A PostgreSQL advisory
/// lock keeps a single run at a time across API instances; a failure is recorded in the feed state for the screen.
/// </summary>
internal sealed class CatalogSyncRunner(IServiceScopeFactory scopeFactory, IOptions<JobsOptions> options, TimeProvider timeProvider, ILogger<CatalogSyncRunner> logger) : ICatalogSyncRunner
{
    private const long LockKey = 7_100_421_003;

    public bool BackgroundEnabled => options.Value.Enabled;

    public int IntervalMinutes => Math.Clamp(options.Value.CatalogFeedMinutes, 1, 60);

    public async Task<CatalogSyncRunSummary?> RunAsync(bool fullReload, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<CatalogFeedConsumer>();
        if (!consumer.Enabled)
        {
            return null;
        }

        var db = scope.ServiceProvider.GetRequiredService<BrocalDbContext>();
        if (!await AdvisoryLock.TryAcquireAsync(db, LockKey, cancellationToken))
        {
            return null;
        }

        try
        {
            var pushed = await scope.ServiceProvider.GetRequiredService<MasterCatalogSync>().DispatchPendingAsync(cancellationToken);
            var result = await consumer.RunAsync(fullReload, cancellationToken);
            return new CatalogSyncRunSummary(result.Pages, result.Applied, result.Errors, pushed, result.FullSync, result.Cursor);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "La sincronización de catálogos compartidos falló");
            await RecordErrorAsync(ex.Message, cancellationToken);
            throw;
        }
        finally
        {
            await AdvisoryLock.ReleaseAsync(db, LockKey);
        }
    }

    private async Task RecordErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var replica = scope.ServiceProvider.GetRequiredService<ICatalogReplica>();
            var state = await replica.GetStateAsync(cancellationToken);
            state.RecordError(message, timeProvider.GetUtcNow());
            await replica.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "No se pudo guardar el error de la sincronización de catálogos");
        }
    }
}
