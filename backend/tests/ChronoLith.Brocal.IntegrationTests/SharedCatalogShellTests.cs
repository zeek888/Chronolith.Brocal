using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Infrastructure.Authentication;
using ChronoLith.Brocal.Infrastructure.Identity;
using ChronoLith.Brocal.Infrastructure.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.IntegrationTests;

/// <summary>
/// The HTTP side of the integration with the shared catalogs against a simulated Shell (no database, no Docker): the
/// writer forwards the right token with <c>X-ChronoLith-Source: brocal</c> and maps the answers, and the feed client
/// parses pages, reports and full loads as the module contract describes them.
/// </summary>
public sealed class SharedCatalogShellTests
{
    private static readonly Guid Created = Guid.NewGuid();

    /// <summary>Simulated Shell: records every request and answers from a script keyed by method and path.</summary>
    private sealed class FakeShell : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Authorization, string? Source, string Body)> Requests { get; } = [];

        public Dictionary<string, (HttpStatusCode Status, string Body)> Answers { get; } = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            request.Headers.TryGetValues("X-ChronoLith-Source", out var source);
            Requests.Add((request.Method.Method, path, request.Headers.Authorization?.ToString(), source?.FirstOrDefault(), body));

            if (path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{"access_token":"service-token","expires_in":300}""");
            }

            var (status, answer) = Answers.TryGetValue($"{request.Method.Method} {path}", out var scripted) ? scripted : (HttpStatusCode.NotFound, """{"title":"no existe"}""");
            return Json(status, answer);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://shell.test/") };
    }

    private static readonly KeycloakOptions Keycloak = new() { AuthServerUrl = "http://keycloak.test", Realm = "chronolith", ClientId = "chronolith-brocal-api", ClientSecret = "secret" };
    private static readonly ShellOptions Shell = new() { ApiUrl = "http://shell.test" };
    private static readonly TenancyOptions Module = new() { Mode = "ChronoLith" };

    private static (ShellMasterCatalogWriter Writer, FakeShell Shell) Writer(string? userToken, TenancyOptions? tenancy = null, ShellOptions? shell = null)
    {
        var fake = new FakeShell();
        var context = new DefaultHttpContext();
        if (userToken is not null)
        {
            context.Request.Headers.Authorization = $"Bearer {userToken}";
        }

        var accessor = new HttpContextAccessor { HttpContext = context };
        var tokens = new ShellServiceAccountTokenProvider(new FakeHttpClientFactory(fake), Options.Create(Keycloak), Options.Create(shell ?? Shell), TimeProvider.System);
        var writer = new ShellMasterCatalogWriter(new FakeHttpClientFactory(fake).CreateClient("shell"), accessor, tokens, Options.Create(shell ?? Shell), Options.Create(Keycloak), Options.Create(tenancy ?? Module), NullLogger<ShellMasterCatalogWriter>.Instance);
        return (writer, fake);
    }

    [Fact]
    public async Task Writes_with_the_user_token_carry_the_source_header_and_return_the_created_id()
    {
        var (writer, shell) = Writer("user-token");
        shell.Answers["POST /api/catalogs/wells"] = (HttpStatusCode.Created, $$$"""{"id":"{{{Created}}}"}""");

        var result = await writer.SendAsync("POST", "api/catalogs/wells", """{"code":"KU-61"}""", asServiceAccount: false, TestContext.Current.CancellationToken);

        Assert.Equal(WriteBackOutcome.Sent, result.Outcome);
        Assert.Equal(Created, result.CreatedId);
        var request = Assert.Single(shell.Requests);
        Assert.Equal("Bearer user-token", request.Authorization);
        Assert.Equal("brocal", request.Source);
        Assert.Equal("""{"code":"KU-61"}""", request.Body);
    }

    [Fact]
    public async Task Writes_with_the_service_account_ask_keycloak_for_the_token_first()
    {
        var (writer, shell) = Writer(null);
        shell.Answers[$"PUT /api/catalogs/facilities/{Created}"] = (HttpStatusCode.NoContent, string.Empty);

        var result = await writer.SendAsync("PUT", $"api/catalogs/facilities/{Created}", "{}", asServiceAccount: true, TestContext.Current.CancellationToken);

        Assert.Equal(WriteBackOutcome.Sent, result.Outcome);
        Assert.Null(result.CreatedId);
        Assert.Equal(2, shell.Requests.Count);
        Assert.EndsWith("/realms/chronolith/protocol/openid-connect/token", shell.Requests[0].Path);
        Assert.Contains("client_id=chronolith-brocal-api", shell.Requests[0].Body);
        Assert.Equal("Bearer service-token", shell.Requests[1].Authorization);
        Assert.Equal("brocal", shell.Requests[1].Source);
    }

    [Fact]
    public async Task Standalone_uses_the_service_account_of_the_chronolith_realm_configured_in_shell_options()
    {
        var standalone = new ShellOptions { ApiUrl = "http://shell.test", AuthServerUrl = "http://chronolith-keycloak.test/auth", Realm = "chronolith", ClientId = "brocal-sync", ClientSecret = "s3cret" };
        var (writer, shell) = Writer(null, new TenancyOptions { Mode = "Standalone" }, standalone);
        shell.Answers[$"PUT /api/catalogs/companies/{Created}"] = (HttpStatusCode.NoContent, string.Empty);

        var result = await writer.SendAsync("PUT", $"api/catalogs/companies/{Created}", "{}", asServiceAccount: true, TestContext.Current.CancellationToken);

        Assert.Equal(WriteBackOutcome.Sent, result.Outcome);
        Assert.Equal("/auth/realms/chronolith/protocol/openid-connect/token", shell.Requests[0].Path);
        Assert.Contains("client_id=brocal-sync", shell.Requests[0].Body);
    }

    [Fact]
    public async Task Standalone_without_a_service_account_is_disabled()
    {
        var (writer, shell) = Writer("user-token", new TenancyOptions { Mode = "Standalone" }, new ShellOptions { ApiUrl = "http://shell.test" });

        var result = await writer.SendAsync("POST", "api/catalogs/wells", "{}", asServiceAccount: false, TestContext.Current.CancellationToken);

        Assert.Equal(WriteBackOutcome.Disabled, result.Outcome);
        Assert.Empty(shell.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, WriteBackOutcome.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized, WriteBackOutcome.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest, WriteBackOutcome.Rejected)]
    [InlineData(HttpStatusCode.Conflict, WriteBackOutcome.Rejected)]
    [InlineData(HttpStatusCode.NotFound, WriteBackOutcome.Rejected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, WriteBackOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, WriteBackOutcome.Unavailable)]
    public async Task Answers_of_the_master_decide_whether_to_queue_or_to_reject(HttpStatusCode status, WriteBackOutcome expected)
    {
        var (writer, shell) = Writer("user-token");
        shell.Answers["POST /api/catalogs/wells"] = (status, """{"detail":"motivo"}""");

        var result = await writer.SendAsync("POST", "api/catalogs/wells", "{}", asServiceAccount: false, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Contains(((int)status).ToString(), result.Detail);
    }

    [Fact]
    public async Task Without_a_user_token_the_write_is_forbidden_so_it_goes_to_the_outbox()
    {
        var (writer, shell) = Writer(null);

        var result = await writer.SendAsync("POST", "api/catalogs/wells", "{}", asServiceAccount: false, TestContext.Current.CancellationToken);

        Assert.Equal(WriteBackOutcome.Forbidden, result.Outcome);
        Assert.Empty(shell.Requests);
    }

    private static (ShellCatalogFeedClient Client, FakeShell Shell) FeedClient()
    {
        var fake = new FakeShell();
        var tokens = new ShellServiceAccountTokenProvider(new FakeHttpClientFactory(fake), Options.Create(Keycloak), Options.Create(Shell), TimeProvider.System);
        var client = new ShellCatalogFeedClient(new FakeHttpClientFactory(fake).CreateClient("feed"), tokens, Options.Create(Shell), Options.Create(Keycloak), Options.Create(Module), NullLogger<ShellCatalogFeedClient>.Instance);
        return (client, fake);
    }

    [Fact]
    public async Task The_feed_page_is_parsed_as_the_contract_describes_it()
    {
        var (client, shell) = FeedClient();
        var entity = Guid.NewGuid();
        shell.Answers["GET /api/catalogs/changes?consumer=brocal&since=1234&limit=200"] = (HttpStatusCode.OK, $$$"""
            {"cursor":1236,"latest":1240,"hasMore":true,"requiresFullSync":false,"changes":[
              {"id":1235,"catalog":"wells","entityId":"{{{entity}}}","code":"KU-61","name":"Ku 61","action":"updated","occurredAt":"2026-11-06T18:20:11Z","actor":"admin","source":null,"replay":false,
               "data":{"id":"{{{entity}}}","code":"KU-61","name":"Ku 61","status":"producing","isActive":true}},
              {"id":900,"catalog":"fields","entityId":"{{{Guid.NewGuid()}}}","code":"KU","name":"Ku","action":"deleted","occurredAt":"2026-11-06T18:20:11Z","actor":"admin","source":"brocal","replay":true,"data":{}}
            ]}
            """);

        var page = await client.PullAsync(1234, 200, TestContext.Current.CancellationToken);

        Assert.Equal(1236, page.Cursor);
        Assert.Equal(1240, page.Latest);
        Assert.True(page.HasMore);
        Assert.False(page.RequiresFullSync);
        Assert.Equal(2, page.Changes.Count);
        Assert.Equal(entity, page.Changes[0].EntityId);
        Assert.Equal("producing", page.Changes[0].Data.GetProperty("status").GetString());
        Assert.True(page.Changes[1].Replay);
        Assert.Equal("brocal", page.Changes[1].Source);
        Assert.Equal("deleted", page.Changes[1].Action);
        Assert.Equal("Bearer service-token", shell.Requests.Last().Authorization);
    }

    [Fact]
    public async Task RequiresFullSync_and_the_report_body_follow_the_contract()
    {
        var (client, shell) = FeedClient();
        shell.Answers["GET /api/catalogs/changes?consumer=brocal&since=5&limit=200"] = (HttpStatusCode.OK, """{"cursor":1240,"latest":1240,"hasMore":false,"requiresFullSync":true,"changes":[]}""");
        shell.Answers["PUT /api/catalogs/changes/consumers/brocal"] = (HttpStatusCode.NoContent, string.Empty);

        var page = await client.PullAsync(5, 200, TestContext.Current.CancellationToken);
        await client.ReportAsync(1240, [1238, 1239], [new CatalogFeedError(1237, "El campo no existe en la réplica")], TestContext.Current.CancellationToken);

        Assert.True(page.RequiresFullSync);
        Assert.Equal(1240, page.Cursor);
        var report = shell.Requests.Last();
        using var body = JsonDocument.Parse(report.Body);
        Assert.Equal(1240, body.RootElement.GetProperty("cursor").GetInt64());
        Assert.Equal([1238L, 1239L], body.RootElement.GetProperty("applied").EnumerateArray().Select(e => e.GetInt64()));
        var error = Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal(1237, error.GetProperty("changeId").GetInt64());
        Assert.Equal("El campo no existe en la réplica", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task The_full_load_pages_through_the_catalog_and_reads_the_parent()
    {
        var (client, shell) = FeedClient();
        var field = Guid.NewGuid();
        shell.Answers["GET /api/catalogs/wells?page=1&pageSize=200"] = (HttpStatusCode.OK, $$$"""{"items":[{"id":"{{{Guid.NewGuid()}}}","code":"KU-61","name":"Ku 61","fieldId":"{{{field}}}","isActive":true,"updatedAt":"2026-11-01T00:00:00Z"}],"page":1,"pageSize":200,"totalCount":2,"totalPages":2}""");
        shell.Answers["GET /api/catalogs/wells?page=2&pageSize=200"] = (HttpStatusCode.OK, $$$"""{"items":[{"id":"{{{Guid.NewGuid()}}}","code":"KU-62","name":"Ku 62","fieldId":"{{{field}}}","isActive":false}],"page":2,"pageSize":200,"totalCount":2,"totalPages":2}""");

        var rows = await client.ReadAllAsync("wells", TestContext.Current.CancellationToken);
        var missing = await client.ReadAllAsync("pipelines", TestContext.Current.CancellationToken);

        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        Assert.Equal(field, rows[0].Data.ParentId);
        Assert.False(rows[1].Data.IsActive);
        Assert.Contains("\"code\":\"KU-61\"", rows[0].Data.Snapshot);
        Assert.Null(missing);
    }

    [Fact]
    public async Task A_shell_error_on_the_feed_surfaces_as_an_exception_with_the_status()
    {
        var (client, shell) = FeedClient();
        shell.Answers["GET /api/catalogs/changes?consumer=brocal&since=0&limit=200"] = (HttpStatusCode.BadGateway, "upstream");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.PullAsync(0, 200, TestContext.Current.CancellationToken));

        Assert.Contains("502", error.Message);
    }
}
