using System.Text.Json;
using System.Text.RegularExpressions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Indicators;
using ChronoLith.Brocal.Domain.Hse;

namespace ChronoLith.Brocal.Application.UnitTests;

/// <summary>
/// The bodies Brocal sends to the indicator API of the Shell follow <c>ChronoLith/docs/indicators.md</c>: the
/// definitions (<c>PUT /api/indicators/sources/brocal</c>) and the values (<c>PUT /api/indicators/values</c>).
/// </summary>
public sealed class IndicatorPayloadTests
{
    private static readonly Regex KeyFormat = new("^[a-z][a-z0-9-]*(\\.[a-z0-9-]+)*$");
    private static readonly string[] Types = ["number", "percent", "date", "text"];
    private static readonly string[] Aggregations = ["sum", "avg", "min", "max", "count", "none"];
    private static readonly string[] Directions = ["higherIsBetter", "lowerIsBetter", "neutral"];

    [Fact]
    public void Definitions_follow_the_contract_with_translations_in_en_and_pt_BR()
    {
        using var json = JsonDocument.Parse(BrocalIndicators.DefinitionsJson(Indicators.Module));
        var root = json.RootElement;

        Assert.Equal("Brocal", root.GetProperty("name").GetString());
        Assert.Equal("Brocal", root.GetProperty("translations").GetProperty("en").GetProperty("name").GetString());
        Assert.Equal("Brocal", root.GetProperty("translations").GetProperty("pt-BR").GetProperty("name").GetString());

        var indicators = root.GetProperty("indicators").EnumerateArray().ToList();
        Assert.Equal(11, indicators.Count);
        Assert.InRange(indicators.Count, 1, 100);
        Assert.Equal(indicators.Count, indicators.Select(i => i.GetProperty("key").GetString()).Distinct().Count());

        foreach (var indicator in indicators)
        {
            var key = indicator.GetProperty("key").GetString()!;
            Assert.Matches(KeyFormat, key);
            Assert.InRange(key.Length, 1, 60);
            Assert.InRange(indicator.GetProperty("name").GetString()!.Length, 1, 120);
            Assert.InRange(indicator.GetProperty("description").GetString()!.Length, 1, 500);
            Assert.Contains(indicator.GetProperty("type").GetString(), Types);
            Assert.Contains(indicator.GetProperty("aggregation").GetString(), Aggregations);
            Assert.Contains(indicator.GetProperty("accumulation").GetString(), new[] { "snapshot", "daily" });
            Assert.Contains(indicator.GetProperty("direction").GetString(), Directions);
            Assert.InRange(indicator.GetProperty("decimals").GetInt32(), 0, 6);
            Assert.Equal(["well", "facility"], indicator.GetProperty("subjectTypes").EnumerateArray().Select(s => s.GetString()));
            Assert.Equal("brocal.access", indicator.GetProperty("permissionCode").GetString());

            // Counts and percentages: no physical magnitude, so no quantity (nothing to convert from SI).
            Assert.False(indicator.TryGetProperty("quantity", out _));

            foreach (var language in new[] { "en", "pt-BR" })
            {
                var translation = indicator.GetProperty("translations").GetProperty(language);
                Assert.InRange(translation.GetProperty("name").GetString()!.Length, 1, 120);
                Assert.InRange(translation.GetProperty("description").GetString()!.Length, 1, 500);
            }

            // Dates and texts are counted, never added up.
            if (indicator.GetProperty("type").GetString() is "date" or "text")
            {
                Assert.Contains(indicator.GetProperty("aggregation").GetString(), new[] { "count", "none" });
            }
        }

        var compliance = indicators.Single(i => i.GetProperty("key").GetString() == BrocalIndicators.DocumentsCompliance);
        Assert.Equal("percent", compliance.GetProperty("type").GetString());
        Assert.Equal("avg", compliance.GetProperty("aggregation").GetString());
        var tickets = indicators.Single(i => i.GetProperty("key").GetString() == BrocalIndicators.TicketsServices);
        Assert.Equal("daily", tickets.GetProperty("accumulation").GetString());
        var expired = indicators.Single(i => i.GetProperty("key").GetString() == BrocalIndicators.DocumentsExpired);
        Assert.Equal("Documentos vencidos", expired.GetProperty("name").GetString());
        Assert.Equal("sum", expired.GetProperty("aggregation").GetString());
        Assert.Equal("snapshot", expired.GetProperty("accumulation").GetString());
    }

