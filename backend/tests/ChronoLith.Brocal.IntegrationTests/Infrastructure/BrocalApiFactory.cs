using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace ChronoLith.Brocal.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API (standalone mode, migrations and the demo company) against PostgreSQL in a container. Keycloak
/// is replaced by <see cref="TestAuthHandler"/>: each client signs in as one of the demo users.
/// </summary>
public sealed class BrocalApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DemoTenant = "demo";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("chronolith_brocal")
        .WithUsername("brocal")
        .WithPassword("brocal")
        .Build();

    private readonly string _filesPath = Path.Combine(Path.GetTempPath(), "brocal-tests", Guid.NewGuid().ToString("N"));

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();

        // Program reads part of the configuration while registering services, so it goes in as environment variables.
        Environment.SetEnvironmentVariable("ConnectionStrings__BrocalDb", _database.GetConnectionString());
        Environment.SetEnvironmentVariable("Tenancy__Mode", "Standalone");
        Environment.SetEnvironmentVariable("Database__ApplyMigrationsOnStartup", "true");
        Environment.SetEnvironmentVariable("Database__SeedDemoTenant", "true");
        Environment.SetEnvironmentVariable("Database__DemoAdminEmail", TestUsers.Admin.Email);
        Environment.SetEnvironmentVariable("Jobs__Enabled", "false");
        Environment.SetEnvironmentVariable("Identity__ProvisionUsers", "false");
        Environment.SetEnvironmentVariable("Files__RootPath", _filesPath);
        Environment.SetEnvironmentVariable("Files__SigningKey", "integration-tests-signing-key-0123456789");
        Environment.SetEnvironmentVariable("Keycloak__AuthServerUrl", "http://keycloak.invalid");
        Environment.SetEnvironmentVariable("Keycloak__Realm", "brocal");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
        try
        {
            Directory.Delete(_filesPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services => services
            .AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null));
    }

    /// <summary>Client signed in as <paramref name="user"/> working on <paramref name="tenant"/> (binds the membership first).</summary>
    public async Task<HttpClient> ClientForAsync(TestUser user, string? tenant = DemoTenant)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost/api/") });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.Username);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, user.Email);
        if (user.Roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', user.Roles));
        }

        (await client.PostAsync("me/session", null)).EnsureSuccessStatusCode();
        if (tenant is not null)
        {
            client.DefaultRequestHeaders.Add("X-Tenant", tenant);
        }

        return client;
    }

    public HttpClient AnonymousClient() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost/api/") });
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<BrocalApiFactory>
{
    public const string Name = "api";
}

public sealed record TestUser(string Username, string Email, params string[] Roles);

/// <summary>Users of the demo company (memberships created by the seeder) plus the platform administrator.</summary>
public static class TestUsers
{
    public static readonly TestUser Admin = new("admin", "admin@brocal.test", "brocal-platform-admin");
    public static readonly TestUser Office = new("oficina", "oficina@sps-demo.mx");
    public static readonly TestUser Operator = new("operador", "operador@sps-demo.mx");
    public static readonly TestUser Management = new("direccion", "direccion@sps-demo.mx");
    public static readonly TestUser Customer = new("residente", "residente@pemex-demo.mx");
    public static readonly TestUser Stranger = new("extrano", "extrano@otra-empresa.mx");
}

/// <summary>Builds the identity Keycloak would put in the token from test headers (no header: anonymous).</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string EmailHeader = "X-Test-Email";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrWhiteSpace(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("sub", $"kc-{user}"),
            new("preferred_username", user!),
            new("email", Request.Headers[EmailHeader].ToString()),
            new("email_verified", "true"),
            new("name", $"{user} (prueba)")
        };
        claims.AddRange(Request.Headers[RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim("role", r.Trim())));

        var identity = new ClaimsIdentity(claims, SchemeName, "preferred_username", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public static class HttpClientJsonExtensions
{
    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        await response.EnsureSuccessAsync();
        return (await response.Content.ReadFromJsonAsync<T>(BrocalApiFactory.Json))!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, BrocalApiFactory.Json);

    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response)
    {
        await response.EnsureSuccessAsync();
        return (await response.Content.ReadFromJsonAsync<T>(BrocalApiFactory.Json))!;
    }

    /// <summary>Like EnsureSuccessStatusCode, with the problem details in the message.</summary>
    public static async Task EnsureSuccessAsync(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            Assert.Fail($"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    public static async Task<Guid> UploadAsync(this HttpClient client, byte[] content, string fileName, string contentType, string category)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(category), "category");
        var response = await client.PostAsync("files", form);
        return (await response.ReadJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
