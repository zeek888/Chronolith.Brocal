using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Platform;

public enum IndicatorMessageKind
{
    /// <summary>
    /// Something that feeds the indicators changed (ticket, work order, incident, document, linked site…): the
    /// affected subjects are recalculated later and turned into <see cref="Publish"/> or <see cref="Retire"/> messages.
    /// Written in the same transaction as the business change.
    /// </summary>
    Recalculate = 0,

    /// <summary>Body of <c>PUT /api/indicators/values</c> (up to 500 values).</summary>
    Publish = 1,

    /// <summary>Path of <c>DELETE /api/indicators/values/brocal/{key}/{type}/{id}</c>.</summary>
    Retire = 2
}

public enum IndicatorMessageStatus
{
    Pending = 0,

    /// <summary>Delivered (or, for a recalculation, turned into deliveries).</summary>
    Done = 1,

    /// <summary>The Shell answered 400: logged and discarded, never retried (like the well timeline).</summary>
    Rejected = 2,

    /// <summary>
    /// Retries exhausted (only publications of today's values; <see cref="IndicatorMessage.KeepRetrying"/> ones never
    /// fail): the next recalculation or the daily republication publishes the subject again.
    /// </summary>
    Failed = 3
}

/// <summary>
/// Outbox of the indicators that Brocal publishes to the ChronoLith Shell (<c>docs/indicators.md</c> of ChronoLith).
/// Global table (not <c>ITenantOwned</c>): the values of a well or facility add up every company of the installation.
/// Messages are delivered in creation order; a failure stops the round so a later retirement never overtakes an
/// earlier publication.
/// </summary>
public sealed class IndicatorMessage : Entity
{
    public const int MaxAttempts = 10;

    public const int MaxErrorLength = 1000;

    private IndicatorMessage()
    {
    }

    private IndicatorMessage(IndicatorMessageKind kind, string payload, int items, DateTimeOffset createdAt, bool keepRetrying)
    {
        Kind = kind;
        Payload = payload;
        Items = items;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
        KeepRetrying = keepRetrying;
    }

    public IndicatorMessageKind Kind { get; private set; }

    /// <summary>
    /// Retried until the Shell takes it, without <see cref="MaxAttempts"/>: retirements and publications of past days,
    /// which no later republication would repeat (the daily one only publishes today).
    /// </summary>
    public bool KeepRetrying { get; private set; }

    /// <summary>JSON of the change set (recalculation), of the values (publication) or the DELETE path (retirement).</summary>
    public string Payload { get; private set; } = null!;

    /// <summary>Values in the publication (1 for the other kinds), for logs and the administration.</summary>
    public int Items { get; private set; }

    public IndicatorMessageStatus Status { get; private set; } = IndicatorMessageStatus.Pending;

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? LastError { get; private set; }

    public static IndicatorMessage Recalculate(string changeSetJson, DateTimeOffset now) => new(IndicatorMessageKind.Recalculate, changeSetJson, 1, now, false);

    /// <param name="keepRetrying">The batch carries days before today: retried until delivered.</param>
    public static IndicatorMessage Publish(string valuesJson, int values, DateTimeOffset now, bool keepRetrying = false) =>
        new(IndicatorMessageKind.Publish, valuesJson, values, now, keepRetrying);

    /// <summary>Retirements are retried until delivered: nothing else would clean the values of the subject in the Shell.</summary>
    public static IndicatorMessage Retire(string path, DateTimeOffset now) => new(IndicatorMessageKind.Retire, path, 1, now, true);

    public void MarkDone(DateTimeOffset now)
    {
        if (Kind != IndicatorMessageKind.Recalculate)
        {
            Attempts++;
        }

        Status = IndicatorMessageStatus.Done;
        CompletedAt = now;
        LastError = null;
    }

    /// <summary>The Shell rejected the request (400): it would fail again, so it is not retried.</summary>
    public void MarkRejected(DateTimeOffset now, string error)
    {
        Attempts++;
        Status = IndicatorMessageStatus.Rejected;
        CompletedAt = now;
        LastError = Clip(error);
    }

    /// <summary>
    /// The Shell did not answer or refused the token: exponential back-off (1, 2, 4… up to 30 minutes) until
    /// <see cref="MaxAttempts"/>, or for ever with <see cref="KeepRetrying"/>.
    /// </summary>
    public void MarkFailed(DateTimeOffset now, string error)
    {
        Attempts++;
        LastError = Clip(error);
        if (Attempts >= MaxAttempts && !KeepRetrying)
        {
            Status = IndicatorMessageStatus.Failed;
            CompletedAt = now;
            return;
        }

        NextAttemptAt = now.AddMinutes(Math.Min(30, Math.Pow(2, Math.Min(Attempts, 16) - 1)));
    }

    /// <summary>
    /// The Shell rejected a batch of several subjects (400): it is replaced by one message per subject, with the same
    /// creation time so they keep their place in the queue, and only the culprit is discarded.
    /// </summary>
    public void MarkSplit(DateTimeOffset now, string error)
    {
        Attempts++;
        Status = IndicatorMessageStatus.Rejected;
        CompletedAt = now;
        LastError = Clip($"Dividido por sujeto: {error}");
    }

    private static string Clip(string text) => text.Length > MaxErrorLength ? text[..MaxErrorLength] : text;
}

/// <summary>
/// A well or facility of the Shell Brocal has published indicators for (or retired them from). With it the daily
/// recalculation retires the subjects that stopped belonging to Brocal even when the retirement of the moment was lost,
/// and a retirement is not repeated every day. Global table, like the outbox.
/// </summary>
public sealed class IndicatorSubject : Entity
{
    public const int MaxTypeLength = 20;

    private IndicatorSubject()
    {
    }

    private IndicatorSubject(Guid subjectId, string subjectType)
        : base(subjectId) => SubjectType = subjectType;

    /// <summary><c>well</c> or <c>facility</c>.</summary>
    public string SubjectType { get; private set; } = null!;

    /// <summary>Its values are in the Shell (false: their retirement was queued).</summary>
    public bool IsPublished { get; private set; }

    /// <summary><c>documents.next-expiry</c> was published last time (it is retired when there is no next expiry).</summary>
    public bool HasNextExpiry { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static IndicatorSubject Create(Guid subjectId, string subjectType, DateTimeOffset now) =>
        new(subjectId, subjectType) { UpdatedAt = now };

    public void MarkPublished(string subjectType, bool hasNextExpiry, DateTimeOffset now)
    {
        SubjectType = subjectType;
        IsPublished = true;
        HasNextExpiry = hasNextExpiry;
        UpdatedAt = now;
    }

    public void MarkRetired(DateTimeOffset now)
    {
        IsPublished = false;
        HasNextExpiry = false;
        UpdatedAt = now;
    }
}