    [Fact]
    public void Standalone_definitions_carry_no_permission_of_the_module()
    {
        using var json = JsonDocument.Parse(BrocalIndicators.DefinitionsJson(new IndicatorPublishingSettings("https://brocal.example.mx/", null)));

        Assert.All(json.RootElement.GetProperty("indicators").EnumerateArray(), i => Assert.False(i.TryGetProperty("permissionCode", out _)));
    }

    [Fact]
    public void Values_carry_type_period_link_and_observed_at_in_utc_without_planned()
    {
        var subject = Guid.NewGuid();
        var facts = Indicators.Facts(subject, documents: [new DocumentCell("U-01", "Póliza RC", true, Indicators.Today.AddDays(10), 30, Indicators.Today)],
            tickets: new() { [Indicators.Today] = 3 }, openOrders: 2);
        var observed = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(-6));

        var values = IndicatorCalculator.Calculate(facts, Indicators.Today, [], observed, Indicators.Module);
        using var json = JsonDocument.Parse(BrocalIndicators.ValuesJson(values));

        Assert.Equal("brocal", json.RootElement.GetProperty("source").GetString());
        var items = json.RootElement.GetProperty("values").EnumerateArray().ToList();
        Assert.Equal(values.Count, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal("well", item.GetProperty("subjectType").GetString());
            Assert.Equal(subject, item.GetProperty("subjectId").GetGuid());
            Assert.Equal("2026-11-06", item.GetProperty("period").GetString());
            Assert.Equal("2026-11-06T18:00:00Z", item.GetProperty("observedAt").GetString());
            Assert.False(item.TryGetProperty("planned", out _));
            Assert.StartsWith("/brocal/", item.GetProperty("link").GetString());
        });

        var services = items.Single(i => i.GetProperty("indicator").GetString() == BrocalIndicators.TicketsServices);
        Assert.Equal(3, services.GetProperty("value").GetDecimal());
        Assert.Equal("/brocal/boletas", services.GetProperty("link").GetString());

        var next = items.Single(i => i.GetProperty("indicator").GetString() == BrocalIndicators.DocumentsNextExpiry);
        Assert.Equal("2026-11-16", next.GetProperty("date").GetString());
        Assert.False(next.TryGetProperty("value", out _));
        Assert.Equal("Póliza RC · U-01", next.GetProperty("note").GetString());

        var compliance = items.Single(i => i.GetProperty("indicator").GetString() == BrocalIndicators.DocumentsCompliance);
        Assert.Equal(100m, compliance.GetProperty("value").GetDecimal());
    }

    [Fact]
    public void Document_counts_follow_the_traffic_light_of_brocal()
    {
        var today = Indicators.Today;
        DocumentCell[] cells =
        [
            new("U-01", "Tarjeta de circulación", true, today.AddDays(100), 30, today),
            new("E-01", "DC-3 Trabajo en alturas", true, null, 30, today),
            new("E-01", "Examen médico", true, today.AddDays(10), 30, today),
            new("E-02", "H2S", true, today.AddDays(-1), 30, today),
            new("E-02", "Examen médico", false, null, 30, today)
        ];

        var values = IndicatorCalculator.Calculate(Indicators.Facts(documents: cells), today, [], DateTimeOffset.UtcNow, Indicators.Module)
            .ToDictionary(v => v.Indicator);

        Assert.Equal(2, values[BrocalIndicators.DocumentsValid].Value);
        Assert.Equal(1, values[BrocalIndicators.DocumentsExpiring].Value);
        Assert.Equal("warning", values[BrocalIndicators.DocumentsExpiring].Severity);
        Assert.Equal(1, values[BrocalIndicators.DocumentsExpired].Value);
        Assert.Equal("error", values[BrocalIndicators.DocumentsExpired].Severity);
        Assert.Equal(1, values[BrocalIndicators.DocumentsMissing].Value);
        Assert.Equal(60.0m, values[BrocalIndicators.DocumentsCompliance].Value);
        Assert.Equal("error", values[BrocalIndicators.DocumentsCompliance].Severity);
        Assert.Equal(today.AddDays(10), values[BrocalIndicators.DocumentsNextExpiry].Date);
        Assert.Equal("warning", values[BrocalIndicators.DocumentsNextExpiry].Severity);
        Assert.All(values.Values, v => Assert.Null(v.Planned));
    }

    [Fact]
    public void Without_people_or_units_deployed_compliance_is_full_without_severity_there_is_no_next_expiry_and_counts_are_zero()
    {
        var values = IndicatorCalculator.Calculate(Indicators.Facts(), Indicators.Today, [], DateTimeOffset.UtcNow, Indicators.Module).ToDictionary(v => v.Indicator);

        // Always published: the last percentage (and its severity) must not stay current once the orders close.
        var compliance = values[BrocalIndicators.DocumentsCompliance];
        Assert.Equal(100m, compliance.Value);
        Assert.Null(compliance.Severity);
        Assert.Equal(IndicatorCalculator.NothingDeployedNote, compliance.Note);
        Assert.Equal(Indicators.Today, compliance.Period);

        // A date cannot be null: the publisher retires next-expiry instead.
        Assert.DoesNotContain(BrocalIndicators.DocumentsNextExpiry, values.Keys);
        Assert.Equal(0, values[BrocalIndicators.DocumentsExpired].Value);
        Assert.Null(values[BrocalIndicators.DocumentsExpired].Severity);
        Assert.Equal(0, values[BrocalIndicators.TicketsServices].Value);
        Assert.Equal(0, values[BrocalIndicators.HseOpenIncidents].Value);
    }

    [Fact]
    public void Only_expired_or_missing_documents_give_no_next_expiry_and_compliance_in_error()
    {
        var today = Indicators.Today;
        DocumentCell[] cells = [new("E-01", "H2S", true, today.AddDays(-5), 30, today), new("E-01", "Examen médico", false, null, 30, today)];

        var values = IndicatorCalculator.Calculate(Indicators.Facts(documents: cells), today, [], DateTimeOffset.UtcNow, Indicators.Module).ToDictionary(v => v.Indicator);

        Assert.DoesNotContain(BrocalIndicators.DocumentsNextExpiry, values.Keys);
        Assert.Equal(0m, values[BrocalIndicators.DocumentsCompliance].Value);
        Assert.Equal("error", values[BrocalIndicators.DocumentsCompliance].Severity);
    }

    [Fact]
    public void A_publication_splits_into_one_body_per_subject_keeping_the_order_of_its_values()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var values = IndicatorCalculator.Calculate(Indicators.Facts(first), Indicators.Today, [], DateTimeOffset.UtcNow, Indicators.Module)
            .Concat(IndicatorCalculator.Calculate(Indicators.Facts(second, BrocalIndicators.Facility), Indicators.Today, [], DateTimeOffset.UtcNow, Indicators.Module))
            .ToList();

        var parts = BrocalIndicators.SplitBySubject(BrocalIndicators.ValuesJson(values));

        Assert.Equal(2, parts.Count);
        Assert.Equal(values.Count, parts.Sum(p => p.Items));
        using var body = JsonDocument.Parse(parts[1].Json);
        Assert.Equal("brocal", body.RootElement.GetProperty("source").GetString());
        var items = body.RootElement.GetProperty("values").EnumerateArray().ToList();
        Assert.Equal(parts[1].Items, items.Count);
        Assert.All(items, i => Assert.Equal(second, i.GetProperty("subjectId").GetGuid()));
        Assert.Equal(values.Where(v => v.SubjectId == second).Select(v => v.Indicator), items.Select(i => i.GetProperty("indicator").GetString()));
        Assert.Empty(BrocalIndicators.SplitBySubject("no es json"));
    }

    [Fact]
    public void Daily_indicators_are_published_for_the_changed_days_and_today_never_for_the_future()
    {
        var today = Indicators.Today;
        var facts = Indicators.Facts(tickets: new() { [today.AddDays(-3)] = 2, [today] = 1, [today.AddDays(2)] = 4 }, incidents: new() { [today.AddDays(-3)] = 1 });

        var values = IndicatorCalculator.Calculate(facts, today, [today.AddDays(-3), today.AddDays(2)], DateTimeOffset.UtcNow, Indicators.Module);

        var services = values.Where(v => v.Indicator == BrocalIndicators.TicketsServices).ToDictionary(v => v.Period, v => v.Value);
        Assert.Equal([today.AddDays(-3), today], services.Keys.Order());
        Assert.Equal(2, services[today.AddDays(-3)]);
        Assert.Equal(1, services[today]);
        var incidents = values.Where(v => v.Indicator == BrocalIndicators.HseIncidents).ToDictionary(v => v.Period, v => v.Value);
        Assert.Equal(1, incidents[today.AddDays(-3)]);
        Assert.Equal(0, incidents[today]);

        // Snapshot indicators are the state of today only.
        Assert.All(values.Where(v => v.Indicator is not (BrocalIndicators.TicketsServices or BrocalIndicators.HseIncidents)), v => Assert.Equal(today, v.Period));
    }

    [Fact]
    public void Open_incidents_are_an_error_when_one_is_high_or_critical()
    {
        var warning = IndicatorCalculator.Calculate(Indicators.Facts(openIncidents: 2, worst: IncidentSeverity.Medium), Indicators.Today, [], DateTimeOffset.UtcNow, Indicators.Module)
            .Single(v => v.Indicator == BrocalIndicators.HseOpenIncidents);
        var error = IndicatorCalculator.Calculate(Indicators.Facts(openIncidents: 1, worst: IncidentSeverity.Critical), Indicators.Today, [], DateTimeOffset.UtcNow, Indicators.Module)
            .Single(v => v.Indicator == BrocalIndicators.HseOpenIncidents);

        Assert.Equal(2, warning.Value);
        Assert.Equal("warning", warning.Severity);
        Assert.Equal("error", error.Severity);
    }

    [Fact]
    public void Standalone_links_are_absolute_https_urls_and_absent_without_a_public_url()
    {
        var withUrl = IndicatorCalculator.Calculate(Indicators.Facts(), Indicators.Today, [], DateTimeOffset.UtcNow, new IndicatorPublishingSettings("https://brocal.example.mx/", null));
        var withoutUrl = IndicatorCalculator.Calculate(Indicators.Facts(), Indicators.Today, [], DateTimeOffset.UtcNow, new IndicatorPublishingSettings(null, null));

        Assert.Equal("https://brocal.example.mx/ordenes", withUrl.Single(v => v.Indicator == BrocalIndicators.WorkOrdersOpen).Link);
        Assert.All(withoutUrl, v => Assert.Null(v.Link));
    }

    [Fact]
    public void Retirement_paths_name_source_key_type_and_subject()
    {
        var id = Guid.NewGuid();

        Assert.Equal($"api/indicators/values/brocal/documents.expired/facility/{id}", BrocalIndicators.RetirePath(BrocalIndicators.DocumentsExpired, "facility", id));
    }

    [Fact]
    public void Change_sets_survive_the_outbox_and_merge()
    {
        var site = Guid.NewGuid();
        var subject = Guid.NewGuid();
        var first = new IndicatorChangeSet();
        first.AddSite(site);
        first.AddDay(Indicators.Today);
        first.AddSubject(subject, null);
        var second = new IndicatorChangeSet();
        second.AddSubject(subject, "well");

        var restored = IndicatorChangeSet.FromJson(first.ToJson());
        restored.Merge(second);

        Assert.False(restored.All);
        Assert.Contains(site, restored.Sites);
        Assert.Contains(Indicators.Today, restored.Days);
        Assert.Equal("well", restored.Subjects[subject]);
        Assert.True(IndicatorChangeSet.FromJson("no es json").All);
    }
}
