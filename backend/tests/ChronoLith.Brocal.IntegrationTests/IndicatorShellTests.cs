using System.Net;
using System.Text;
using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Indicators;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Infrastructure.Authentication;
using ChronoLith.Brocal.Infrastructure.Identity;
using ChronoLith.Brocal.Infrastructure.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.IntegrationTests;

/// <summary>
/// The HTTP side of the indicators against a simulated Shell (no database, no Docker): paths, token of the service
/// account and bodies of <c>ChronoLith/docs/indicators.md</c>, and how the answers map to the outbox outcomes.
/// </summary>
public sealed class IndicatorShellTests
{
    private sealed class FakeShell : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Authorization, string Body)> Requests { get; } = [];

        public Dictionary<string, (HttpStatusCode Status, string Body)> Answers { get; } = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method.Method, path, request.Headers.Authorization?.ToString(), body));

            if (path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{"access_token":"service-token","expires_in":300}""");
            }

            var (status, answer) = Answers.TryGetValue($"{request.Method.Method} {path}", out var scripted) ? scripted : (HttpStatusCode.NotFound, """{"title":"no existe"}""");
            return Json(status, answer);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://shell.test/") };
    }

    private static readonly KeycloakOptions Keycloak = new() { AuthServerUrl = "http://keycloak.test", Realm = "chronolith", ClientId = "chronolith-brocal-api", ClientSecret = "secret" };
    private static readonly ShellOptions Shell = new() { ApiUrl = "http://shell.test" };
    private static readonly TenancyOptions Module = new() { Mode = "ChronoLith" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ShellIndicatorClient Client, FakeShell Shell) Client(IndicatorOptions? indicators = null, TenancyOptions? tenancy = null, ShellOptions? shell = null)
    {
        var fake = new FakeShell();
        var tokens = new ShellServiceAccountTokenProvider(new FakeHttpClientFactory(fake), Options.Create(Keycloak), Options.Create(shell ?? Shell), TimeProvider.System);
        var client = new ShellIndicatorClient(new FakeHttpClientFactory(fake).CreateClient("indicators"), tokens, Options.Create(indicators ?? new IndicatorOptions()),
            Options.Create(shell ?? Shell), Options.Create(Keycloak), Options.Create(tenancy ?? Module), NullLogger<ShellIndicatorClient>.Instance);
        return (client, fake);
    }

    [Fact]
    public async Task Definitions_go_to_the_source_of_brocal_with_the_service_account()
    {
        var (client, shell) = Client();
        shell.Answers["PUT /api/indicators/sources/brocal"] = (HttpStatusCode.OK, """{"value":11}""");
        var body = BrocalIndicators.DefinitionsJson(new IndicatorPublishingSettings("/brocal/", "brocal.access"));

        var result = await client.RegisterAsync(body, Ct);

        Assert.Equal(IndicatorSendOutcome.Sent, result.Outcome);
        Assert.EndsWith("/realms/chronolith/protocol/openid-connect/token", shell.Requests[0].Path);
        var request = shell.Requests[1];
        Assert.Equal("PUT", request.Method);
        Assert.Equal("Bearer service-token", request.Authorization);
        Assert.Equal(body, request.Body);
    }

    [Fact]
    public async Task Values_are_put_and_retirements_deleted_by_source_key_type_and_subject()
    {
        var (client, shell) = Client();
        var subject = Guid.NewGuid();
        var path = BrocalIndicators.RetirePath(BrocalIndicators.DocumentsExpired, "well", subject);
        shell.Answers["PUT /api/indicators/values"] = (HttpStatusCode.OK, """{"value":1}""");
        shell.Answers[$"DELETE /{path}"] = (HttpStatusCode.NoContent, string.Empty);
        var values = BrocalIndicators.ValuesJson([new IndicatorValue(BrocalIndicators.DocumentsExpired, "well", subject, new DateOnly(2026, 11, 6), 2, null, null, null, "error", null, "/brocal/personal/matriz", DateTime.UtcNow)]);

        var published = await client.PublishAsync(values, Ct);
        var retired = await client.RetireAsync(path, Ct);

        Assert.Equal(IndicatorSendOutcome.Sent, published.Outcome);
        Assert.Equal(IndicatorSendOutcome.Sent, retired.Outcome);
        using var body = JsonDocument.Parse(shell.Requests.Single(r => r.Method == "PUT").Body);
        Assert.Equal("brocal", body.RootElement.GetProperty("source").GetString());
        Assert.Equal("documents.expired", body.RootElement.GetProperty("values")[0].GetProperty("indicator").GetString());
        Assert.Equal($"/api/indicators/values/brocal/documents.expired/well/{subject}", shell.Requests.Single(r => r.Method == "DELETE").Path);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, IndicatorSendOutcome.Rejected)]
    [InlineData(HttpStatusCode.UnprocessableEntity, IndicatorSendOutcome.Rejected)]
    [InlineData(HttpStatusCode.NotFound, IndicatorSendOutcome.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, IndicatorSendOutcome.Unavailable)]
    [InlineData(HttpStatusCode.Forbidden, IndicatorSendOutcome.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, IndicatorSendOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, IndicatorSendOutcome.Unavailable)]
    public async Task Answers_of_the_shell_decide_whether_to_discard_or_to_retry(HttpStatusCode status, IndicatorSendOutcome expected)
    {
        var (client, shell) = Client();
        shell.Answers["PUT /api/indicators/values"] = (status, """{"detail":"El sujeto no existe en el catálogo compartido."}""");

        var result = await client.PublishAsync("""{"source":"brocal","values":[]}""", Ct);

        Assert.Equal(expected, result.Outcome);
        Assert.Contains(((int)status).ToString(), result.Detail);
    }

    [Fact]
    public async Task A_403_asks_for_a_new_token_and_names_the_missing_role()
    {
        var (client, shell) = Client();
        shell.Answers["PUT /api/indicators/values"] = (HttpStatusCode.Forbidden, string.Empty);

        var first = await client.PublishAsync("""{"source":"brocal","values":[]}""", Ct);
        await client.PublishAsync("""{"source":"brocal","values":[]}""", Ct);

        Assert.Equal(IndicatorSendOutcome.Unavailable, first.Outcome);
        Assert.Contains("chronolith-module-registrar", first.Detail);
        Assert.Equal(2, shell.Requests.Count(r => r.Path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Without_connection_or_with_indicators_disabled_nothing_is_sent()
    {
        var (standalone, standaloneShell) = Client(tenancy: new TenancyOptions { Mode = "Standalone" });
        var (disabled, disabledShell) = Client(new IndicatorOptions { Enabled = false });

        Assert.False(standalone.IsConfigured);
        Assert.False(disabled.IsConfigured);
        Assert.Equal(IndicatorSendOutcome.Disabled, (await standalone.PublishAsync("{}", Ct)).Outcome);
        Assert.Equal(IndicatorSendOutcome.Disabled, (await disabled.RegisterAsync("{}", Ct)).Outcome);
        Assert.Empty(standaloneShell.Requests);
        Assert.Empty(disabledShell.Requests);
    }

    [Fact]
    public void Links_and_permission_depend_on_the_mode()
    {
        var registration = new ModuleRegistrationOptions { RoutePath = "brocal" };

        var module = IndicatorAvailability.Settings(new IndicatorOptions(), registration, Module);
        var standalone = IndicatorAvailability.Settings(new IndicatorOptions { PublicUrl = "https://brocal.chronolith.com.mx/" }, registration, new TenancyOptions { Mode = "Standalone" });
        var insecure = IndicatorAvailability.Settings(new IndicatorOptions { PublicUrl = "http://localhost:4303" }, registration, new TenancyOptions { Mode = "Standalone" });

        Assert.Equal(new IndicatorPublishingSettings("/brocal/", "brocal.access"), module);
        Assert.Equal(new IndicatorPublishingSettings("https://brocal.chronolith.com.mx/", null), standalone);
        Assert.Equal(new IndicatorPublishingSettings(null, null), insecure);
    }
}
