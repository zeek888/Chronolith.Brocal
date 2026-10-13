using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChronoLith.Brocal.IntegrationTests.Infrastructure;

namespace ChronoLith.Brocal.IntegrationTests;

/// <summary>
/// Approval workflows, business rules and scheduled reviews end to end. The definitions use thresholds no other test
/// reaches (the database is shared by the collection) and are deleted at the end of each test.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AutomationTests(BrocalApiFactory factory)
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Approvers(params string[] templates) => new { roleTemplates = templates, roleIds = Array.Empty<Guid>(), permissions = Array.Empty<string>(), tenantAdmins = false };

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response) =>
        (await response.ReadJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    private static async Task<Guid> LookupAsync(HttpClient client, string catalog, string code)
    {
        var items = await client.GetJsonAsync<JsonElement>($"{catalog}/lookup");
        return items.EnumerateArray().First(i => i.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    /// <summary>Signed ticket of water transport (185 per m³ of the general price list, plus VAT).</summary>
    private static async Task<(Guid Id, HttpResponseMessage Sign)> SignedTicketAsync(HttpClient operatorClient, decimal volume, bool photo)
    {
        var body = new
        {
            clientId = Guid.NewGuid(),
            data = new
            {
                customerId = await LookupAsync(operatorClient, "catalogs/customers", "PEP"),
                siteId = await LookupAsync(operatorClient, "catalogs/sites", "SEN-101"),
                serviceTypeId = await LookupAsync(operatorClient, "pricing/service-types", "TF"),
                serviceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-6)),
                unitIds = new[] { await LookupAsync(operatorClient, "fleet/units", "AT-01") },
                description = "Transporte de agua congénita (prueba de automatización)",
                answers = new Dictionary<string, string>
                {
                    ["fluido"] = "Agua congénita",
                    ["volumen"] = volume.ToString(CultureInfo.InvariantCulture),
                    ["viajes"] = "40",
                    ["origen"] = "Pozo Sen-101",
                    ["destino"] = "Batería Samaria II"
                }
            },
            lines = new[] { new { serviceId = await LookupAsync(operatorClient, "pricing/services", "TRANS-M3"), quantity = volume } }
        };

        var id = (await (await operatorClient.PostJsonAsync("tickets", body)).ReadJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        if (photo)
        {
            var file = await operatorClient.UploadAsync(Png, "evidencia.png", "image/png", "photo");
            (await operatorClient.PostJsonAsync($"tickets/{id}/photos", new { fileId = file, caption = "Evidencia" })).EnsureSuccessStatusCode();
        }

        var signature = await operatorClient.UploadAsync(Png, "firma.png", "image/png", "signature");
        return (id, await operatorClient.PostJsonAsync($"tickets/{id}/sign", new { signerName = "Ing. Rocío Pérez", signatureFileId = signature }));
    }

    private static async Task<JsonElement> WorkflowOfAsync(HttpClient client, string document, Guid id) =>
        await client.GetJsonAsync<JsonElement>($"automation/documents/{document}/{id}");

    [Fact]
    public async Task An_expense_report_goes_through_two_approval_steps()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin);
        var operatorClient = await factory.ClientForAsync(TestUsers.Operator);
        var office = await factory.ClientForAsync(TestUsers.Office);
        var management = await factory.ClientForAsync(TestUsers.Management);

        var workflow = await CreatedIdAsync(await admin.PostJsonAsync("automation/workflows", new
        {
            global = false,
            name = "Viáticos mayores (prueba)",
            document = "expenseReport",
            isEnabled = true,
            isMandatory = false,
            priority = 0,
            match = "all",
            conditions = new[] { new { field = "total", @operator = "greaterThan", value = "20000" } },
            steps = new[]
            {
                new { name = "Facturación", approvers = Approvers("billing"), requiredApprovals = 1, slaHours = (int?)24, allowRequester = false },
                new { name = "Dirección", approvers = Approvers("management"), requiredApprovals = 1, slaHours = (int?)null, allowRequester = false }
            }
        }));

        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var report = await CreatedIdAsync(await operatorClient.PostJsonAsync("expenses", new
            {
                employeeId = Guid.Empty,
                periodStart = today.AddDays(-5),
                periodEnd = today,
                purpose = "Comisión larga a Paraíso (prueba)",
                advanceAmount = 10_000m,
                items = new[]
                {
                    new { date = today.AddDays(-4), category = "lodging", description = "Hotel 4 noches", vendor = "Hotel Paraíso", amount = 18_000m, hasInvoice = true },
                    new { date = today.AddDays(-3), category = "meals", description = "Alimentos", vendor = (string?)null, amount = 7_000m, hasInvoice = false }
                }
            }));

            (await operatorClient.PostJsonAsync($"expenses/{report}/status", new { action = "submit" })).EnsureSuccessStatusCode();

            var pending = (await WorkflowOfAsync(office, "expenseReport", report)).GetProperty("current");
            var request = pending.GetProperty("summary").GetProperty("id").GetGuid();
            Assert.Equal("pending", pending.GetProperty("summary").GetProperty("status").GetString());
            Assert.True(pending.GetProperty("summary").GetProperty("canDecide").GetBoolean());

            // While the request is pending the direct approval is closed.
            Assert.Equal(HttpStatusCode.Conflict, (await office.PostJsonAsync($"expenses/{report}/status", new { action = "approve" })).StatusCode);

            // Management is not the approver of the first step.
            Assert.Equal(HttpStatusCode.Forbidden, (await management.PostJsonAsync($"automation/approvals/{request}/decision", new { approve = true })).StatusCode);

            var first = await (await office.PostJsonAsync($"automation/approvals/{request}/decision", new { approve = true, comment = "Comprobantes completos" })).ReadJsonAsync<JsonElement>();
            Assert.Equal(1, first.GetProperty("summary").GetProperty("currentStep").GetInt32());

            var mine = await management.GetJsonAsync<JsonElement>("automation/approvals?view=mine");
            Assert.Contains(mine.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == request);
            Assert.True(await management.GetJsonAsync<int>("automation/approvals/count") >= 1);

            var last = await (await management.PostJsonAsync($"automation/approvals/{request}/decision", new { approve = true })).ReadJsonAsync<JsonElement>();
            Assert.Equal("approved", last.GetProperty("summary").GetProperty("status").GetString());

            // The approval of the last step approves the report and tells the requester.
            Assert.Equal("approved", (await operatorClient.GetJsonAsync<JsonElement>($"expenses/{report}")).GetProperty("status").GetString());
            var notices = await operatorClient.GetJsonAsync<JsonElement>("me/notifications");
            Assert.Contains(notices.GetProperty("items").EnumerateArray(), n => n.GetProperty("event").GetString() == "approvalDecided");
        }
        finally
        {
            (await admin.DeleteAsync($"automation/workflows/{workflow}", Ct)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task A_global_invoice_workflow_holds_the_stamp_until_management_approves()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin);
        var operatorClient = await factory.ClientForAsync(TestUsers.Operator);
        var office = await factory.ClientForAsync(TestUsers.Office);
        var management = await factory.ClientForAsync(TestUsers.Management);

        var workflowBody = new
        {
            global = true,
            name = "Facturas millonarias (prueba)",
            document = "invoice",
            isEnabled = true,
            isMandatory = true,
            priority = 0,
            match = "all",
            conditions = new[] { new { field = "total", @operator = "greaterThan", value = "1000000" } },
            steps = new[] { new { name = "Dirección", approvers = Approvers("management"), requiredApprovals = 1, slaHours = (int?)null, allowRequester = false } }
        };

        // Only the platform administrator defines rules for every company.
        Assert.Equal(HttpStatusCode.Forbidden, (await management.PostJsonAsync("automation/workflows", workflowBody)).StatusCode);
        var workflow = await CreatedIdAsync(await admin.PostJsonAsync("automation/workflows", workflowBody));

        try
        {
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync($"automation/workflows/{workflow}/opt-out",
                System.Net.Http.Json.JsonContent.Create(new { optOut = true }, options: BrocalApiFactory.Json), Ct)).StatusCode);

            var (ticket, sign) = await SignedTicketAsync(operatorClient, 7_000, photo: false);
            await sign.EnsureSuccessAsync();
            (await office.PostJsonAsync("tickets/approve", new { ids = new[] { ticket } })).EnsureSuccessStatusCode();

            // "Create and stamp": the draft stays and its request is registered.
            var invoice = await CreatedIdAsync(await office.PostJsonAsync("invoices/from-tickets", new { ticketIds = new[] { ticket }, stamp = true }));
            Assert.Equal("draft", (await office.GetJsonAsync<JsonElement>($"invoices/{invoice}")).GetProperty("summary").GetProperty("status").GetString());

            var held = await office.PostAsync($"invoices/{invoice}/stamp", null, Ct);
            Assert.Equal(HttpStatusCode.Accepted, held.StatusCode);
            var pending = await held.Content.ReadFromJsonAsync<JsonElement>(BrocalApiFactory.Json, Ct);
            Assert.True(pending.GetProperty("approvalPending").GetBoolean());
            var request = pending.GetProperty("instanceId").GetGuid();

            var panel = await WorkflowOfAsync(office, "invoice", invoice);
            Assert.Equal(request, panel.GetProperty("current").GetProperty("summary").GetProperty("id").GetGuid());
            Assert.True(panel.GetProperty("current").GetProperty("summary").GetProperty("fromGlobalDefinition").GetBoolean());

            (await management.PostJsonAsync($"automation/approvals/{request}/decision", new { approve = true })).EnsureSuccessStatusCode();

            (await office.PostAsync($"invoices/{invoice}/stamp", null, Ct)).EnsureSuccessStatusCode();
            Assert.Equal("stamped", (await office.GetJsonAsync<JsonElement>($"invoices/{invoice}")).GetProperty("summary").GetProperty("status").GetString());
        }
        finally
        {
            (await admin.DeleteAsync($"automation/workflows/{workflow}", Ct)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task The_dispatch_of_an_urgent_order_waits_for_its_approval()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin);
        var office = await factory.ClientForAsync(TestUsers.Office);
        var management = await factory.ClientForAsync(TestUsers.Management);
        var customer = await LookupAsync(office, "catalogs/customers", "OPG");

        var workflow = await CreatedIdAsync(await admin.PostJsonAsync("automation/workflows", new
        {
            global = false,
            name = "Órdenes urgentes de OPG (prueba)",
            document = "workOrder",
            isEnabled = true,
            isMandatory = false,
            priority = 0,
            match = "all",
            conditions = new object[]
            {
                new { field = "priority", @operator = "in", value = "urgent" },
                new { field = "customerId", @operator = "equal", value = customer.ToString() }
            },
            steps = new[] { new { name = "Dirección", approvers = Approvers("management"), requiredApprovals = 1, slaHours = (int?)4, allowRequester = false } }
        }));

        try
        {
            var start = DateTimeOffset.UtcNow.AddDays(3);
            var saved = await (await office.PostJsonAsync("work-orders", new
            {
                dispatch = true,
                data = new
                {
                    customerId = customer,
                    siteId = await LookupAsync(office, "catalogs/sites", "BAT-PAR"),
                    serviceTypeId = await LookupAsync(office, "pricing/service-types", "LP"),
                    description = "Limpieza urgente de presa (prueba)",
                    priority = "urgent",
                    scheduledStart = start,
                    scheduledEnd = start.AddHours(8),
                    unitIds = new[] { await LookupAsync(office, "fleet/units", "AT-02") }
                }
            })).ReadJsonAsync<JsonElement>();
            var order = saved.GetProperty("id").GetGuid();

            // Saved, not dispatched: the first warning explains the hold.
            Assert.Contains("autorización", saved.GetProperty("warnings")[0].GetString());
            Assert.Equal("scheduled", (await office.GetJsonAsync<JsonElement>($"work-orders/{order}")).GetProperty("summary").GetProperty("status").GetString());

            var held = await office.PostJsonAsync($"work-orders/{order}/status", new { action = "dispatch" });
            Assert.Equal(HttpStatusCode.Accepted, held.StatusCode);
            var request = (await WorkflowOfAsync(office, "workOrder", order)).GetProperty("current").GetProperty("summary").GetProperty("id").GetGuid();

            (await management.PostJsonAsync($"automation/approvals/{request}/decision", new { approve = true })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NoContent, (await office.PostJsonAsync($"work-orders/{order}/status", new { action = "dispatch" })).StatusCode);
            Assert.Equal("dispatched", (await office.GetJsonAsync<JsonElement>($"work-orders/{order}")).GetProperty("summary").GetProperty("status").GetString());
        }
        finally
        {
            (await admin.DeleteAsync($"automation/workflows/{workflow}", Ct)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task A_blocking_rule_stops_the_close_and_tells_its_recipients()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin);
        var operatorClient = await factory.ClientForAsync(TestUsers.Operator);
        var management = await factory.ClientForAsync(TestUsers.Management);

        var rule = await CreatedIdAsync(await admin.PostJsonAsync("automation/rules", new
        {
            global = false,
            name = "Evidencia en boletas grandes (prueba)",
            document = "serviceTicket",
            triggers = new[] { "close" },
            isEnabled = true,
            isMandatory = false,
            priority = 0,
            match = "all",
            conditions = new[]
            {
                new { field = "total", @operator = "greaterThan", value = "2000000" },
                new { field = "photoCount", @operator = "lessThan", value = "1" }
            },
            effect = "block",
            message = "La boleta {folio} necesita al menos una foto de evidencia.",
            severity = "warning",
            recipients = new { audience = Approvers("management"), emails = Array.Empty<string>(), notifyActor = false }
        }));

        try
        {
            var (ticket, blocked) = await SignedTicketAsync(operatorClient, 12_000, photo: false);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
            var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>(BrocalApiFactory.Json, Ct);
            Assert.Contains("necesita al menos una foto", problem.GetProperty("detail").GetString());
            Assert.Equal("draft", (await operatorClient.GetJsonAsync<JsonElement>($"tickets/{ticket}")).GetProperty("status").GetString());

            // The attempt is notified even though the close did not happen.
            var notices = await management.GetJsonAsync<JsonElement>("me/notifications");
            Assert.Contains(notices.GetProperty("items").EnumerateArray(), n => n.GetProperty("event").GetString() == "businessRuleAlert");

            var file = await operatorClient.UploadAsync(Png, "evidencia.png", "image/png", "photo");
            (await operatorClient.PostJsonAsync($"tickets/{ticket}/photos", new { fileId = file })).EnsureSuccessStatusCode();
            var signature = await operatorClient.UploadAsync(Png, "firma.png", "image/png", "signature");
            var signed = await (await operatorClient.PostJsonAsync($"tickets/{ticket}/sign", new { signerName = "Residente", signatureFileId = signature })).ReadJsonAsync<JsonElement>();
            Assert.Equal("signed", signed.GetProperty("status").GetString());
        }
        finally
        {
            (await admin.DeleteAsync($"automation/rules/{rule}", Ct)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task A_company_can_switch_off_an_optional_global_rule()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin);
        var office = await factory.ClientForAsync(TestUsers.Office);

        var rule = await CreatedIdAsync(await admin.PostJsonAsync("automation/rules", new
        {
            global = true,
            name = "Descuentos excesivos (prueba)",
            document = "quote",
            triggers = new[] { "save" },
            isEnabled = true,
            isMandatory = false,
            priority = 0,
            match = "all",
            conditions = new[] { new { field = "maxLineDiscountPercent", @operator = "greaterThan", value = "90" } },
            effect = "block",
            message = "Un concepto tiene más de 90 % de descuento.",
            severity = "warning",
            recipients = (object?)null
        }));

        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var quote = new
            {
                customerId = await LookupAsync(office, "catalogs/customers", "OPG"),
                issuedOn = today,
                validUntil = today.AddDays(15),
                currency = "MXN",
                subject = "Cotización con descuento (prueba)",
                lines = new[] { new { serviceId = await LookupAsync(office, "pricing/services", "LIMP-PRESA"), description = "Limpieza de presa", quantity = 1m, unitName = "Servicio", unitPrice = 28_000m, discountPercent = 95m, vatRate = 0.16m } }
            };

            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await office.PostJsonAsync("quotes", quote)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await office.GetAsync("automation/rules", Ct)).StatusCode);

            var rules = await admin.GetJsonAsync<JsonElement>("automation/rules");
            Assert.Contains(rules.EnumerateArray(), r => r.GetProperty("id").GetGuid() == rule && r.GetProperty("isGlobal").GetBoolean());

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"automation/rules/{rule}/opt-out",
                System.Net.Http.Json.JsonContent.Create(new { optOut = true }, options: BrocalApiFactory.Json), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await office.PostJsonAsync("quotes", quote)).StatusCode);
        }
        finally
        {
            (await admin.PutAsync($"automation/rules/{rule}/opt-out", System.Net.Http.Json.JsonContent.Create(new { optOut = false }, options: BrocalApiFactory.Json), Ct)).EnsureSuccessStatusCode();
            (await admin.DeleteAsync($"automation/rules/{rule}", Ct)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task The_platform_administrator_runs_the_reviews_and_sees_their_log()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin, tenant: null);
        var office = await factory.ClientForAsync(TestUsers.Office, tenant: null);

        Assert.Equal(HttpStatusCode.Forbidden, (await office.PostAsync("platform/jobs/run", null, Ct)).StatusCode);

        var run = await (await admin.PostAsync("platform/jobs/run", null, Ct)).ReadJsonAsync<JsonElement>();
        var demo = run.GetProperty("details").EnumerateArray().First(d => d.GetProperty("slug").GetString() == BrocalApiFactory.DemoTenant);
        Assert.Equal(JsonValueKind.Null, demo.GetProperty("error").ValueKind);

        // The demo company has a driver license and permits expiring within the warning window.
        var counts = demo.GetProperty("counts");
        Assert.True(counts.GetProperty("licenses").GetInt32() >= 1);
        Assert.True(counts.GetProperty("documents").GetInt32() >= 1);

        var log = await admin.GetJsonAsync<JsonElement>("platform/jobs");
        Assert.Contains(log.GetProperty("runs").EnumerateArray(), r => r.GetProperty("id").GetGuid() == run.GetProperty("id").GetGuid());
    }
}
