using System.Net;
using System.Text.Json;
using ChronoLith.Brocal.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ChronoLith.Brocal.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SecurityTests(BrocalApiFactory factory)
{
    /// <summary>Machine-to-machine routes (API key) and signed file links.</summary>
    private static readonly HashSet<string> AnonymousRoutes =
    [
        "api/files/{id:guid}/content",
        "api/integrations/gps/positions",
        "api/integrations/feeds/tickets",
        "api/integrations/feeds/exports/{dataset}"
    ];

    /// <summary>Routes used before a company is selected.</summary>
    private static readonly HashSet<string> SessionOnlyRoutes = ["api/me/session", "api/me/context", "api/platform/modules", "api/platform/chronolith-theme"];

    [Fact]
    public void Every_endpoint_requires_a_company_permission_unless_explicitly_listed()
    {
        _ = factory.Server;
        var problems = new List<string>();
        foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var route = endpoint.RoutePattern.RawText?.TrimStart('/') ?? string.Empty;
            if (!route.StartsWith("api/", StringComparison.Ordinal))
            {
                continue;
            }

            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault();
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                if (!AnonymousRoutes.Contains(route))
                {
                    problems.Add($"{method} {route}: permite acceso anónimo");
                }

                continue;
            }

            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>().ToList();
            if (policies.Count == 0 && !SessionOnlyRoutes.Contains(route))
            {
                problems.Add($"{method} {route}: solo exige sesión (falta empresa o permiso)");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task Requests_without_a_session_are_rejected()
    {
        var response = await factory.AnonymousClient().GetAsync("tickets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_without_a_membership_cannot_enter_the_company()
    {
        var stranger = await factory.ClientForAsync(TestUsers.Stranger);

        var response = await stranger.GetAsync("tickets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Field_operators_only_reach_their_permissions_and_their_own_tickets()
    {
        var office = await factory.ClientForAsync(TestUsers.Office);
        var operatorClient = await factory.ClientForAsync(TestUsers.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("catalogs/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("dashboard")).StatusCode);

        var all = await office.GetJsonAsync<JsonElement>("tickets?pageSize=100");
        var own = await operatorClient.GetJsonAsync<JsonElement>("tickets?pageSize=100");
        Assert.True(own.GetProperty("totalCount").GetInt32() < all.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Enum_filters_accept_the_same_names_as_the_json_payloads()
    {
        var office = await factory.ClientForAsync(TestUsers.Office);

        var camel = await office.GetJsonAsync<JsonElement>("tickets?status=signed&status=approved");
        var pascal = await office.GetJsonAsync<JsonElement>("tickets?status=Signed&status=Approved");

        Assert.Equal(camel.GetProperty("totalCount").GetInt32(), pascal.GetProperty("totalCount").GetInt32());
        Assert.All(camel.GetProperty("items").EnumerateArray(), t => Assert.Contains(t.GetProperty("status").GetString(), new[] { "signed", "approved" }));
    }
}

[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests(BrocalApiFactory factory)
{
    [Fact]
    public async Task Companies_do_not_see_each_other_and_disabled_modules_are_closed()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin, tenant: null);
        var created = await admin.PostJsonAsync("platform/tenants", new
        {
            slug = "aislada",
            name = "Empresa aislada",
            plan = "Básico",
            modules = new[] { "tickets" },
            adminEmail = TestUsers.Admin.Email,
            adminName = "Administrador",
            provisionAdminAccount = false
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var isolated = await factory.ClientForAsync(TestUsers.Admin, tenant: "aislada");
        var demo = await factory.ClientForAsync(TestUsers.Admin);

        Assert.Equal(0, (await isolated.GetJsonAsync<JsonElement>("tickets")).GetProperty("totalCount").GetInt32());
        Assert.True((await demo.GetJsonAsync<JsonElement>("tickets")).GetProperty("totalCount").GetInt32() > 0);
        Assert.Equal(HttpStatusCode.Forbidden, (await isolated.GetAsync("work-orders")).StatusCode);

        var office = await factory.ClientForAsync(TestUsers.Office, tenant: "aislada");
        Assert.Equal(HttpStatusCode.Forbidden, (await office.GetAsync("tickets")).StatusCode);
    }

    [Fact]
    public async Task Platform_administration_is_reserved_to_the_platform_role()
    {
        var office = await factory.ClientForAsync(TestUsers.Office, tenant: null);

        Assert.Equal(HttpStatusCode.Forbidden, (await office.GetAsync("platform/tenants")).StatusCode);
    }

    [Fact]
    public async Task Starting_a_session_binds_invitations_of_other_companies()
    {
        var supervisor = new TestUser("supervisor2", "supervisor2@sps-demo.mx");
        var demoAdmin = await factory.ClientForAsync(TestUsers.Admin);
        var invited = await demoAdmin.PostJsonAsync("company/members", new { email = supervisor.Email, fullName = "Supervisor dos", isTenantAdmin = false, roleIds = Array.Empty<Guid>(), createAccount = false });
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);

        // First sign-in: binds the demo membership. From then on the requests resolve the demo company.
        var client = await factory.ClientForAsync(supervisor);

        var platformAdmin = await factory.ClientForAsync(TestUsers.Admin, tenant: null);
        var created = await platformAdmin.PostJsonAsync("platform/tenants", new
        {
            slug = "segunda",
            name = "Segunda empresa",
            plan = "Básico",
            modules = new[] { "tickets" },
            adminEmail = supervisor.Email,
            adminName = "Supervisor dos",
            provisionAdminAccount = false
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // The request works on the demo company and the invitation belongs to the new one.
        var session = await (await client.PostAsync("me/session", null)).ReadJsonAsync<JsonElement>();
        Assert.Equal(1, session.GetProperty("bound").GetInt32());
        Assert.Equal(2, session.GetProperty("memberships").GetInt32());

        var context = await client.GetJsonAsync<JsonElement>("me/context");
        Assert.Contains(context.GetProperty("tenants").EnumerateArray(), t => t.GetProperty("slug").GetString() == "segunda");
    }
}
