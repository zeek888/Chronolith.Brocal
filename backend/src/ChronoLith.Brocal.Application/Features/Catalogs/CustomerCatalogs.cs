using System.Linq.Expressions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Catalogs;

// ---- Customers ------------------------------------------------------------------------------------------

public sealed record CustomerDto(
    Guid Id,
    string Code,
    string Name,
    string LegalName,
    string Rfc,
    string? TaxRegime,
    string? PostalCode,
    string CfdiUse,
    string PaymentForm,
    string PaymentMethod,
    int CreditDays,
    string? Email,
    string? Phone,
    string? Address,
    string? Color,
    Guid? ShellCompanyId,
    bool RequiresCopade,
    IReadOnlyList<CustomerContact> Contacts,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal sealed class CustomerDataValidator : AbstractValidator<CustomerData>
{
    public CustomerDataValidator()
    {
        RuleFor(c => c.Code).BusinessCode();
        RuleFor(c => c.Name).RequiredName();
        RuleFor(c => c.Rfc).NotEmpty().WithMessage("El RFC es obligatorio.").Rfc();
        RuleFor(c => c.PostalCode).PostalCode();
        RuleFor(c => c.Email).Email().When(c => !string.IsNullOrWhiteSpace(c.Email));
        RuleFor(c => c.Color).Color();
        RuleFor(c => c.CreditDays).InclusiveBetween(0, 365);
        RuleFor(c => c.PaymentMethod).Must(m => m is "PUE" or "PPD").WithMessage("El método de pago es PUE o PPD.");
    }
}

internal sealed class CustomerSpec(IApplicationDbContext db) : ICatalogSpec<Customer, CustomerData, CustomerDto>
{
    public string Name => CustomerErrors.Catalog;

    public Expression<Func<Customer, CustomerDto>> Projection => c => new CustomerDto(
        c.Id, c.Code, c.Name, c.LegalName, c.Rfc, c.TaxRegime, c.PostalCode, c.CfdiUse, c.PaymentForm, c.PaymentMethod, c.CreditDays,
        c.Email, c.Phone, c.Address, c.Color, c.ShellCompanyId, c.RequiresCopade, c.Contacts, c.Notes, c.IsActive, c.CreatedAt, c.UpdatedAt);

    public Expression<Func<Customer, string?>>? LookupExtra => c => c.Rfc;

    public string CodeOf(CustomerData data) => data.Code;

    public Result<Customer> Create(CustomerData data) => Customer.Create(data);

    public Result Update(Customer entity, CustomerData data) => entity.Update(data);

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Tickets.AnyAsync(t => t.CustomerId == id, cancellationToken)
        || await db.WorkOrders.AnyAsync(w => w.CustomerId == id, cancellationToken)
        || await db.Contracts.AnyAsync(c => c.CustomerId == id, cancellationToken)
        || await db.Invoices.AnyAsync(i => i.CustomerId == id, cancellationToken)
        || await db.Quotes.AnyAsync(q => q.CustomerId == id, cancellationToken)
        || await db.Memberships.AnyAsync(m => m.CustomerId == id, cancellationToken);
}

// ---- Contracts ------------------------------------------------------------------------------------------

public sealed record ContractDto(
    Guid Id,
    string Code,
    string Name,
    Guid CustomerId,
    string? CustomerName,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? CeilingAmount,
    string Currency,
    ContractStatus Status,
    string? CustomerRepresentative,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal sealed class ContractDataValidator : AbstractValidator<ContractData>
{
    public ContractDataValidator()
    {
        RuleFor(c => c.Code).BusinessCode();
        RuleFor(c => c.Name).RequiredName();
        RuleFor(c => c.CustomerId).RequiredId("el cliente");
        RuleFor(c => c.EndDate).GreaterThanOrEqualTo(c => c.StartDate).When(c => c.EndDate is not null)
            .WithMessage("La fecha de término no puede ser anterior al inicio.");
    }
}

internal sealed class ContractSpec(IApplicationDbContext db) : ICatalogSpec<Contract, ContractData, ContractDto>
{
    public string Name => ContractErrors.Catalog;

    public Expression<Func<Contract, ContractDto>> Projection => c => new ContractDto(
        c.Id, c.Code, c.Name, c.CustomerId,
        db.Customers.Where(x => x.Id == c.CustomerId).Select(x => x.Name).FirstOrDefault(),
        c.StartDate, c.EndDate, c.CeilingAmount, c.Currency, c.Status, c.CustomerRepresentative, c.Notes, c.IsActive, c.CreatedAt, c.UpdatedAt);

    public Expression<Func<Contract, Guid?>>? LookupParent => c => c.CustomerId;

    public string CodeOf(ContractData data) => data.Code;

    public Result<Contract> Create(ContractData data) => Contract.Create(data);

    public Result Update(Contract entity, ContractData data) => entity.Update(data);

    public Task<Result> ValidateAsync(ContractData data, Contract? existing, CancellationToken cancellationToken) =>
        References.EnsureAsync<Customer>(db, data.CustomerId, CustomerErrors.Catalog, cancellationToken);

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Tickets.AnyAsync(t => t.ContractId == id, cancellationToken)
        || await db.WorkOrders.AnyAsync(w => w.ContractId == id, cancellationToken)
        || await db.PriceLists.AnyAsync(p => p.ContractId == id, cancellationToken)
        || await db.Estimates.AnyAsync(e => e.ContractId == id, cancellationToken);

    public IQueryable<Contract> Filter(IQueryable<Contract> query, IReadOnlyDictionary<string, string?> filters)
    {
        if (filters.TryGetValue("customerId", out var customer) && Guid.TryParse(customer, out var customerId))
        {
            query = query.Where(c => c.CustomerId == customerId);
        }

        if (filters.TryGetValue("status", out var status) && Enum.TryParse<ContractStatus>(status, true, out var value))
        {
            query = query.Where(c => c.Status == value);
        }

        return query;
    }
}

// ---- Sites ----------------------------------------------------------------------------------------------

public sealed record SiteDto(
    Guid Id,
    string Code,
    string Name,
    SiteType Type,
    Guid? CustomerId,
    string? CustomerName,
    string? FieldName,
    string? Municipality,
    string? State,
    string? PostalCode,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ShellCatalogId,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal sealed class SiteDataValidator : AbstractValidator<SiteData>
{
    public SiteDataValidator()
    {
        RuleFor(c => c.Code).BusinessCode();
        RuleFor(c => c.Name).RequiredName();
        RuleFor(c => c.PostalCode).PostalCode();
        RuleFor(c => c.Latitude).InclusiveBetween(-90, 90).When(c => c.Latitude is not null);
        RuleFor(c => c.Longitude).InclusiveBetween(-180, 180).When(c => c.Longitude is not null);
    }
}

internal sealed class SiteSpec(IApplicationDbContext db) : ICatalogSpec<Site, SiteData, SiteDto>
{
    public string Name => SiteErrors.Catalog;

    public Expression<Func<Site, SiteDto>> Projection => s => new SiteDto(
        s.Id, s.Code, s.Name, s.Type, s.CustomerId,
        db.Customers.Where(x => x.Id == s.CustomerId).Select(x => x.Name).FirstOrDefault(),
        s.FieldName, s.Municipality, s.State, s.PostalCode, s.Latitude, s.Longitude, s.ShellCatalogId, s.Notes, s.IsActive, s.CreatedAt, s.UpdatedAt);

    public Expression<Func<Site, Guid?>>? LookupParent => s => s.CustomerId;

    public Expression<Func<Site, string?>>? LookupExtra => s => s.FieldName;

    public string CodeOf(SiteData data) => data.Code;

    public Result<Site> Create(SiteData data) => Site.Create(data);

    public Result Update(Site entity, SiteData data) => entity.Update(data);

    public Task<Result> ValidateAsync(SiteData data, Site? existing, CancellationToken cancellationToken) =>
        References.EnsureAsync<Customer>(db, data.CustomerId, CustomerErrors.Catalog, cancellationToken);

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Tickets.AnyAsync(t => t.SiteId == id, cancellationToken)
        || await db.WorkOrders.AnyAsync(w => w.SiteId == id, cancellationToken)
        || await db.Incidents.AnyAsync(i => i.SiteId == id, cancellationToken);

    public IQueryable<Site> Filter(IQueryable<Site> query, IReadOnlyDictionary<string, string?> filters)
    {
        if (filters.TryGetValue("customerId", out var customer) && Guid.TryParse(customer, out var customerId))
        {
            query = query.Where(s => s.CustomerId == customerId || s.CustomerId == null);
        }

        if (filters.TryGetValue("type", out var type) && Enum.TryParse<SiteType>(type, true, out var value))
        {
            query = query.Where(s => s.Type == value);
        }

        return query;
    }
}
