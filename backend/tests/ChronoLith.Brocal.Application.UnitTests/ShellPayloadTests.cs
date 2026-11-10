using System.Text.Json;
using ChronoLith.Brocal.Application.Features.SharedCatalogs;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Application.UnitTests;

/// <summary>What Brocal sends to the master: the shared fields of the record, the rest from the master snapshot.</summary>
public sealed class ShellPayloadTests
{
    private static readonly Guid Field = Guid.NewGuid();
    private static readonly Guid Operator = Guid.NewGuid();

    [Fact]
    public void A_well_keeps_the_master_fields_brocal_does_not_know()
    {
        var well = Guid.NewGuid();
        var site = Site.Create(new SiteData("KU-61", "Ku 61 editado", SiteType.Well, null, "Ku", null, null, null, 18.5m, -93.1m, well, null, Field)).Value;
        var master = SharedCatalogItem.Create(well, SharedCatalogNames.Wells, new SharedCatalogData("KU-61", "Ku 61", Field, true, null,
            """{"status":"drilling","type":"exploration","trajectory":"horizontal","rigId":"3f1b2e1a-0000-4000-8000-000000000001","notes":"nota del maestro","spudDate":"2026-01-15","plannedDepthMeters":4200}"""), null, DateTimeOffset.UtcNow);

        using var body = JsonDocument.Parse(ShellPayloads.ForSite(SharedCatalogNames.Wells, site, master, Operator));
        var root = body.RootElement;

        Assert.Equal("KU-61", root.GetProperty("code").GetString());
        Assert.Equal("Ku 61 editado", root.GetProperty("name").GetString());
        Assert.Equal(Field, root.GetProperty("fieldId").GetGuid());
        Assert.Equal(18.5m, root.GetProperty("latitude").GetDecimal());
        Assert.Equal("drilling", root.GetProperty("status").GetString());
        Assert.Equal("exploration", root.GetProperty("type").GetString());
        Assert.Equal("horizontal", root.GetProperty("trajectory").GetString());
        Assert.Equal("nota del maestro", root.GetProperty("notes").GetString());
        Assert.Equal("2026-01-15", root.GetProperty("spudDate").GetString());
        Assert.Equal(4200, root.GetProperty("plannedDepthMeters").GetInt32());
        Assert.Equal(Operator, root.GetProperty("operatorId").GetGuid());
    }

    [Fact]
    public void A_new_well_gets_sensible_defaults_for_the_master()
    {
        var site = Site.Create(new SiteData("SEN-101", "Sen 101", SiteType.Well, null, null, null, null, null, null, null, null, null, Field)).Value;

        using var body = JsonDocument.Parse(ShellPayloads.ForSite(SharedCatalogNames.Wells, site, null, null));
        var root = body.RootElement.GetProperty("fieldId");

        Assert.Equal(Field, root.GetGuid());
        Assert.Equal("development", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("vertical", body.RootElement.GetProperty("trajectory").GetString());
        Assert.Equal("producing", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("operatorId").ValueKind);
    }

    [Fact]
    public void A_facility_sends_only_its_input_fields()
    {
        var site = Site.Create(new SiteData("BAT-PAR", "Batería Paredón", SiteType.Facility, null, null, null, null, null, 17.8m, -93.3m, null, null)).Value;

        using var body = JsonDocument.Parse(ShellPayloads.ForSite(SharedCatalogNames.Facilities, site, null, null));

        Assert.Equal("other", body.RootElement.GetProperty("type").GetString());
        Assert.False(body.RootElement.TryGetProperty("status", out _));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("fieldId").ValueKind);
    }

    [Fact]
    public void A_customer_becomes_an_operator_company_with_its_rfc()
    {
        var customer = Customer.Create(new CustomerData("PEP", "Pemex Exploración y Producción", null, "PEP920716AB1", null, null, null, null, "PPD", 60, null, null, null, null, null, true, null, null)).Value;

        using var body = JsonDocument.Parse(ShellPayloads.ForCustomer(customer, null));

        Assert.Equal("PEP", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("operator", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("PEP920716AB1", body.RootElement.GetProperty("taxId").GetString());
        Assert.Equal("MX", body.RootElement.GetProperty("country").GetString());
    }

    [Fact]
    public void A_customer_linked_to_a_company_keeps_the_type_of_the_master()
    {
        var company = Guid.NewGuid();
        var customer = Customer.Create(new CustomerData("HAL", "Halliburton", null, "XEXX010101000", null, null, null, null, "PUE", 30, null, null, null, null, company, false, null, null)).Value;
        var master = SharedCatalogItem.Create(company, SharedCatalogNames.Companies, new SharedCatalogData("HAL", "Halliburton de México", null, true, null,
            """{"type":"serviceProvider","taxId":"HAL123456AB1","country":"US","notes":"contratista"}"""), null, DateTimeOffset.UtcNow);

        using var body = JsonDocument.Parse(ShellPayloads.ForCustomer(customer, master));

        Assert.Equal("serviceProvider", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("HAL123456AB1", body.RootElement.GetProperty("taxId").GetString());
        Assert.Equal("US", body.RootElement.GetProperty("country").GetString());
        Assert.Equal("contratista", body.RootElement.GetProperty("notes").GetString());
        Assert.Equal("Halliburton", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void Paths_and_created_ids_follow_the_shell_api()
    {
        var id = Guid.NewGuid();

        Assert.Equal("api/catalogs/wells", ShellPayloads.PathOf("wells", null));
        Assert.Equal($"api/catalogs/facilities/{id}", ShellPayloads.PathOf("facilities", id));
        Assert.Equal(id, ShellPayloads.CreatedId($$$"""{"id":"{{{id}}}"}"""));
        Assert.Null(ShellPayloads.CreatedId(string.Empty));
    }

    [Fact]
    public void Master_json_is_parsed_with_the_parent_of_each_catalog()
    {
        using var well = JsonDocument.Parse($$$"""{"code":"KU-61","name":"Ku 61","fieldId":"{{{Field}}}","isActive":false,"updatedAt":"2026-11-06T18:20:11Z"}""");
        using var rig = JsonDocument.Parse($$$"""{"code":"R-1","name":"Equipo 1","contractorId":"{{{Operator}}}"}""");

        var parsedWell = MasterJson.Parse("wells", null, null, well.RootElement);
        var parsedRig = MasterJson.Parse("rigs", null, null, rig.RootElement);

        Assert.Equal(Field, parsedWell.ParentId);
        Assert.False(parsedWell.IsActive);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 18, 20, 11, TimeSpan.Zero), parsedWell.UpdatedAt);
        Assert.Equal(Operator, parsedRig.ParentId);
        Assert.True(parsedRig.IsActive);
    }
}
