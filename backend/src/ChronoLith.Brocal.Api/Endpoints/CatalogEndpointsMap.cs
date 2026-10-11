using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Features.Catalogs;
using ChronoLith.Brocal.Application.Features.Pricing;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>Customers, contracts and sites (core master data of every company).</summary>
internal sealed class CommercialCatalogEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        string[] read = [Permissions.CatalogsRead, Permissions.CatalogsManage];

        app.MapCatalog<Customer, CustomerData, CustomerDto>("catalogs/customers", "Clientes", read, Permissions.CatalogsManage);
        app.MapCatalog<Contract, ContractData, ContractDto>("catalogs/contracts", "Contratos", read, Permissions.CatalogsManage);
        app.MapCatalog<Site, SiteData, SiteDto>("catalogs/sites", "Locaciones", read, Permissions.CatalogsManage);

        // Module mode: wells, fields and companies of the ChronoLith shared catalog, to link sites and customers.
        app.MapGet("catalogs/shell/{catalog}", async (string catalog, IShellCatalogClient shell, CancellationToken ct) =>
                Results.Ok(new { available = shell.IsAvailable, items = await shell.LookupAsync(catalog, ct) }))
            .WithTags("Locaciones")
            .RequirePermission(Permissions.CatalogsManage);
    }
}

/// <summary>Services (SAT codes), price lists and service types (capture forms).</summary>
internal sealed class PricingEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        string[] read = [Permissions.PricingRead, Permissions.PricingManage];

        app.MapCatalog<Service, ServiceData, ServiceDto>("pricing/services", "Servicios", read, Permissions.PricingManage);
        app.MapCatalog<PriceList, PriceListData, PriceListDto>("pricing/price-lists", "Tarifarios", read, Permissions.PricingManage);
        app.MapCatalog<ServiceType, ServiceTypeData, ServiceTypeDto>("pricing/service-types", "Tipos de servicio",
            // The portal reads the capture form (field labels) to show the answers of its tickets; no prices here.
            [Permissions.PricingRead, Permissions.PricingManage, Permissions.TicketsCapture, Permissions.TicketsRead, Permissions.TicketsReadOwn, Permissions.DispatchManage, Permissions.PortalAccess],
            Permissions.PricingManage);

        app.MapGet("pricing/resolve", async (Guid? customerId, Guid? contractId, DateOnly? date, Guid[]? serviceIds,
                IQueryHandler<ResolvePricesQuery, IReadOnlyList<ResolvedPriceDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ResolvePricesQuery(customerId, contractId, date, serviceIds), ct)).ToOk())
            .WithTags("Tarifarios")
            .WithSummary("Precio que aplica a cada servicio para un cliente/contrato en una fecha (contrato → cliente → general → precio base).")
            .RequirePermission(Permissions.PricingRead, Permissions.TicketsCapture, Permissions.QuotesManage, Permissions.DispatchManage);
    }
}
