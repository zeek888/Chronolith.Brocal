using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Catalogs;
using ChronoLith.Brocal.Application.Features.Pricing;
using ChronoLith.Brocal.Application.Features.SharedCatalogs;
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

        // Shared catalogs of ChronoLith (local replica fed by the change feed), to link sites and customers.
        var shell = app.MapGroup("catalogs/shell").WithTags("Catálogos de ChronoLith");

        shell.MapGet("status", async (IQueryHandler<GetSharedCatalogAvailabilityQuery, SharedCatalogAvailabilityDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetSharedCatalogAvailabilityQuery(), ct)).ToOk())
            .WithSummary("Si Brocal está conectado a un Shell de ChronoLith (réplica de catálogos disponible).")
            .RequireTenant();

        shell.MapGet("{catalog}/lookup", async (string catalog, bool? activeOnly, Guid? parentId,
                IQueryHandler<GetSharedCatalogLookupQuery, IReadOnlyList<LookupDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetSharedCatalogLookupQuery(catalog, activeOnly ?? true, parentId), ct)).ToOk())
            .WithSummary("Pozos, instalaciones, campos, compañías… del catálogo compartido, desde la réplica local (misma forma que los demás lookups).")
            .RequireTenant();

        app.MapPost("catalogs/sites/{id:guid}/shell", async (Guid id, ICommandHandler<PublishSiteToShellCommand, CatalogPushResultDto> handler, CancellationToken ct) =>
                (await handler.Handle(new PublishSiteToShellCommand(id), ct)).ToOk())
            .WithTags("Locaciones")
            .WithSummary("Crea la locación en el catálogo maestro de ChronoLith (o la enlaza si su clave ya existe) y envía sus datos compartidos.")
            .RequirePermission(Permissions.CatalogsManage);

        app.MapPost("catalogs/customers/{id:guid}/shell", async (Guid id, ICommandHandler<PublishCustomerToShellCommand, CatalogPushResultDto> handler, CancellationToken ct) =>
                (await handler.Handle(new PublishCustomerToShellCommand(id), ct)).ToOk())
            .WithTags("Clientes")
            .WithSummary("Crea el cliente como compañía del catálogo maestro de ChronoLith (o lo enlaza si su clave ya existe).")
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
