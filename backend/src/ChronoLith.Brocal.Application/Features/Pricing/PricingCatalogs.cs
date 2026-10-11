using System.Linq.Expressions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Pricing;

// ---- Services (conceptos) ----------------------------------------------------------------------------

public sealed record ServiceDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string? Category,
    string UnitName,
    string SatProductCode,
    string SatUnitCode,
    string TaxObject,
    decimal VatRate,
    decimal? BasePrice,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal sealed class ServiceDataValidator : AbstractValidator<ServiceData>
{
    public ServiceDataValidator()
    {
        RuleFor(c => c.Code).BusinessCode();
        RuleFor(c => c.Name).RequiredName();
        RuleFor(c => c.UnitName).NotEmpty().MaximumLength(30);
        RuleFor(c => c.SatProductCode).NotEmpty().Matches("^\\d{8}$").WithMessage("La clave SAT de producto o servicio tiene 8 dígitos.");
        RuleFor(c => c.SatUnitCode).NotEmpty().MaximumLength(3);
        RuleFor(c => c.BasePrice).GreaterThanOrEqualTo(0).When(c => c.BasePrice is not null);
    }
}

internal sealed class ServiceSpec(IApplicationDbContext db) : ICatalogSpec<Service, ServiceData, ServiceDto>
{
    public string Name => ServiceErrors.Catalog;

    public Expression<Func<Service, ServiceDto>> Projection => s => new ServiceDto(
        s.Id, s.Code, s.Name, s.Description, s.Category, s.UnitName, s.SatProductCode, s.SatUnitCode, s.TaxObject, s.VatRate, s.BasePrice,
        s.Notes, s.IsActive, s.CreatedAt, s.UpdatedAt);

    public Expression<Func<Service, string?>>? LookupExtra => s => s.UnitName;

    public string CodeOf(ServiceData data) => data.Code;

    public Result<Service> Create(ServiceData data) => Service.Create(data);

    public Result Update(Service entity, ServiceData data) => entity.Update(data);

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<Domain.Operations.TicketLine>().AnyAsync(l => l.ServiceId == id, cancellationToken)
        || await db.Set<PriceListItem>().AnyAsync(i => i.ServiceId == id, cancellationToken)
        || await db.Set<QuoteLine>().AnyAsync(l => l.ServiceId == id, cancellationToken);

    public IQueryable<Service> Filter(IQueryable<Service> query, IReadOnlyDictionary<string, string?> filters) =>
        filters.TryGetValue("category", out var category) && !string.IsNullOrWhiteSpace(category)
            ? query.Where(s => s.Category == category)
            : query;
}

// ---- Price lists ----------------------------------------------------------------------------------------

public sealed record PriceListItemDto(Guid ServiceId, string? ServiceCode, string? ServiceName, string? UnitName, decimal UnitPrice, string? Notes);

public sealed record PriceListDto(
    Guid Id,
    string Code,
    string Name,
    Guid? CustomerId,
    string? CustomerName,
    Guid? ContractId,
    string? ContractName,
    string Currency,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int ItemCount,
    IReadOnlyList<PriceListItemDto> Items,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal sealed class PriceListDataValidator : AbstractValidator<PriceListData>
{
    public PriceListDataValidator()
    {
        RuleFor(c => c.Code).BusinessCode();
        RuleFor(c => c.Name).RequiredName();
        RuleFor(c => c.ValidTo).GreaterThanOrEqualTo(c => c.ValidFrom).When(c => c.ValidTo is not null)
            .WithMessage("La vigencia final no puede ser anterior a la inicial.");
        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ServiceId).NotEmpty();
            item.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

internal sealed class PriceListSpec(IApplicationDbContext db) : ICatalogSpec<PriceList, PriceListData, PriceListDto>
{
    public string Name => PricingErrors.Catalog;

    public Expression<Func<PriceList, PriceListDto>> Projection => p => new PriceListDto(
        p.Id, p.Code, p.Name, p.CustomerId,
        db.Customers.Where(x => x.Id == p.CustomerId).Select(x => x.Name).FirstOrDefault(),
        p.ContractId,
        db.Contracts.Where(x => x.Id == p.ContractId).Select(x => x.Name).FirstOrDefault(),
        p.Currency, p.ValidFrom, p.ValidTo, p.Items.Count,
        p.Items.Select(i => new PriceListItemDto(
            i.ServiceId,
            db.Services.Where(s => s.Id == i.ServiceId).Select(s => s.Code).FirstOrDefault(),
            db.Services.Where(s => s.Id == i.ServiceId).Select(s => s.Name).FirstOrDefault(),
            db.Services.Where(s => s.Id == i.ServiceId).Select(s => s.UnitName).FirstOrDefault(),
            i.UnitPrice,
            i.Notes)).ToList(),
        p.Notes, p.IsActive, p.CreatedAt, p.UpdatedAt);

    public Expression<Func<PriceList, Guid?>>? LookupParent => p => p.CustomerId;

    public string CodeOf(PriceListData data) => data.Code;

    public Result<PriceList> Create(PriceListData data) => PriceList.Create(data);

    public Result Update(PriceList entity, PriceListData data) => entity.Update(data);

    public async Task<Result> ValidateAsync(PriceListData data, PriceList? existing, CancellationToken cancellationToken)
    {
        var customer = await References.EnsureAsync<Customer>(db, data.CustomerId, CustomerErrors.Catalog, cancellationToken);
        if (customer.IsFailure)
        {
            return customer;
        }

        if (data.ContractId is not null)
        {
            var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == data.ContractId, cancellationToken);
            if (contract is null)
            {
                return Domain.Common.CatalogErrors.ReferenceNotFound(ContractErrors.Catalog);
            }

            if (contract.CustomerId != data.CustomerId)
            {
                return ContractErrors.CustomerMismatch;
            }
        }

        return await References.EnsureAllAsync<Service>(db, data.Items?.Select(i => i.ServiceId), ServiceErrors.Catalog, cancellationToken);
    }

    public Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);

    public IQueryable<PriceList> Filter(IQueryable<PriceList> query, IReadOnlyDictionary<string, string?> filters)
    {
        if (filters.TryGetValue("customerId", out var customer) && Guid.TryParse(customer, out var customerId))
        {
            query = query.Where(p => p.CustomerId == customerId);
        }

        if (filters.TryGetValue("scope", out var scope) && scope == "general")
        {
            query = query.Where(p => p.CustomerId == null);
        }

        return query;
    }
}

