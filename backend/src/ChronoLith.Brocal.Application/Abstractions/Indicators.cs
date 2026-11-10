using ChronoLith.Brocal.Application.Features.Indicators;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Application.Abstractions;

public enum IndicatorSendOutcome
{
    /// <summary>Not connected to a Shell.</summary>
    Disabled,

    Sent,

    /// <summary>
    /// 404: nothing to retire (a DELETE of values that were never published) or, on a publication, the Shell does not
    /// know the source (its definitions were lost): register again.
    /// </summary>
    NotFound,

    /// <summary>400 (undeclared indicator, unknown subject, value of the wrong type): logged and discarded.</summary>
    Rejected,

    /// <summary>The Shell did not answer, failed or refused the token: retried later.</summary>
    Unavailable
}

public sealed record IndicatorSendResult(IndicatorSendOutcome Outcome, string? Detail = null);

/// <summary>Calls of the indicator API of the Shell with the service account of Brocal (<c>chronolith-module-registrar</c>).</summary>
public interface IIndicatorClient
{
    /// <summary><c>Shell:ApiUrl</c> and the service account of the realm <c>chronolith</c> are configured.</summary>
    bool IsConfigured { get; }

    /// <summary><c>PUT /api/indicators/sources/brocal</c>.</summary>
    Task<IndicatorSendResult> RegisterAsync(string definitionsJson, CancellationToken cancellationToken);

    /// <summary><c>PUT /api/indicators/values</c>.</summary>
    Task<IndicatorSendResult> PublishAsync(string valuesJson, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/indicators/values/{source}/{key}/{type}/{id}</c> (relative path).</summary>
    Task<IndicatorSendResult> RetireAsync(string path, CancellationToken cancellationToken);
}

/// <summary>Storage of the indicator outbox (global table, outside the tenant filter).</summary>
public interface IIndicatorOutbox
{
    void Add(IndicatorMessage message);

    /// <summary>Pending recalculations, oldest first.</summary>
    Task<IReadOnlyList<IndicatorMessage>> PendingRecalculationsAsync(int take, CancellationToken cancellationToken);

    /// <summary>Pending publications and retirements, oldest first (delivered in this order).</summary>
    Task<IReadOnlyList<IndicatorMessage>> PendingDeliveriesAsync(int take, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Deletes the finished messages (done, rejected or failed) created before <paramref name="before"/>; pending ones are kept.</summary>
    Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken);

    /// <summary>Subjects published or retired before (tracked: changes are saved with <see cref="SaveChangesAsync"/>); all of them with null.</summary>
    Task<IReadOnlyDictionary<Guid, IndicatorSubject>> SubjectsAsync(IReadOnlyCollection<Guid>? subjectIds, CancellationToken cancellationToken);

    void AddSubject(IndicatorSubject subject);
}

/// <summary>Reads, across every company, what the indicators of the subjects of a change set are calculated from.</summary>
public interface IIndicatorDataSource
{
    Task<IndicatorDataSet> LoadAsync(IndicatorChangeSet changes, DateOnly today, CancellationToken cancellationToken);
}

/// <summary>
/// How the values are presented in the Shell: where <c>link</c> points (<c>/brocal/</c> in module mode, the public
/// <c>https://</c> URL of Brocal standalone, nothing otherwise) and the permission that hides the indicators.
/// </summary>
public sealed record IndicatorPublishingSettings(string? LinkBase, string? PermissionCode);
