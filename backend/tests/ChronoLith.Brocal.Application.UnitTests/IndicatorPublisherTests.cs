using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Indicators;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Application.UnitTests;

/// <summary>
/// The outbox of the indicators against a simulated Shell: markers become publications of up to 500 values and
/// retirements, deliveries keep their order, a 400 is discarded without retrying and the daily job republishes
/// every active subject.
/// </summary>
public sealed class IndicatorPublisherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Markers_are_merged_and_turned_into_batches_of_up_to_500_values()
    {
        var (publisher, outbox, data, _, clock) = Indicators.Publisher();
        var site = Guid.NewGuid();
        var marker = new IndicatorChangeSet();
        marker.AddSite(site);
        marker.AddDay(Indicators.Today.AddDays(-1));
        outbox.Add(IndicatorMessage.Recalculate(marker.ToJson(), clock.GetUtcNow().AddMinutes(-2)));
        outbox.Add(IndicatorMessage.Recalculate(new IndicatorChangeSet { All = true }.ToJson(), clock.GetUtcNow().AddMinutes(-1)));
        for (var i = 0; i < 50; i++)
        {
            data.Subjects.Add(Indicators.Facts());
        }

        var queued = await publisher.PrepareAsync(Ct);

        var load = Assert.Single(data.Loads);
        Assert.True(load.Changes.All);
        Assert.Contains(site, load.Changes.Sites);
        Assert.Contains(Indicators.Today.AddDays(-1), load.Changes.Days);
        Assert.Equal(Indicators.Today, load.Today);

        var publications = outbox.Of(IndicatorMessageKind.Publish);
        Assert.Equal(queued, publications.Sum(p => p.Items));
        Assert.All(publications, p => Assert.InRange(p.Items, 1, IndicatorPublisher.BatchSize));
        Assert.True(queued > IndicatorPublisher.BatchSize, "los valores de 50 pozos no caben en una sola solicitud");
        Assert.Equal((queued + IndicatorPublisher.BatchSize - 1) / IndicatorPublisher.BatchSize, publications.Count);

        // No subject is split between two requests (a 400 is narrowed down subject by subject).
        var subjectsPerRequest = new List<HashSet<Guid>>();
        foreach (var publication in publications)
        {
            using var body = JsonDocument.Parse(publication.Payload);
            Assert.Equal(publication.Items, body.RootElement.GetProperty("values").GetArrayLength());
            subjectsPerRequest.Add(body.RootElement.GetProperty("values").EnumerateArray().Select(v => v.GetProperty("subjectId").GetGuid()).ToHashSet());
        }

        Assert.Empty(subjectsPerRequest[0].Intersect(subjectsPerRequest[1]));

        // Yesterday travels in every batch: retried until delivered (no later republication repeats a past day).
        Assert.All(publications, p => Assert.True(p.KeepRetrying));

        Assert.All(outbox.Of(IndicatorMessageKind.Recalculate), m => Assert.Equal(IndicatorMessageStatus.Done, m.Status));
        Assert.Equal(1, outbox.Saves);
    }

    [Fact]
    public async Task A_subject_no_site_links_to_anymore_is_retired_indicator_by_indicator()
    {
        var (publisher, outbox, data, _, clock) = Indicators.Publisher();
        var subject = Guid.NewGuid();
        var marker = new IndicatorChangeSet();
        marker.AddSubject(subject, "well");
        outbox.Add(IndicatorMessage.Recalculate(marker.ToJson(), clock.GetUtcNow()));
        data.Retired.Add(new RetiredSubject("well", subject));

        await publisher.PrepareAsync(Ct);

        var retirements = outbox.Of(IndicatorMessageKind.Retire);
        Assert.Equal(BrocalIndicators.Keys.Count, retirements.Count);
        Assert.Contains(retirements, r => r.Payload == $"api/indicators/values/brocal/documents.expired/well/{subject}");
        Assert.Empty(outbox.Of(IndicatorMessageKind.Publish));
    }

    [Fact]
    public async Task Deliveries_go_in_order_a_400_is_discarded_and_a_404_on_a_retirement_is_done()
    {
        var (publisher, outbox, _, client, clock) = Indicators.Publisher();
        var now = clock.GetUtcNow();
        var first = IndicatorMessage.Publish("""{"source":"brocal","values":[1]}""", 1, now.AddSeconds(-3));
        var rejected = IndicatorMessage.Publish("""{"source":"brocal","values":[2]}""", 1, now.AddSeconds(-2));
        var retirement = IndicatorMessage.Retire("api/indicators/values/brocal/tickets.unsigned/well/x", now.AddSeconds(-1));
        outbox.Add(retirement);
        outbox.Add(first);
        outbox.Add(rejected);
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Sent));
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Rejected, "400: el sujeto no existe"));
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.NotFound, "404"));

        var summary = await publisher.DeliverAsync(Ct);

        Assert.Equal(["publish", "publish", "retire"], client.Calls.Select(c => c.Operation));
        Assert.Equal(first.Payload, client.Calls[0].Body);
        Assert.Equal(IndicatorMessageStatus.Done, first.Status);
        Assert.Equal(IndicatorMessageStatus.Rejected, rejected.Status);
        Assert.Contains("el sujeto no existe", rejected.LastError);
        Assert.Equal(IndicatorMessageStatus.Done, retirement.Status);
        Assert.Equal(new IndicatorDeliverySummary(2, 1, 0, 0), summary);

        // The rejected request is never sent again.
        await publisher.DeliverAsync(Ct);
        Assert.Equal(3, client.Calls.Count);
    }

    [Fact]
    public async Task When_the_shell_does_not_answer_the_round_stops_and_retries_later_in_the_same_order()
    {
        var (publisher, outbox, _, client, clock) = Indicators.Publisher();
        var now = clock.GetUtcNow();
        var first = IndicatorMessage.Publish("""{"values":[1]}""", 1, now.AddSeconds(-2));
        var second = IndicatorMessage.Retire("api/indicators/values/brocal/work-orders.open/well/y", now.AddSeconds(-1));
        outbox.Add(first);
        outbox.Add(second);
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Unavailable, "503"));

        var failed = await publisher.DeliverAsync(Ct);

        Assert.Single(client.Calls);
        Assert.Equal(IndicatorMessageStatus.Pending, first.Status);
        Assert.Equal(1, first.Attempts);
        Assert.Equal(now.AddMinutes(1), first.NextAttemptAt);
        Assert.Equal(IndicatorMessageStatus.Pending, second.Status);
        Assert.Equal(1, failed.Failed);

        // Before the back-off nothing is sent (the later retirement does not overtake the publication).
        await publisher.DeliverAsync(Ct);
        Assert.Single(client.Calls);

        clock.Now = now.AddMinutes(2);
        await publisher.DeliverAsync(Ct);
        Assert.Equal(["publish", "publish", "retire"], client.Calls.Select(c => c.Operation));
        Assert.Equal(IndicatorMessageStatus.Done, first.Status);
        Assert.Equal(IndicatorMessageStatus.Done, second.Status);
    }

    [Fact]
    public async Task After_the_last_attempt_the_message_fails_and_the_next_ones_go_on()
    {
        var (publisher, outbox, _, client, clock) = Indicators.Publisher();
        var message = IndicatorMessage.Publish("{}", 1, clock.GetUtcNow());
        outbox.Add(message);

        for (var attempt = 1; attempt < IndicatorMessage.MaxAttempts; attempt++)
        {
            client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Unavailable, "503"));
            await publisher.DeliverAsync(Ct);
            clock.Now = message.NextAttemptAt;
        }

        var next = IndicatorMessage.Publish("""{"values":[2]}""", 1, clock.GetUtcNow());
        outbox.Add(next);
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Unavailable, "503"));
        await publisher.DeliverAsync(Ct);

        Assert.Equal(IndicatorMessageStatus.Failed, message.Status);
        Assert.Equal(IndicatorMessage.MaxAttempts, message.Attempts);
        Assert.Equal(IndicatorMessageStatus.Done, next.Status);
    }

    [Fact]
    public async Task Nothing_is_sent_when_brocal_is_not_connected_to_a_shell()
    {
        var (publisher, outbox, _, client, clock) = Indicators.Publisher();
        client.IsConfigured = false;
        var message = IndicatorMessage.Publish("{}", 1, clock.GetUtcNow());
        outbox.Add(message);

        Assert.False(await publisher.RegisterAsync(Ct));
        await publisher.DeliverAsync(Ct);

        Assert.Empty(client.Calls);
        Assert.Equal(IndicatorMessageStatus.Pending, message.Status);
    }

    [Fact]
    public async Task The_daily_job_republishes_every_active_subject_for_the_business_day()
    {
        var (publisher, outbox, data, client, _) = Indicators.Publisher();
        var subject = Guid.NewGuid();
        data.Subjects.Add(Indicators.Facts(subject, BrocalIndicators.Facility, openOrders: 1));

        await publisher.EnqueueDailyAsync(Ct);
        var marker = Assert.Single(outbox.Of(IndicatorMessageKind.Recalculate));
        var changes = IndicatorChangeSet.FromJson(marker.Payload);
        Assert.True(changes.All);
        Assert.Equal([Indicators.Today], changes.Days);

        await publisher.PrepareAsync(Ct);
        await publisher.DeliverAsync(Ct);

        var call = Assert.Single(client.Calls, c => c.Operation == "publish");
        using var body = JsonDocument.Parse(call.Body);
        var values = body.RootElement.GetProperty("values").EnumerateArray().ToList();
        Assert.All(values, v =>
        {
            Assert.Equal("facility", v.GetProperty("subjectType").GetString());
            Assert.Equal(subject, v.GetProperty("subjectId").GetGuid());
            Assert.Equal("2026-11-06", v.GetProperty("period").GetString());
        });
        Assert.Equal(1, values.Single(v => v.GetProperty("indicator").GetString() == BrocalIndicators.WorkOrdersOpen).GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task The_definitions_are_registered_with_the_source_payload()
    {
        var (publisher, _, _, client, _) = Indicators.Publisher();
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Sent));
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Unavailable, "503"));

        Assert.True(await publisher.RegisterAsync(Ct));
        Assert.False(await publisher.RegisterAsync(Ct));

        Assert.Equal("register", client.Calls[0].Operation);
        using var body = JsonDocument.Parse(client.Calls[0].Body);
        Assert.Equal(BrocalIndicators.All.Count, body.RootElement.GetProperty("indicators").GetArrayLength());
    }

    [Fact]
    public async Task Housekeeping_drops_finished_messages_older_than_a_week_but_never_pending_ones()
    {
        var (publisher, outbox, _, _, clock) = Indicators.Publisher();
        var now = clock.GetUtcNow();
        var oldDone = IndicatorMessage.Publish("{}", 1, now.AddDays(-8));
        oldDone.MarkDone(now.AddDays(-8));
        var oldRejected = IndicatorMessage.Publish("{}", 1, now.AddDays(-9));
        oldRejected.MarkRejected(now.AddDays(-9), "400");
        var oldPendingRetirement = IndicatorMessage.Retire("api/indicators/values/brocal/work-orders.open/well/z", now.AddDays(-10));
        var recentDone = IndicatorMessage.Publish("{}", 1, now.AddDays(-1));
        recentDone.MarkDone(now.AddDays(-1));
        outbox.Add(oldDone);
        outbox.Add(oldRejected);
        outbox.Add(oldPendingRetirement);
        outbox.Add(recentDone);

        Assert.Equal(2, await publisher.PurgeAsync(Ct));
        Assert.Equal([oldPendingRetirement, recentDone], outbox.Messages);
    }

    [Fact]
    public async Task Retirements_and_past_days_are_retried_until_delivered_while_todays_values_give_up()
    {
        var (publisher, outbox, _, client, clock) = Indicators.Publisher();
        var retirement = IndicatorMessage.Retire("api/indicators/values/brocal/work-orders.open/well/y", clock.GetUtcNow());
        outbox.Add(retirement);

        var wait = TimeSpan.Zero;
        for (var attempt = 1; attempt <= IndicatorMessage.MaxAttempts * 2; attempt++)
        {
            client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Unavailable, "503"));
            await publisher.DeliverAsync(Ct);
            wait = retirement.NextAttemptAt - clock.Now;
            clock.Now = retirement.NextAttemptAt;
        }

        Assert.Equal(IndicatorMessageStatus.Pending, retirement.Status);
        Assert.Equal(IndicatorMessage.MaxAttempts * 2, retirement.Attempts);
        Assert.Equal(TimeSpan.FromMinutes(30), wait);

        // Once the Shell answers, it goes (and nothing overtook it in the meantime).
        await publisher.DeliverAsync(Ct);
        Assert.Equal(IndicatorMessageStatus.Done, retirement.Status);

        Assert.False(IndicatorMessage.Publish("{}", 1, clock.GetUtcNow()).KeepRetrying);
        Assert.True(IndicatorMessage.Publish("{}", 1, clock.GetUtcNow(), keepRetrying: true).KeepRetrying);
    }

    [Fact]
    public async Task A_rejected_batch_of_several_subjects_is_resent_subject_by_subject_and_only_the_culprit_is_discarded()
    {
        var (publisher, outbox, data, client, clock) = Indicators.Publisher();
        var good = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        DocumentCell[] valid = [new("U-01", "Póliza RC", true, Indicators.Today.AddDays(90), 30, Indicators.Today)];
        data.Subjects.Add(Indicators.Facts(good, documents: valid));
        data.Subjects.Add(Indicators.Facts(deleted, documents: valid));
        outbox.Add(IndicatorMessage.Recalculate(new IndicatorChangeSet { All = true }.ToJson(), clock.GetUtcNow()));
        await publisher.PrepareAsync(Ct);
        var batch = Assert.Single(outbox.Of(IndicatorMessageKind.Publish));
        clock.Now = clock.GetUtcNow().AddSeconds(5);
        var later = IndicatorMessage.Retire("api/indicators/values/brocal/work-orders.open/well/later", clock.GetUtcNow());
        outbox.Add(later);

        // The batch of both subjects is rejected: split, and the round ends so the parts keep their place.
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Rejected, "400: el sujeto no existe"));
        var first = await publisher.DeliverAsync(Ct);
        Assert.Equal(1, first.Rejected);
        Assert.Equal(IndicatorMessageStatus.Rejected, batch.Status);
        Assert.StartsWith("Dividido por sujeto", batch.LastError);
        var parts = outbox.Of(IndicatorMessageKind.Publish).Where(m => m != batch).ToList();
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Equal(batch.CreatedAt, p.CreatedAt));
        Assert.Equal(batch.Items, parts.Sum(p => p.Items));
        Assert.Equal(IndicatorMessageStatus.Pending, later.Status);

        // Next round: the parts go before the later message; only the one of the deleted subject is rejected.
        var calls = client.Calls.Count;
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Sent));
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.Rejected, "400: el sujeto no existe"));
        await publisher.DeliverAsync(Ct);

        var sent = client.Calls.Skip(calls).ToList();
        Assert.Equal(["publish", "publish", "retire"], sent.Select(c => c.Operation));
        Assert.Equal(later.Payload, sent[2].Body);
        Assert.Equal(1, parts.Count(p => p.Status == IndicatorMessageStatus.Done));
        Assert.Equal(1, parts.Count(p => p.Status == IndicatorMessageStatus.Rejected));
        Assert.Equal(IndicatorMessageStatus.Done, later.Status);
    }

    [Fact]
    public async Task A_404_to_a_publication_asks_to_register_again_without_spending_an_attempt()
    {
        var (publisher, outbox, _, client, clock) = Indicators.Publisher();
        var message = IndicatorMessage.Publish("""{"source":"brocal","values":[]}""", 1, clock.GetUtcNow().AddSeconds(-1));
        var next = IndicatorMessage.Publish("""{"source":"brocal","values":[]}""", 1, clock.GetUtcNow());
        outbox.Add(message);
        outbox.Add(next);
        client.Answers.Enqueue(new IndicatorSendResult(IndicatorSendOutcome.NotFound, "404 La fuente no ha declarado sus indicadores"));

        var summary = await publisher.DeliverAsync(Ct);

        Assert.True(summary.NeedsRegistration);
        Assert.Single(client.Calls);
        Assert.Equal(IndicatorMessageStatus.Pending, message.Status);
        Assert.Equal(0, message.Attempts);
        Assert.Equal(IndicatorMessageStatus.Pending, next.Status);
        Assert.Equal(2, summary.Pending);

        // Registered again, the same message goes first.
        Assert.True(await publisher.RegisterAsync(Ct));
        var after = await publisher.DeliverAsync(Ct);
        Assert.False(after.NeedsRegistration);
        Assert.Equal(IndicatorMessageStatus.Done, message.Status);
        Assert.Equal(IndicatorMessageStatus.Done, next.Status);
    }

    [Fact]
    public async Task Next_expiry_is_retired_when_there_is_none_once_and_not_on_every_recalculation()
    {
        var (publisher, outbox, data, _, clock) = Indicators.Publisher();
        var subject = Guid.NewGuid();
        var today = Indicators.Today;
        var path = BrocalIndicators.RetirePath(BrocalIndicators.DocumentsNextExpiry, BrocalIndicators.Well, subject);

        // With a valid document: next-expiry published and remembered.
        data.Subjects.Add(Indicators.Facts(subject, documents: [new DocumentCell("U-01", "Póliza RC", true, today.AddDays(90), 30, today)]));
        await Recalculate(outbox, clock, publisher);
        Assert.DoesNotContain(path, outbox.Retirements);
        Assert.True(outbox.Subjects[subject].IsPublished);
        Assert.True(outbox.Subjects[subject].HasNextExpiry);

        // The order closes (nothing deployed): next-expiry is retired, compliance goes on at 100 % without severity.
        data.Subjects.Clear();
        data.Subjects.Add(Indicators.Facts(subject));
        var before = outbox.Of(IndicatorMessageKind.Publish).Count;
        await Recalculate(outbox, clock, publisher);
        Assert.Equal([path], outbox.Retirements);
        Assert.False(outbox.Subjects[subject].HasNextExpiry);
        var publication = outbox.Of(IndicatorMessageKind.Publish).Skip(before).Single();
        Assert.Contains(BrocalIndicators.DocumentsCompliance, publication.Payload);
        Assert.DoesNotContain(BrocalIndicators.DocumentsNextExpiry, publication.Payload);

        // Next recalculations do not repeat the retirement.
        await Recalculate(outbox, clock, publisher);
        Assert.Equal([path], outbox.Retirements);
    }

    [Fact]
    public async Task The_daily_run_retires_subjects_published_before_and_not_publishable_anymore_only_once()
    {
        var (publisher, outbox, data, _, clock) = Indicators.Publisher();
        var kept = Guid.NewGuid();
        var lost = Guid.NewGuid();
        var deactivated = Guid.NewGuid();
        data.Subjects.Add(Indicators.Facts(kept));
        data.Subjects.Add(Indicators.Facts(lost, BrocalIndicators.Facility));
        data.Subjects.Add(Indicators.Facts(deactivated));
        await Recalculate(outbox, clock, publisher);
        outbox.Messages.Clear();

        // The site of `lost` was unlinked and its retirement was lost; `deactivated` only has inactive sites left
        // (the loader reports it as retired).
        data.Subjects.RemoveAll(s => s.SubjectId != kept);
        data.Retired.Add(new RetiredSubject(BrocalIndicators.Well, deactivated));
        await Recalculate(outbox, clock, publisher);

        var retirements = outbox.Retirements;
        Assert.Equal(BrocalIndicators.Keys.Count * 2, retirements.Count);
        Assert.Contains(BrocalIndicators.RetirePath(BrocalIndicators.WorkOrdersOpen, BrocalIndicators.Facility, lost), retirements);
        Assert.Contains(BrocalIndicators.RetirePath(BrocalIndicators.WorkOrdersOpen, BrocalIndicators.Well, deactivated), retirements);
        Assert.DoesNotContain(retirements, r => r.EndsWith(kept.ToString(), StringComparison.Ordinal));
        Assert.All(outbox.Of(IndicatorMessageKind.Retire), r => Assert.True(r.KeepRetrying));
        Assert.False(outbox.Subjects[lost].IsPublished);
        Assert.False(outbox.Subjects[deactivated].IsPublished);

        // The next day nothing is retired again.
        outbox.Messages.Clear();
        await Recalculate(outbox, clock, publisher);
        Assert.Empty(outbox.Retirements);

        // Linked again: published again.
        data.Retired.Clear();
        data.Subjects.Add(Indicators.Facts(lost, BrocalIndicators.Facility));
        await Recalculate(outbox, clock, publisher);
        Assert.True(outbox.Subjects[lost].IsPublished);
    }

    private static async Task Recalculate(InMemoryIndicatorOutbox outbox, TestClock clock, IndicatorPublisher publisher)
    {
        clock.Now = clock.GetUtcNow().AddSeconds(1);
        outbox.Add(IndicatorMessage.Recalculate(new IndicatorChangeSet { All = true }.ToJson(), clock.GetUtcNow()));
        await publisher.PrepareAsync(Ct);
    }
}
