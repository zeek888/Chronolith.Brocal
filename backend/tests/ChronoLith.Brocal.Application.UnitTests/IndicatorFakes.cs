using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Indicators;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChronoLith.Brocal.Application.UnitTests;

/// <summary>Indicator outbox in memory (same ordering rules as the database store).</summary>
internal sealed class InMemoryIndicatorOutbox : IIndicatorOutbox
{
    public List<IndicatorMessage> Messages { get; } = [];

    public int Saves { get; private set; }

    public void Add(IndicatorMessage message) => Messages.Add(message);

    public Task<IReadOnlyList<IndicatorMessage>> PendingRecalculationsAsync(int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IndicatorMessage>>(Messages
            .Where(m => m.Kind == IndicatorMessageKind.Recalculate && m.Status == IndicatorMessageStatus.Pending)
            .OrderBy(m => m.CreatedAt).Take(take).ToList());

    public Task<IReadOnlyList<IndicatorMessage>> PendingDeliveriesAsync(int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IndicatorMessage>>(Messages
            .Where(m => m.Kind != IndicatorMessageKind.Recalculate && m.Status == IndicatorMessageStatus.Pending)
            .OrderBy(m => m.CreatedAt).Take(take).ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Task.FromResult(Messages.RemoveAll(m => m.CreatedAt < before && m.Status != IndicatorMessageStatus.Pending));

    public Dictionary<Guid, IndicatorSubject> Subjects { get; } = [];

    public Task<IReadOnlyDictionary<Guid, IndicatorSubject>> SubjectsAsync(IReadOnlyCollection<Guid>? subjectIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IndicatorSubject>>(Subjects.Values
            .Where(s => subjectIds is null || subjectIds.Contains(s.Id))
            .ToDictionary(s => s.Id));

    public void AddSubject(IndicatorSubject subject) => Subjects.Add(subject.Id, subject);

    public List<IndicatorMessage> Of(IndicatorMessageKind kind) => Messages.Where(m => m.Kind == kind).ToList();

    /// <summary>Retirement paths queued, in order.</summary>
    public List<string> Retirements => Of(IndicatorMessageKind.Retire).Select(m => m.Payload).ToList();
}

/// <summary>Data of the subjects as the loader would read them; records the change sets it was asked for.</summary>
internal sealed class FakeIndicatorDataSource : IIndicatorDataSource
{
    public List<(IndicatorChangeSet Changes, DateOnly Today)> Loads { get; } = [];

    public List<SubjectFacts> Subjects { get; } = [];

    public List<RetiredSubject> Retired { get; } = [];

    public Task<IndicatorDataSet> LoadAsync(IndicatorChangeSet changes, DateOnly today, CancellationToken cancellationToken)
    {
        Loads.Add((changes, today));
        return Task.FromResult(new IndicatorDataSet(Subjects.ToList(), Retired.ToList()));
    }
}

/// <summary>Simulated indicator API of the Shell: answers from a queue (default: 200) and records every call.</summary>
internal sealed class FakeIndicatorClient : IIndicatorClient
{
    public bool IsConfigured { get; set; } = true;

    public Queue<IndicatorSendResult> Answers { get; } = new();

    public List<(string Operation, string Body)> Calls { get; } = [];

    public Task<IndicatorSendResult> RegisterAsync(string definitionsJson, CancellationToken cancellationToken) => Answer("register", definitionsJson);

    public Task<IndicatorSendResult> PublishAsync(string valuesJson, CancellationToken cancellationToken) => Answer("publish", valuesJson);

    public Task<IndicatorSendResult> RetireAsync(string path, CancellationToken cancellationToken) => Answer("retire", path);

    private Task<IndicatorSendResult> Answer(string operation, string body)
    {
        Calls.Add((operation, body));
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : new IndicatorSendResult(IndicatorSendOutcome.Sent));
    }
}

internal static class Indicators
{
    /// <summary>2026-11-06 18:00 UTC = 12:00 in Mexico City.</summary>
    public static readonly DateOnly Today = new(2026, 11, 6);

    public static readonly IndicatorPublishingSettings Module = new("/brocal/", "brocal.access");

    public static SubjectFacts Facts(Guid? id = null, string type = BrocalIndicators.Well, bool active = true, IReadOnlyList<DocumentCell>? documents = null,
        Dictionary<DateOnly, int>? tickets = null, Dictionary<DateOnly, int>? incidents = null, int openOrders = 0, int unsigned = 0, int openIncidents = 0,
        Domain.Hse.IncidentSeverity? worst = null) =>
        new(type, id ?? Guid.NewGuid(), active, openOrders, unsigned, tickets ?? [], incidents ?? [], openIncidents, worst, 0, 0, documents ?? []);

    public static (IndicatorPublisher Publisher, InMemoryIndicatorOutbox Outbox, FakeIndicatorDataSource Data, FakeIndicatorClient Client, TestClock Clock) Publisher(
        IndicatorPublishingSettings? settings = null)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 11, 6, 18, 0, 0, TimeSpan.Zero));
        var outbox = new InMemoryIndicatorOutbox();
        var data = new FakeIndicatorDataSource();
        var client = new FakeIndicatorClient();
        var publisher = new IndicatorPublisher(outbox, data, client, settings ?? Module, clock, NullLogger<IndicatorPublisher>.Instance);
        return (publisher, outbox, data, client, clock);
    }
}
