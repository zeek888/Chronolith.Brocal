using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Tickets;

public sealed record TicketSummaryDto(
    Guid Id,
    string Folio,
    TicketStatus Status,
    DateOnly ServiceDate,
    Guid CustomerId,
    string? CustomerName,
    Guid SiteId,
    string? SiteName,
    Guid ServiceTypeId,
    string? ServiceTypeName,
    string? ServiceTypeColor,
    Guid? WorkOrderId,
    string? WorkOrderFolio,
    decimal Total,
    string Currency,
    string? CapturedByName,
    DateTimeOffset? SignedAt,
    bool HasSignature,
    int PhotoCount,
    Guid? InvoiceId,
    DateTimeOffset CreatedAt);

public sealed record TicketLineDto(Guid Id, int Order, Guid ServiceId, string? ServiceCode, string Description, decimal Quantity, string UnitName, decimal UnitPrice, PriceSource PriceSource, decimal VatRate, decimal Amount);

public sealed record TicketPhotoDto(Guid Id, Guid FileId, string Url, string? Caption, DateTimeOffset TakenAt, decimal? Latitude, decimal? Longitude);

public sealed record NamedRef(Guid Id, string Code, string Name);

public sealed record TicketDetailDto(
    Guid Id,
    string Folio,
    Guid? ClientId,
    TicketStatus Status,
    Guid CustomerId,
    string? CustomerName,
    Guid? ContractId,
    string? ContractName,
    Guid SiteId,
    string? SiteName,
    Guid ServiceTypeId,
    string? ServiceTypeName,
    Guid? WorkOrderId,
    string? WorkOrderFolio,
    DateOnly ServiceDate,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<NamedRef> Units,
    Guid? CrewId,
    string? CrewName,
    IReadOnlyList<NamedRef> Employees,
    string? Description,
    string? Observations,
    string? CustomerRepresentative,
    IReadOnlyDictionary<string, string?> Answers,
    decimal? CaptureLatitude,
    decimal? CaptureLongitude,
    IReadOnlyList<TicketLineDto> Lines,
    IReadOnlyList<TicketPhotoDto> Photos,
    string Currency,
    decimal Subtotal,
    decimal Vat,
    decimal Total,
    string? CapturedByName,
    string? SignerName,
    string? SignerPosition,
    string? SignatureUrl,
    DateTimeOffset? SignedAt,
    DateTimeOffset? ApprovedAt,
    string? ApprovedBy,
    Guid? InvoiceId,
    string? InvoiceReference,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    string? PdfUrl,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

public sealed record SearchTicketsQuery(
    string? Search,
    IReadOnlyList<TicketStatus>? Statuses,
    Guid? CustomerId,
    Guid? SiteId,
    Guid? ServiceTypeId,
    Guid? WorkOrderId,
    Guid? UnitId,
    DateOnly? From,
    DateOnly? To,
    bool? Uninvoiced,
    int Page = 1,
    int PageSize = 25,
    string? Sort = null,
    bool Descending = true) : IQuery<PagedResult<TicketSummaryDto>>;

/// <summary>Tickets the user can see: everything with <c>tickets.read</c>, otherwise only the ones they took part in.</summary>
internal static class TicketVisibility
{
    public static IQueryable<ServiceTicket> Visible(this IQueryable<ServiceTicket> tickets, ITenantContext tenant)
    {
        if (tenant.Has(Permissions.TicketsRead))
        {
            return tenant.Membership?.CustomerId is { } portalCustomer ? tickets.Where(t => t.CustomerId == portalCustomer) : tickets;
        }

        if (tenant.Has(Permissions.PortalAccess) && tenant.Membership?.CustomerId is { } customerId)
        {
            return tickets.Where(t => t.CustomerId == customerId && t.Status != TicketStatus.Draft && t.Status != TicketStatus.Cancelled);
        }

        var membershipId = tenant.Membership?.Id;
        var employeeId = tenant.Membership?.EmployeeId;
        return tickets.Where(t => t.CapturedById == membershipId || employeeId != null && t.EmployeeIds.Contains(employeeId.Value));
    }
}

internal sealed class SearchTicketsQueryHandler(IApplicationDbContext db, ITenantContext tenant) : IQueryHandler<SearchTicketsQuery, PagedResult<TicketSummaryDto>>
{
    public async Task<Result<PagedResult<TicketSummaryDto>>> Handle(SearchTicketsQuery query, CancellationToken cancellationToken)
    {
        var request = new PageRequest(query.Search, query.Page, query.PageSize, query.Sort, query.Descending);
        var tickets = db.Tickets.AsNoTracking().Visible(tenant);

        if (request.Term is { } term)
        {
            tickets = tickets.Where(t =>
                t.Folio.ToLower().Contains(term) ||
                (t.Description != null && t.Description.ToLower().Contains(term)) ||
                db.Customers.Any(c => c.Id == t.CustomerId && c.Name.ToLower().Contains(term)) ||
                db.Sites.Any(s => s.Id == t.SiteId && (s.Name.ToLower().Contains(term) || s.Code.ToLower().Contains(term))));
        }

        if (query.Statuses is { Count: > 0 } statuses)
        {
            tickets = tickets.Where(t => statuses.Contains(t.Status));
        }

        tickets = tickets
            .WhereIf(query.CustomerId is not null, t => t.CustomerId == query.CustomerId)
            .WhereIf(query.SiteId is not null, t => t.SiteId == query.SiteId)
            .WhereIf(query.ServiceTypeId is not null, t => t.ServiceTypeId == query.ServiceTypeId)
            .WhereIf(query.WorkOrderId is not null, t => t.WorkOrderId == query.WorkOrderId)
            .WhereIf(query.UnitId is not null, t => t.UnitIds.Contains(query.UnitId!.Value))
            .WhereIf(query.From is not null, t => t.ServiceDate >= query.From)
            .WhereIf(query.To is not null, t => t.ServiceDate <= query.To)
            .WhereIf(query.Uninvoiced == true, t => t.Status == TicketStatus.Signed || t.Status == TicketStatus.Approved);

        tickets = request.Sort?.ToLowerInvariant() switch
        {
            "total" => request.Descending ? tickets.OrderByDescending(t => t.Total) : tickets.OrderBy(t => t.Total),
            "folio" => request.Descending ? tickets.OrderByDescending(t => t.FolioNumber) : tickets.OrderBy(t => t.FolioNumber),
            "status" => request.Descending ? tickets.OrderByDescending(t => t.Status) : tickets.OrderBy(t => t.Status),
            _ => request.Descending ? tickets.OrderByDescending(t => t.ServiceDate).ThenByDescending(t => t.FolioNumber) : tickets.OrderBy(t => t.ServiceDate).ThenBy(t => t.FolioNumber)
        };

        return await tickets.Select(t => new TicketSummaryDto(
            t.Id,
            t.Folio,
            t.Status,
            t.ServiceDate,
            t.CustomerId,
            db.Customers.Where(c => c.Id == t.CustomerId).Select(c => c.Name).FirstOrDefault(),
            t.SiteId,
            db.Sites.Where(s => s.Id == t.SiteId).Select(s => s.Name).FirstOrDefault(),
            t.ServiceTypeId,
            db.ServiceTypes.Where(s => s.Id == t.ServiceTypeId).Select(s => s.Name).FirstOrDefault(),
            db.ServiceTypes.Where(s => s.Id == t.ServiceTypeId).Select(s => s.Color).FirstOrDefault(),
            t.WorkOrderId,
            db.WorkOrders.Where(w => w.Id == t.WorkOrderId).Select(w => w.Folio).FirstOrDefault(),
            t.Total,
            t.Currency,
            t.CapturedByName,
            t.SignedAt,
            t.SignatureFileId != null,
            t.Photos.Count,
            t.InvoiceId,
            t.CreatedAt)).ToPagedAsync(request, cancellationToken);
    }
}

public sealed record GetTicketQuery(Guid Id) : IQuery<TicketDetailDto>;

internal sealed class GetTicketQueryHandler(IApplicationDbContext db, ITenantContext tenant, IFileService files) : IQueryHandler<GetTicketQuery, TicketDetailDto>
{
    public async Task<Result<TicketDetailDto>> Handle(GetTicketQuery query, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.AsNoTracking().Visible(tenant)
            .Include(t => t.Lines)
            .Include(t => t.Photos)
            .FirstOrDefaultAsync(t => t.Id == query.Id, cancellationToken);

        return ticket is null ? TicketErrors.NotFound : await TicketDetails.BuildAsync(db, files, ticket, cancellationToken);
    }
}

internal static class TicketDetails
{
    public static async Task<TicketDetailDto> BuildAsync(IApplicationDbContext db, IFileService files, ServiceTicket ticket, CancellationToken cancellationToken)
    {
        var customers = await CatalogNames.LoadAsync<Customer>(db, [ticket.CustomerId], cancellationToken);
        var contracts = await CatalogNames.LoadAsync<Contract>(db, [ticket.ContractId], cancellationToken);
        var sites = await CatalogNames.LoadAsync<Site>(db, [ticket.SiteId], cancellationToken);
        var types = await CatalogNames.LoadAsync<ServiceType>(db, [ticket.ServiceTypeId], cancellationToken);
        var units = await CatalogNames.LoadAsync<Unit>(db, ticket.UnitIds, cancellationToken);
        var employees = await CatalogNames.LoadAsync<Employee>(db, ticket.EmployeeIds, cancellationToken);
        var crews = await CatalogNames.LoadAsync<Crew>(db, [ticket.CrewId], cancellationToken);
        var services = await CatalogNames.LoadAsync<Service>(db, ticket.Lines.Select(l => l.ServiceId), cancellationToken);
        var workOrder = ticket.WorkOrderId is null
            ? null
            : await db.WorkOrders.AsNoTracking().Where(w => w.Id == ticket.WorkOrderId).Select(w => w.Folio).FirstOrDefaultAsync(cancellationToken);
        var invoice = ticket.InvoiceId is null
            ? null
            : await db.Invoices.AsNoTracking().Where(i => i.Id == ticket.InvoiceId).Select(i => new { i.Series, i.Folio, i.Uuid }).FirstOrDefaultAsync(cancellationToken);

        return new TicketDetailDto(
            ticket.Id,
            ticket.Folio,
            ticket.ClientId,
            ticket.Status,
            ticket.CustomerId,
            customers.NameOf(ticket.CustomerId),
            ticket.ContractId,
            contracts.NameOf(ticket.ContractId),
            ticket.SiteId,
            sites.NameOf(ticket.SiteId),
            ticket.ServiceTypeId,
            types.NameOf(ticket.ServiceTypeId),
            ticket.WorkOrderId,
            workOrder,
            ticket.ServiceDate,
            ticket.StartedAt,
            ticket.FinishedAt,
            ticket.UnitIds.Where(units.ContainsKey).Select(id => new NamedRef(id, units[id].Code, units[id].Name)).ToList(),
            ticket.CrewId,
            crews.NameOf(ticket.CrewId),
            ticket.EmployeeIds.Where(employees.ContainsKey).Select(id => new NamedRef(id, employees[id].Code, employees[id].Name)).ToList(),
            ticket.Description,
            ticket.Observations,
            ticket.CustomerRepresentative,
            ticket.Answers,
            ticket.CaptureLatitude,
            ticket.CaptureLongitude,
            ticket.Lines.OrderBy(l => l.Order)
                .Select(l => new TicketLineDto(l.Id, l.Order, l.ServiceId, services.CodeOf(l.ServiceId), l.Description, l.Quantity, l.UnitName, l.UnitPrice, l.PriceSource, l.VatRate, l.Amount))
                .ToList(),
            ticket.Photos.OrderBy(p => p.Order)
                .Select(p => new TicketPhotoDto(p.Id, p.FileId, files.GetUrl(p.FileId), p.Caption, p.TakenAt, p.Latitude, p.Longitude))
                .ToList(),
            ticket.Currency,
            ticket.Subtotal,
            ticket.Vat,
            ticket.Total,
            ticket.CapturedByName,
            ticket.SignerName,
            ticket.SignerPosition,
            ticket.SignatureFileId is { } signature ? files.GetUrl(signature) : null,
            ticket.SignedAt,
            ticket.ApprovedAt,
            ticket.ApprovedBy,
            ticket.InvoiceId,
            invoice is null ? null : invoice.Folio is null ? $"{invoice.Series} (borrador)" : $"{invoice.Series}-{invoice.Folio}",
            ticket.CancelledAt,
            ticket.CancellationReason,
            ticket.PdfFileId is { } pdf ? files.GetUrl(pdf) : null,
            ticket.CreatedAt,
            ticket.CreatedBy,
            ticket.UpdatedAt,
            ticket.UpdatedBy);
    }
}