// ---- Service types (forms) ------------------------------------------------------------------------------

public sealed record ServiceTypeDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Icon,
    string? Color,
    bool RequiresSignature,
    int MinPhotos,
    IReadOnlyList<FormFieldDefinition> Fields,
    IReadOnlyList<Guid> DefaultServiceIds,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal sealed class ServiceTypeDataValidator : AbstractValidator<ServiceTypeData>
{
    public ServiceTypeDataValidator()
    {
        RuleFor(c => c.Code).BusinessCode();
        RuleFor(c => c.Name).RequiredName();
        RuleFor(c => c.MinPhotos).InclusiveBetween(0, 30);
        RuleFor(c => c.Color).Color();
    }
}

internal sealed class ServiceTypeSpec(IApplicationDbContext db) : ICatalogSpec<ServiceType, ServiceTypeData, ServiceTypeDto>
{
    public string Name => ServiceTypeErrors.Catalog;

    public Expression<Func<ServiceType, ServiceTypeDto>> Projection => t => new ServiceTypeDto(
        t.Id, t.Code, t.Name, t.Description, t.Icon, t.Color, t.RequiresSignature, t.MinPhotos, t.Fields, t.DefaultServiceIds, t.Notes, t.IsActive, t.CreatedAt, t.UpdatedAt);

    public Expression<Func<ServiceType, string?>>? LookupExtra => t => t.Color;

    public string CodeOf(ServiceTypeData data) => data.Code;

    public Result<ServiceType> Create(ServiceTypeData data) => ServiceType.Create(data);

    public Result Update(ServiceType entity, ServiceTypeData data) => entity.Update(data);

    public Task<Result> ValidateAsync(ServiceTypeData data, ServiceType? existing, CancellationToken cancellationToken) =>
        References.EnsureAllAsync<Service>(db, data.DefaultServiceIds, ServiceErrors.Catalog, cancellationToken);

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Tickets.AnyAsync(t => t.ServiceTypeId == id, cancellationToken)
        || await db.WorkOrders.AnyAsync(w => w.ServiceTypeId == id, cancellationToken);
}

// ---- Price resolution -----------------------------------------------------------------------------------

public sealed record ResolvedPriceDto(Guid ServiceId, string Code, string Name, string UnitName, decimal VatRate, decimal? UnitPrice, PriceSource Source, Guid? PriceListId, string? PriceListName);

/// <summary>Prices that apply to a customer/contract on a date (used by the ticket capture and the quotes).</summary>
public sealed record ResolvePricesQuery(Guid? CustomerId, Guid? ContractId, DateOnly? Date, IReadOnlyList<Guid>? ServiceIds) : IQuery<IReadOnlyList<ResolvedPriceDto>>;

internal sealed class ResolvePricesQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<ResolvePricesQuery, IReadOnlyList<ResolvedPriceDto>>
{
    public async Task<Result<IReadOnlyList<ResolvedPriceDto>>> Handle(ResolvePricesQuery query, CancellationToken cancellationToken)
    {
        var resolver = await PriceBook.LoadAsync(db, cancellationToken);
        var date = query.Date ?? clock.Today;
        var services = db.Services.AsNoTracking().Where(s => s.IsActive);
        if (query.ServiceIds is { Count: > 0 } ids)
        {
            services = db.Services.AsNoTracking().Where(s => ids.Contains(s.Id));
        }

        var list = await services.OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return list.Select(s =>
        {
            var price = resolver.Resolve(query.CustomerId, query.ContractId, s.Id, date, s.BasePrice);
            return new ResolvedPriceDto(s.Id, s.Code, s.Name, s.UnitName, s.VatRate, price.UnitPrice, price.Source, price.PriceListId, price.PriceListName);
        }).ToList();
    }
}

/// <summary>Price lists of the company loaded once to resolve several lines.</summary>
internal sealed class PriceBook(IReadOnlyList<PriceList> lists)
{
    public static async Task<PriceBook> LoadAsync(IApplicationDbContext db, CancellationToken cancellationToken) =>
        new(await db.PriceLists.AsNoTracking().Include(p => p.Items).Where(p => p.IsActive).ToListAsync(cancellationToken));

    public ResolvedPrice Resolve(Guid? customerId, Guid? contractId, Guid serviceId, DateOnly date, decimal? basePrice) =>
        PriceResolver.Resolve(lists, customerId, contractId, serviceId, date, basePrice);
}
