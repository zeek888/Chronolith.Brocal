using System.Net;
using System.Text;
using System.Text.Json;
using ChronoLith.Brocal.IntegrationTests.Infrastructure;

namespace ChronoLith.Brocal.IntegrationTests;

/// <summary>From the field to the invoice: capture (offline-safe), evidence, signature, approval, CFDI and collection.</summary>
[Collection(ApiCollection.Name)]
public sealed class TicketLifecycleTests(BrocalApiFactory factory)
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");

    private static async Task<Guid> LookupAsync(HttpClient client, string catalog, string code)
    {
        var items = await client.GetJsonAsync<JsonElement>($"{catalog}/lookup");
        return items.EnumerateArray().First(i => i.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private static async Task<object> TicketAsync(HttpClient client, Guid clientId, decimal volume)
    {
        var customer = await LookupAsync(client, "catalogs/customers", "PEP");
        var site = await LookupAsync(client, "catalogs/sites", "SEN-101");
        var type = await LookupAsync(client, "pricing/service-types", "TF");
        var service = await LookupAsync(client, "pricing/services", "TRANS-M3");
        var unit = await LookupAsync(client, "fleet/units", "AT-01");

        return new
        {
            clientId,
            data = new
            {
                customerId = customer,
                siteId = site,
                serviceTypeId = type,
                serviceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-6)),
                unitIds = new[] { unit },
                description = "Transporte de agua congénita (prueba de integración)",
                customerRepresentative = "Ing. Rocío Pérez",
                answers = new Dictionary<string, string>
                {
                    ["fluido"] = "Agua congénita",
                    ["volumen"] = volume.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["viajes"] = "2",
                    ["origen"] = "Pozo Sen-101",
                    ["destino"] = "Batería Samaria II"
                }
            },
            lines = new[] { new { serviceId = service, quantity = volume } }
        };
    }

    [Fact]
    public async Task A_signed_ticket_becomes_a_stamped_invoice_and_a_collected_receivable()
    {
        var operatorClient = await factory.ClientForAsync(TestUsers.Operator);
        var office = await factory.ClientForAsync(TestUsers.Office);

        // Capture: retrying the same offline capture does not duplicate the ticket.
        var body = await TicketAsync(operatorClient, Guid.NewGuid(), 40);
        var first = await (await operatorClient.PostJsonAsync("tickets", body)).ReadJsonAsync<JsonElement>();
        var retry = await (await operatorClient.PostJsonAsync("tickets", body)).ReadJsonAsync<JsonElement>();
        var ticketId = first.GetProperty("id").GetGuid();
        Assert.True(first.GetProperty("created").GetBoolean());
        Assert.False(retry.GetProperty("created").GetBoolean());
        Assert.Equal(ticketId, retry.GetProperty("id").GetGuid());

        // Evidence and signature.
        var photo = await operatorClient.UploadAsync(Png, "llegada.png", "image/png", "photo");
        (await operatorClient.PostJsonAsync($"tickets/{ticketId}/photos", new { fileId = photo, caption = "Llegada a locación" })).EnsureSuccessStatusCode();
        var signature = await operatorClient.UploadAsync(Png, "firma.png", "image/png", "signature");
        var signed = await (await operatorClient.PostJsonAsync($"tickets/{ticketId}/sign", new { signerName = "Ing. Rocío Pérez", signerPosition = "Residente", signatureFileId = signature }))
            .ReadJsonAsync<JsonElement>();
        Assert.Equal("signed", signed.GetProperty("status").GetString());
        var total = signed.GetProperty("total").GetDecimal();
        Assert.True(total > 0);

        var pdf = await office.GetAsync($"tickets/{ticketId}/pdf");
        await pdf.EnsureSuccessAsync();
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(), 0, 4));

        // Office validates and invoices in one click (simulated PAC).
        (await office.PostJsonAsync("tickets/approve", new { ids = new[] { ticketId } })).EnsureSuccessStatusCode();
        var invoice = await (await office.PostJsonAsync("invoices/from-tickets", new { ticketIds = new[] { ticketId }, stamp = true })).ReadJsonAsync<JsonElement>();
        var invoiceId = invoice.GetProperty("id").GetGuid();

        var detail = await office.GetJsonAsync<JsonElement>($"invoices/{invoiceId}");
        var summary = detail.GetProperty("summary");
        Assert.Equal("stamped", summary.GetProperty("status").GetString());
        Assert.Equal(total, summary.GetProperty("total").GetDecimal());
        Assert.False(string.IsNullOrEmpty(summary.GetProperty("uuid").GetString()));

        var xml = await office.GetStringAsync($"invoices/{invoiceId}/xml");
        Assert.Contains("cfdi:Comprobante", xml);
        Assert.Contains("TimbreFiscalDigital", xml);

        var ticket = await office.GetJsonAsync<JsonElement>($"tickets/{ticketId}");
        Assert.Equal("invoiced", ticket.GetProperty("status").GetString());

        // Collection: a partial payment reduces the balance.
        var payment = await office.PostJsonAsync("collections/payments", new
        {
            customerId = summary.GetProperty("customerId").GetGuid(),
            paidAt = DateTimeOffset.UtcNow,
            paymentForm = "03",
            reference = "SPEI-INTEGRACION",
            applications = new[] { new { invoiceId, amount = Math.Round(total / 2, 2) } }
        });
        Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
        var after = await office.GetJsonAsync<JsonElement>($"invoices/{invoiceId}");
        Assert.Equal(total - Math.Round(total / 2, 2), after.GetProperty("summary").GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task A_signed_ticket_cannot_be_edited()
    {
        var operatorClient = await factory.ClientForAsync(TestUsers.Operator);
        var body = await TicketAsync(operatorClient, Guid.NewGuid(), 12);
        var ticketId = (await (await operatorClient.PostJsonAsync("tickets", body)).ReadJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var signature = await operatorClient.UploadAsync(Png, "firma.png", "image/png", "signature");
        (await operatorClient.PostJsonAsync($"tickets/{ticketId}/sign", new { signerName = "Residente", signatureFileId = signature })).EnsureSuccessStatusCode();

        var edit = await operatorClient.PutAsync($"tickets/{ticketId}", System.Net.Http.Json.JsonContent.Create(body, options: BrocalApiFactory.Json));

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    [Fact]
    public async Task Concurrent_captures_get_unique_consecutive_folios()
    {
        var operatorClient = await factory.ClientForAsync(TestUsers.Operator);
        var bodies = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => TicketAsync(operatorClient, Guid.NewGuid(), 10)));

        var responses = await Task.WhenAll(bodies.Select(b => operatorClient.PostJsonAsync("tickets", b)));
        var folios = new List<long>();
        foreach (var response in responses)
        {
            var folio = (await response.ReadJsonAsync<JsonElement>()).GetProperty("folio").GetString()!;
            folios.Add(long.Parse(folio[(folio.LastIndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(8, folios.Distinct().Count());
        Assert.Equal(7, folios.Max() - folios.Min());
    }
}

[Collection(ApiCollection.Name)]
public sealed class PortalAndIntegrationTests(BrocalApiFactory factory)
{
    [Fact]
    public async Task Customer_portal_sees_only_its_customer_and_no_drafts()
    {
        var customer = await factory.ClientForAsync(TestUsers.Customer);

        var tickets = await customer.GetJsonAsync<JsonElement>("tickets?pageSize=200");
        Assert.True(tickets.GetProperty("totalCount").GetInt32() > 0);
        Assert.All(tickets.GetProperty("items").EnumerateArray(), t => Assert.NotEqual("draft", t.GetProperty("status").GetString()));
        Assert.Single(tickets.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("customerId").GetGuid()).Distinct());

        var summary = await customer.GetJsonAsync<JsonElement>("portal/summary");
        Assert.Equal("Pemex Exploración y Producción", summary.GetProperty("customerName").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("catalogs/customers")).StatusCode);

        var report = await customer.GetAsync($"reports/monthly?year={DateTime.UtcNow.Year}&month={DateTime.UtcNow.Month}");
        await report.EnsureSuccessAsync();
        Assert.Equal("application/pdf", report.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Gps_positions_are_accepted_only_with_a_valid_api_key()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin);
        var key = await (await admin.PostJsonAsync("integrations/api-keys", new { name = "GPS de prueba", scopes = new[] { "gps.write" } })).ReadJsonAsync<JsonElement>();
        var plain = key.GetProperty("plainValue").GetString()!;

        var gps = factory.AnonymousClient();
        var body = new { source = "test", positions = new[] { new { unitCode = "AT-02", recordedAt = DateTimeOffset.UtcNow, latitude = 18.01m, longitude = -93.37m, speedKmh = 40m } } };

        using (var request = new HttpRequestMessage(HttpMethod.Post, "integrations/gps/positions") { Content = System.Net.Http.Json.JsonContent.Create(body, options: BrocalApiFactory.Json) })
        {
            request.Headers.Add("X-Api-Key", plain);
            var accepted = await (await gps.SendAsync(request)).ReadJsonAsync<JsonElement>();
            Assert.Equal(1, accepted.GetProperty("accepted").GetInt32());
        }

        (await admin.DeleteAsync($"integrations/api-keys/{key.GetProperty("key").GetProperty("id").GetGuid()}")).EnsureSuccessStatusCode();
        using (var request = new HttpRequestMessage(HttpMethod.Post, "integrations/gps/positions") { Content = System.Net.Http.Json.JsonContent.Create(body, options: BrocalApiFactory.Json) })
        {
            request.Headers.Add("X-Api-Key", plain);
            Assert.Equal(HttpStatusCode.Unauthorized, (await gps.SendAsync(request)).StatusCode);
        }

        // A key of another scope does not open the GPS route.
        var exportKey = await (await admin.PostJsonAsync("integrations/api-keys", new { name = "Exportación", scopes = new[] { "export.read" } })).ReadJsonAsync<JsonElement>();
        using (var request = new HttpRequestMessage(HttpMethod.Post, "integrations/gps/positions") { Content = System.Net.Http.Json.JsonContent.Create(body, options: BrocalApiFactory.Json) })
        {
            request.Headers.Add("X-Api-Key", exportKey.GetProperty("plainValue").GetString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await gps.SendAsync(request)).StatusCode);
        }
    }

    [Fact]
    public async Task Every_export_dataset_produces_a_workbook()
    {
        var admin = await factory.ClientForAsync(TestUsers.Admin);
        var datasets = await admin.GetJsonAsync<JsonElement>("integrations/exports");

        foreach (var dataset in datasets.EnumerateArray().Select(d => d.GetProperty("key").GetString()))
        {
            var response = await admin.GetAsync($"integrations/exports/{dataset}");
            await response.EnsureSuccessAsync();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 1000 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K', $"{dataset} no es un archivo xlsx");
        }
    }
}
