using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.Extensions.Logging;

namespace ChronoLith.Brocal.Application.Features.Indicators;

/// <param name="NeedsRegistration">The Shell answered 404 to a publication (it lost the source): register the definitions before delivering again.</param>
public sealed record IndicatorDeliverySummary(int Sent, int Rejected, int Failed, int Pending, bool NeedsRegistration = false);

/// <summary>
/// Publishes the indicators of Brocal to the ChronoLith Shell through the outbox (<c>indicator_messages</c>):
/// <list type="number">
/// <item>the persistence layer writes a <see cref="IndicatorMessageKind.Recalculate"/> marker in the same transaction
/// as the business change (and the daily job one for every active subject);</item>
/// <item><see cref="PrepareAsync"/> recalculates the subjects of the pending markers and stores the requests
/// (<c>PUT /api/indicators/values</c> in batches of up to 500 values that never split a subject, <c>DELETE</c> for the
/// subjects that stopped belonging to Brocal and for <c>documents.next-expiry</c> when there is none); the subjects
/// published or retired are remembered in <c>indicator_subjects</c>, so the daily run (<c>All</c>) also retires the
/// ones published before that are not publishable anymore;</item>
/// <item><see cref="DeliverAsync"/> sends them in order with retries; a 400 is logged and discarded (split by subject
/// first when the request carried several), a 404 to a publication asks for registering the definitions again.</item>
/// </list>
/// </summary>
public sealed class IndicatorPublisher(
    IIndicatorOutbox outbox,
    IIndicatorDataSource data,
    IIndicatorClient client,
    IndicatorPublishingSettings settings,
    TimeProvider clock,
    ILogger<IndicatorPublisher> logger)
{
    public const int BatchSize = 500;

    /// <summary>Recalculation markers merged in one pass (they coalesce: the values are read when preparing).</summary>
    private const int RecalculationsPerRun = 500;

    private const int DeliveriesPerRun = 50;

    public bool Enabled => client.IsConfigured;

    /// <summary>Declares the definitions (<c>PUT /api/indicators/sources/brocal</c>); idempotent. False when the Shell did not accept them.</summary>
    public async Task<bool> RegisterAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return false;
        }

        var result = await client.RegisterAsync(BrocalIndicators.DefinitionsJson(settings), cancellationToken);
        if (result.Outcome == IndicatorSendOutcome.Sent)
        {
            logger.LogInformation("Indicadores de Brocal registrados en el Shell ({Count} definiciones).", BrocalIndicators.All.Count);
            return true;
        }

        logger.LogWarning("El Shell no aceptó las definiciones de indicadores de Brocal ({Outcome}): {Detail}", result.Outcome, result.Detail);
        return false;
    }

    /// <summary>Queues a recalculation (used by the daily job; business changes are queued by the persistence layer).</summary>
    public async Task EnqueueAsync(IndicatorChangeSet changes, CancellationToken cancellationToken)
    {
        outbox.Add(IndicatorMessage.Recalculate(changes.ToJson(), clock.GetUtcNow()));
        await outbox.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Republication of the day: every active subject, even when nothing changed (the stale-data alert of the Shell depends on it).</summary>
    public Task EnqueueDailyAsync(CancellationToken cancellationToken) =>
        EnqueueAsync(new IndicatorChangeSet { All = true, Days = { BusinessCalendar.Today(clock) } }, cancellationToken);

    /// <summary>Turns the pending markers into publications and retirements (one transaction). Returns how many values were queued.</summary>
    public async Task<int> PrepareAsync(CancellationToken cancellationToken)
    {
        var pending = await outbox.PendingRecalculationsAsync(RecalculationsPerRun, cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }

        var changes = new IndicatorChangeSet();
        foreach (var message in pending)
        {
            changes.Merge(IndicatorChangeSet.FromJson(message.Payload));
        }

        var now = clock.GetUtcNow();
        var today = BusinessCalendar.Today(clock);
        var dataSet = await data.LoadAsync(changes, today, cancellationToken);

        var publishedIds = dataSet.Subjects.Select(s => s.SubjectId).ToHashSet();
        var tracked = new Dictionary<Guid, IndicatorSubject>(await outbox.SubjectsAsync(
            changes.All ? null : publishedIds.Concat(dataSet.Retired.Select(r => r.SubjectId)).Distinct().ToList(), cancellationToken));

        // ---- Publications: the values of a subject travel together, so a 400 can be narrowed down to it ----
        var count = 0;
        var batch = new List<IndicatorValue>();
        var retirements = new List<string>();
        foreach (var subject in dataSet.Subjects)
        {
            var values = IndicatorCalculator.Calculate(subject, today, changes.Days, now, settings);
            count += values.Count;
            if (batch.Count + values.Count > BatchSize)
            {
                Flush(batch, today, now);
            }

            if (values.Count > BatchSize)
            {
                // Years of daily values of one subject (historical recalculation): on its own, in several requests.
                foreach (var chunk in values.Chunk(BatchSize))
                {
                    Flush([.. chunk], today, now);
                }
            }
            else
            {
                batch.AddRange(values);
            }

            // documents.next-expiry is a date: without one, the last date published is retired instead of staying current.
            var hasNextExpiry = values.Any(v => v.Indicator == BrocalIndicators.DocumentsNextExpiry);
            var row = Track(tracked, subject.SubjectId, subject.SubjectType, now, out var isNew);
            if (!hasNextExpiry && (isNew || row.HasNextExpiry))
            {
                retirements.Add(BrocalIndicators.RetirePath(BrocalIndicators.DocumentsNextExpiry, subject.SubjectType, subject.SubjectId));
            }

            row.MarkPublished(subject.SubjectType, hasNextExpiry, now);
        }

        Flush(batch, today, now);

        // ---- Retirements: the subjects that stopped belonging to Brocal ----
        var retired = dataSet.Retired.ToList();
        if (changes.All)
        {
            // Published before and not anymore (unlinked, deleted from the Shell…): repairs a retirement that was lost.
            var known = retired.Select(r => r.SubjectId).ToHashSet();
            retired.AddRange(tracked.Values
                .Where(t => t.IsPublished && !publishedIds.Contains(t.Id) && !known.Contains(t.Id))
                .Select(t => new RetiredSubject(t.SubjectType, t.Id)));
        }

        foreach (var subject in retired)
        {
            if (tracked.TryGetValue(subject.SubjectId, out var row) && !row.IsPublished)
            {
                continue; // Already retired: the retirement is queued (and retried until delivered) or done.
            }

            foreach (var key in BrocalIndicators.Keys)
            {
                retirements.Add(BrocalIndicators.RetirePath(key, subject.SubjectType, subject.SubjectId));
            }

            Track(tracked, subject.SubjectId, subject.SubjectType, now, out _).MarkRetired(now);
        }

        foreach (var path in retirements)
        {
            outbox.Add(IndicatorMessage.Retire(path, now));
        }

        foreach (var message in pending)
        {
            message.MarkDone(now);
        }

        await outbox.SaveChangesAsync(cancellationToken);
        return count;
    }

    /// <summary>Queues the values gathered so far as one request; past days are retried until delivered.</summary>
    private void Flush(List<IndicatorValue> batch, DateOnly today, DateTimeOffset now)
    {
        if (batch.Count == 0)
        {
            return;
        }

        outbox.Add(IndicatorMessage.Publish(BrocalIndicators.ValuesJson(batch), batch.Count, now, batch.Any(v => v.Period < today)));
        batch.Clear();
    }

    /// <summary>The tracking row of a subject, created when Brocal never published nor retired it.</summary>
    private IndicatorSubject Track(Dictionary<Guid, IndicatorSubject> tracked, Guid subjectId, string subjectType, DateTimeOffset now, out bool isNew)
    {
        isNew = !tracked.TryGetValue(subjectId, out var row);
        if (row is not null)
        {
            return row;
        }

        row = IndicatorSubject.Create(subjectId, subjectType, now);
        outbox.AddSubject(row);
        tracked[subjectId] = row;
        return row;
    }

    /// <summary>
    /// Sends the pending publications and retirements, oldest first. The round stops at the first one the Shell does
    /// not take (or that waits for its next attempt), so the order of publications and retirements is kept.
    /// </summary>
    public async Task<IndicatorDeliverySummary> DeliverAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return new IndicatorDeliverySummary(0, 0, 0, 0);
        }

        var pending = await outbox.PendingDeliveriesAsync(DeliveriesPerRun, cancellationToken);
        int sent = 0, rejected = 0, failed = 0, processed = 0;
        var needsRegistration = false;
        foreach (var message in pending)
        {
            var now = clock.GetUtcNow();
            if (message.NextAttemptAt > now)
            {
                break;
            }

            var result = message.Kind == IndicatorMessageKind.Retire
                ? await client.RetireAsync(message.Payload, cancellationToken)
                : await client.PublishAsync(message.Payload, cancellationToken);
            processed++;
            now = clock.GetUtcNow();

            if (result.Outcome == IndicatorSendOutcome.Sent || result.Outcome == IndicatorSendOutcome.NotFound && message.Kind == IndicatorMessageKind.Retire)
            {
                // A DELETE answered 404 had nothing to retire: done as well.
                message.MarkDone(now);
                sent++;
                continue;
            }

            if (result.Outcome == IndicatorSendOutcome.Rejected)
            {
                rejected++;
                if (message.Kind == IndicatorMessageKind.Publish && BrocalIndicators.SplitBySubject(message.Payload) is { Count: > 1 } parts)
                {
                    // One unknown subject (deleted from the Shell…) must not discard the values of the others: one
                    // request per subject, in the same place of the queue; the round ends so they go before what follows.
                    logger.LogWarning("El Shell rechazó una publicación de indicadores de {Subjects} sujetos ({Items} valores); se reenvía por sujeto: {Detail}",
                        parts.Count, message.Items, result.Detail);
                    message.MarkSplit(now, result.Detail ?? "400");
                    foreach (var (json, items) in parts)
                    {
                        outbox.Add(IndicatorMessage.Publish(json, items, message.CreatedAt, message.KeepRetrying));
                    }

                    break;
                }

                logger.LogWarning("El Shell rechazó {Kind} de indicadores ({Items} valores); se descarta sin reintentar: {Detail}", message.Kind, message.Items, result.Detail);
                message.MarkRejected(now, result.Detail ?? "400");
                continue;
            }

            if (result.Outcome == IndicatorSendOutcome.Disabled)
            {
                processed--;
                break;
            }

            if (result.Outcome == IndicatorSendOutcome.NotFound)
            {
                // A publication answered 404: the Shell does not know the source (restored database, deleted source).
                // Not an attempt: the definitions are declared again before delivering anything else.
                logger.LogWarning("El Shell no reconoce la fuente de indicadores de Brocal; se registrarán otra vez las definiciones: {Detail}", result.Detail);
                processed--;
                needsRegistration = true;
                break;
            }

            message.MarkFailed(now, result.Detail ?? "sin respuesta");
            failed++;
            if (message.Status == IndicatorMessageStatus.Failed)
            {
                logger.LogWarning("Se agotaron los reintentos de {Kind} de indicadores ({Items} valores): {Detail}", message.Kind, message.Items, result.Detail);
                continue;
            }

            if (message.KeepRetrying && message.Attempts % IndicatorMessage.MaxAttempts == 0)
            {
                logger.LogWarning("{Kind} de indicadores ({Items} valores) sigue sin entregarse tras {Attempts} intentos; se reintentará cada 30 minutos: {Detail}",
                    message.Kind, message.Items, message.Attempts, result.Detail);
            }

            break;
        }

        if (processed > 0)
        {
            await outbox.SaveChangesAsync(cancellationToken);
        }

        return new IndicatorDeliverySummary(sent, rejected, failed, pending.Count - processed, needsRegistration);
    }

    /// <summary>
    /// Housekeeping: finished messages (done, rejected or failed) older than a week. Pending ones are never purged: a
    /// retirement or a past day would be lost for good.
    /// </summary>
    public Task<int> PurgeAsync(CancellationToken cancellationToken) => outbox.PurgeAsync(clock.GetUtcNow().AddDays(-7), cancellationToken);
}
