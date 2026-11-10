using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChronoLith.Brocal.IntegrationTests.Infrastructure;
using ChronoLith.Licensing;
using ChronoLith.Licensing.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChronoLith.Brocal.IntegrationTests;

/// <summary>
/// ChronoLith license of the installation (standalone mode). The tests that load another license restore a valid one
/// without limits at the end, because every test of the collection shares the API.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LicenseTests(BrocalApiFactory factory)
{
    [Fact]
    public async Task Every_user_sees_the_status_and_only_platform_administrators_the_detail()
    {
        var office = await factory.ClientForAsync(TestUsers.Office);
        var admin = await factory.ClientForAsync(TestUsers.Admin, tenant: null);

        var status = await office.GetJsonAsync<JsonElement>("license/status");
        Assert.True(status.GetProperty("allowsWrites").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await office.GetAsync("license")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await office.PutAsJsonAsync("license", new { token = TestLicenses.Valid() })).StatusCode);

        var detail = await admin.GetJsonAsync<JsonElement>("license");
        Assert.NotEqual(Guid.Empty, detail.GetProperty("instanceId").GetGuid());
        Assert.Equal("brocal", detail.GetProperty("product").GetString());
        var usage = detail.GetProperty("usage").EnumerateArray().Select(u => u.GetProperty("key").GetString()).ToList();
        Assert.Equal(["apikeys", "companies", "employees", "portalusers", "storagemb", "tickets", "units", "users"], usage);

        var invalid = await admin.PutAsJsonAsync("license", new { token = "no-es-una-licencia" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("malformed", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("problem").GetString());
    }

    [Fact]
    public async Task The_limits_of_users_and_companies_answer_403()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin, tenant: null);
        var demoAdmin = await factory.ClientForAsync(TestUsers.Admin);
        var usage = await UsageAsync(admin);

        try
        {
            await LoadAsync(admin, TestLicenses.Issue(TestLicenses.Today.AddDays(365), new Dictionary<string, int>
            {
                ["users"] = usage["users"],
                ["companies"] = usage["companies"]
            }));

            var invited = await demoAdmin.PostJsonAsync("company/members", new { email = "fuera-de-licencia@sps-demo.mx", fullName = "Fuera de licencia", isTenantAdmin = false, roleIds = Array.Empty<Guid>(), createAccount = false });
            await AssertLimitAsync(invited, "users", usage["users"]);

            var company = await admin.PostJsonAsync("platform/tenants", new
            {
                slug = "fuera-de-licencia",
                name = "Empresa fuera de licencia",
                plan = "Básico",
                modules = new[] { "tickets" },
                adminEmail = TestUsers.Admin.Email,
                adminName = "Administrador",
                provisionAdminAccount = false
            });
            await AssertLimitAsync(company, "companies", usage["companies"]);
        }
        finally
        {
            await LoadAsync(admin, TestLicenses.Valid());
        }

        // Without limits again.
        var allowed = await demoAdmin.PostJsonAsync("company/members", new { email = "fuera-de-licencia@sps-demo.mx", fullName = "Fuera de licencia", isTenantAdmin = false, roleIds = Array.Empty<Guid>(), createAccount = false });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task The_installation_wide_limit_of_api_keys_adds_to_the_quota_of_the_company()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin, tenant: null);
        var demoAdmin = await factory.ClientForAsync(TestUsers.Admin);
        var usage = await UsageAsync(admin);

        try
        {
            await LoadAsync(admin, TestLicenses.Issue(TestLicenses.Today.AddDays(365), new Dictionary<string, int> { ["apikeys"] = usage["apikeys"] }));

            var rejected = await demoAdmin.PostJsonAsync("integrations/api-keys", new { name = "Fuera de licencia", scopes = new[] { "export.read" } });
            await AssertLimitAsync(rejected, "apikeys", usage["apikeys"]);
        }
        finally
        {
            await LoadAsync(admin, TestLicenses.Valid());
        }
    }

    [Fact]
    public async Task A_license_with_modules_turns_off_the_others_without_changing_the_companies()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin, tenant: null);
        var demoAdmin = await factory.ClientForAsync(TestUsers.Admin);
        Assert.Equal(HttpStatusCode.OK, (await demoAdmin.GetAsync("invoices")).StatusCode);

        try
        {
            // Service tickets only: it also grants their dependency (price lists) and the core modules.
            await LoadAsync(admin, TestLicenses.Issue(TestLicenses.Today.AddDays(365), modules: ["tickets"]));

            var catalog = (await admin.GetJsonAsync<JsonElement>("platform/modules")).EnumerateArray()
                .ToDictionary(m => m.GetProperty("code").GetString()!, m => m.GetProperty("licensed").GetBoolean());
            Assert.True(catalog["tickets"]);
            Assert.True(catalog["pricing"]);
            Assert.True(catalog["catalogs"]);
            Assert.False(catalog["invoicing"]);
            Assert.False(catalog["fleet"]);

            var context = await demoAdmin.GetJsonAsync<JsonElement>("me/context");
            var enabled = context.GetProperty("tenant").GetProperty("enabledModules").EnumerateArray().Select(m => m.GetString()).ToList();
            Assert.Contains("tickets", enabled);
            Assert.DoesNotContain("invoicing", enabled);
            Assert.DoesNotContain(context.GetProperty("tenant").GetProperty("permissions").EnumerateArray(), p => p.GetString()!.StartsWith("invoicing.", StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.Forbidden, (await demoAdmin.GetAsync("invoices")).StatusCode);

            // A company cannot get a module the license does not grant.
            var company = await admin.PostJsonAsync("platform/tenants", new
            {
                slug = "modulo-sin-licencia",
                name = "Módulo sin licencia",
                plan = "Básico",
                modules = new[] { "tickets", "invoicing" },
                adminEmail = TestUsers.Admin.Email,
                adminName = "Administrador",
                provisionAdminAccount = false
            });
            Assert.Equal(HttpStatusCode.Forbidden, company.StatusCode);
            Assert.Equal("License.ModuleNotGranted", (await company.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        finally
        {
            await LoadAsync(admin, TestLicenses.Valid());
        }

        // The demo company kept its selection: a license with every module turns invoicing back on.
        Assert.Equal(HttpStatusCode.OK, (await demoAdmin.GetAsync("invoices")).StatusCode);
    }

    [Fact]
    public async Task An_expired_license_leaves_the_installation_read_only_until_a_new_one_is_loaded()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin, tenant: null);
        var office = await factory.ClientForAsync(TestUsers.Office);
        var demoAdmin = await factory.ClientForAsync(TestUsers.Admin);
        var member = new { email = "licencia-vencida@sps-demo.mx", fullName = "Licencia vencida", isTenantAdmin = false, roleIds = Array.Empty<Guid>(), createAccount = false };

        try
        {
            // Expired 60 days ago with 30 days of grace: read-only.
            await LoadAsync(admin, TestLicenses.Issue(TestLicenses.Today.AddDays(-60), graceDays: 30));

            var status = await office.GetJsonAsync<JsonElement>("license/status");
            Assert.Equal("expired", status.GetProperty("state").GetString());
            Assert.False(status.GetProperty("allowsWrites").GetBoolean());

            // Reads and the session keep working; business writes answer 403 with the problem type of the license.
            Assert.Equal(HttpStatusCode.OK, (await office.GetAsync("tickets")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await office.PostAsync("me/session", null)).StatusCode);
            var rejected = await demoAdmin.PostJsonAsync("company/members", member);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            Assert.Equal(LicenseProblemTypes.Expired, (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString());

            // Loading a new license is allowed while expired and ends the read-only mode without restarting.
            await LoadAsync(admin, TestLicenses.Valid());
            Assert.Equal(HttpStatusCode.Created, (await demoAdmin.PostJsonAsync("company/members", member)).StatusCode);
        }
        finally
        {
            await LoadAsync(admin, TestLicenses.Valid());
        }
    }

    [Fact]
    public async Task The_api_does_not_start_without_a_license()
    {
        // Same database and standalone configuration as the shared API (it is already initialized), but no
        // License:Key and the embedded keys only: the license stored by other tests is not valid here either.
        _ = factory.Server;
        await using var unlicensed = new UnlicensedApiFactory();

        var exception = Record.Exception(() => unlicensed.Server);

        Assert.NotNull(exception);
        Assert.Contains(Chain(exception), e => e is LicenseRequiredException);
    }

    private static async Task LoadAsync(HttpClient admin, string token)
    {
        var response = await admin.PutAsJsonAsync("license", new { token });
        await response.EnsureSuccessAsync();
    }

    private static async Task<Dictionary<string, int>> UsageAsync(HttpClient admin)
    {
        var detail = await admin.GetJsonAsync<JsonElement>("license");
        return detail.GetProperty("usage").EnumerateArray().ToDictionary(u => u.GetProperty("key").GetString()!, u => u.GetProperty("used").GetInt32());
    }

    private static async Task AssertLimitAsync(HttpResponseMessage response, string key, int limit)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(LicenseProblemTypes.LimitReached, problem.GetProperty("type").GetString());
        Assert.Equal(key, problem.GetProperty("key").GetString());
        Assert.Equal(limit, problem.GetProperty("limit").GetInt32());
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Chain))
                {
                    yield return inner;
                }
            }
        }
    }

    private sealed class UnlicensedApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not Development: appsettings.Development.json carries the development license.
            builder.UseEnvironment("Testing");
        }
    }
}
