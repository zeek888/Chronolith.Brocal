using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Management;

// ---- Billing board (module 4) ---------------------------------------------------------------------------

public sealed record BillingMonthDto(int Year, int Month, decimal Executed, decimal Invoiced, decimal Collected, int Tickets);

public sealed record BillingCustomerDto(Guid CustomerId, string CustomerName, decimal Executed, decimal Uninvoiced, decimal Invoiced, decimal Collected, decimal Receivable);

public sealed record UninvoicedTicketDto(Guid Id, string Folio, DateOnly ServiceDate, string? CustomerName, TicketStatus Status, decimal Total, int DaysWaiting, bool Alert);

public sealed record BillingBoardDto(
    decimal Executed, decimal Uninvoiced, decimal Invoiced, decimal Collected, decimal Receivable, int TicketsWithoutInvoice, int AlertTickets, int AlertDays,
    IReadOnlyList<BillingMonthDto> Months, IReadOnlyList<BillingCustomerDto> Customers, IReadOnlyList<UninvoicedTicketDto> Pending);

/// <summary>
/// Servicios ejecutados contra facturados contra cobrados (amounts before VAT for executed/invoiced, collected with VAT)
/// with the tickets that wait too long for their invoice.
/// </summary>
public sealed record GetBillingBoardQuery(DateOnly From, DateOnly To, Guid? CustomerId) : IQuery<BillingBoardDto>;

internal sealed class GetBillingBoardQueryHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock) : IQueryHandler<GetBillingBoardQuery, BillingBoardDto>
{
    public async Task<Result<BillingBoardDto>> Handle(GetBillingBoardQuery query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var alertDays = tenant.Tenant.Settings.UninvoicedAlertDays;
        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => t.ServiceDate >= query.From && t.ServiceDate <= query.To && t.Status != TicketStatus.Draft && t.Status != TicketStatus.Cancelled)
            .WhereIf(query.CustomerId is not null, t => t.CustomerId == query.CustomerId)
            .Select(t => new { t.Id, t.Folio, t.ServiceDate, t.CustomerId, t.Status, t.Subtotal, t.Total })
            .ToListAsync(cancellationToken);

        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Stamped && i.Type == CfdiType.Income && i.IssueDate >= query.From && i.IssueDate <= query.To)
            .WhereIf(query.CustomerId is not null, i => i.CustomerId == query.CustomerId)
            .Select(i => new { i.CustomerId, i.IssueDate, i.Subtotal, i.Discount })
            .ToListAsync(cancellationToken);

        var fromInstant = clock.StartOfDay(query.From);
        var toInstant = clock.StartOfDay(query.To.AddDays(1));
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= fromInstant && p.PaidAt < toInstant)
            .WhereIf(query.CustomerId is not null, p => p.CustomerId == query.CustomerId)
            .Select(p => new { p.CustomerId, p.PaidAt, p.Amount })
            .ToListAsync(cancellationToken);

        var receivables = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Stamped && i.Balance > 0)
            .WhereIf(query.CustomerId is not null, i => i.CustomerId == query.CustomerId)
            .GroupBy(i => i.CustomerId)
            .Select(g => new { CustomerId = g.Key, Balance = g.Sum(i => i.Balance) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Balance, cancellationToken);

        var customerIds = tickets.Select(t => t.CustomerId).Concat(invoices.Select(i => i.CustomerId)).Concat(payments.Select(p => p.CustomerId)).Concat(receivables.Keys).Distinct();
        var customers = await CatalogNames.LoadAsync<Customer>(db, customerIds, cancellationToken);
        var pending = tickets.Where(t => t.Status is TicketStatus.Signed or TicketStatus.Approved).ToList();

        var months = new List<BillingMonthDto>();
        for (var month = new DateOnly(query.From.Year, query.From.Month, 1); month <= query.To; month = month.AddMonths(1))
        {
            var start = month;
            var end = month.AddMonths(1);
            months.Add(new BillingMonthDto(month.Year, month.Month,
                tickets.Where(t => t.ServiceDate >= start && t.ServiceDate < end).Sum(t => t.Subtotal),
                invoices.Where(i => i.IssueDate >= start && i.IssueDate < end).Sum(i => i.Subtotal - i.Discount),
                payments.Where(p => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(p.PaidAt, clock.TimeZone).DateTime) is var d && d >= start && d < end).Sum(p => p.Amount),
                tickets.Count(t => t.ServiceDate >= start && t.ServiceDate < end)));
        }

        var byCustomer = customers.Keys.Select(id => new BillingCustomerDto(id, customers.NameOf(id) ?? "—",
                tickets.Where(t => t.CustomerId == id).Sum(t => t.Subtotal),
                pending.Where(t => t.CustomerId == id).Sum(t => t.Subtotal),
                invoices.Where(i => i.CustomerId == id).Sum(i => i.Subtotal - i.Discount),
                payments.Where(p => p.CustomerId == id).Sum(p => p.Amount),
                receivables.GetValueOrDefault(id)))
            .OrderByDescending(c => c.Executed)
            .ToList();

        var pendingRows = pending
            .Select(t => new UninvoicedTicketDto(t.Id, t.Folio, t.ServiceDate, customers.NameOf(t.CustomerId), t.Status, t.Total, today.DayNumber - t.ServiceDate.DayNumber,
                today.DayNumber - t.ServiceDate.DayNumber > alertDays))
            .OrderByDescending(t => t.DaysWaiting)
            .ToList();

        return new BillingBoardDto(
            tickets.Sum(t => t.Subtotal),
            pending.Sum(t => t.Subtotal),
            invoices.Sum(i => i.Subtotal - i.Discount),
            payments.Sum(p => p.Amount),
            receivables.Values.Sum(),
            pending.Count,
            pendingRows.Count(t => t.Alert),
            alertDays,
            months,
            byCustomer,
            pendingRows.Take(100).ToList());
    }
}

// ---- Management dashboard (module 18) ---------------------------------------------------------------------

public sealed record UnitUtilizationDto(Guid UnitId, string Code, string Name, UnitStatus Status, int DaysWorked, decimal UtilizationPercent, decimal Revenue);

public sealed record DashboardDto(
    DateOnly From,
    DateOnly To,
    decimal UnitUtilizationPercent,
    decimal RevenuePerUnit,
    decimal AverageDaysTicketToInvoice,
    decimal OverdueReceivables,
    int ServicesThisMonth,
    decimal ServicesAmountThisMonth,
    int ServicesPreviousMonth,
    decimal ServicesAmountPreviousMonth,
    IReadOnlyList<UnitUtilizationDto> Units,
    IReadOnlyList<KpiDto> RevenueByServiceType,
    IReadOnlyList<KpiDto> RevenueByCustomer,
    IReadOnlyList<KpiDto> TicketsByDay,
    IReadOnlyList<KpiDto> Aging,
    IReadOnlyDictionary<string, int> FleetStatus);

/// <summary>
/// Los 5 números del dueño: utilización de unidades, ingreso por unidad, días promedio boleta→factura, cartera vencida y
/// servicios del mes (con su comparación contra el mes anterior), más los desgloses para las gráficas.
/// </summary>
public sealed record GetDashboardQuery(DateOnly? From, DateOnly? To) : IQuery<DashboardDto>;

internal sealed class GetDashboardQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<GetDashboardQuery, DashboardDto>
{
    public async Task<Result<DashboardDto>> Handle(GetDashboardQuery query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var from = query.From ?? monthStart;
        var to = query.To ?? today;
        if (to < from)
        {
            return Error.Validation("Dashboard.InvalidPeriod", "El periodo no es válido.");
        }

        var days = to.DayNumber - from.DayNumber + 1;
        var units = await db.Units.AsNoTracking().Where(u => u.IsActive && u.Type != UnitType.Pickup).ToListAsync(cancellationToken);
        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => t.ServiceDate >= from && t.ServiceDate <= to && t.Status != TicketStatus.Draft && t.Status != TicketStatus.Cancelled)
            .Select(t => new { t.Id, t.ServiceDate, t.UnitIds, t.Subtotal, t.CustomerId, t.ServiceTypeId, t.SignedAt })
            .ToListAsync(cancellationToken);

        // Utilization: unit-days with at least one ticket over available unit-days.
        var unitRows = units.Select(u =>
        {
            var own = tickets.Where(t => t.UnitIds.Contains(u.Id)).ToList();
            var worked = own.Select(t => t.ServiceDate).Distinct().Count();
            var revenue = own.Sum(t => t.Subtotal / Math.Max(1, t.UnitIds.Count));
            return new UnitUtilizationDto(u.Id, u.Code, u.Name, u.Status, worked, Math.Round(worked * 100m / days, 1), Math.Round(revenue, 2));
        }).OrderByDescending(r => r.UtilizationPercent).ToList();

        var utilization = unitRows.Count == 0 ? 0 : Math.Round(unitRows.Average(r => r.UtilizationPercent), 1);
        var revenuePerUnit = unitRows.Count == 0 ? 0 : Math.Round(unitRows.Sum(r => r.Revenue) / unitRows.Count, 2);

        var invoiced = await db.Tickets.AsNoTracking()
            .Where(t => t.Status == TicketStatus.Invoiced && t.InvoicedAt != null && t.ServiceDate >= from.AddDays(-90))
            .Select(t => new { t.ServiceDate, t.InvoicedAt })
            .ToListAsync(cancellationToken);
        var averageDays = invoiced.Count == 0
            ? 0
            : Math.Round((decimal)invoiced.Average(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.InvoicedAt!.Value, clock.TimeZone).DateTime).DayNumber - t.ServiceDate.DayNumber), 1);

        var openInvoices = await db.Invoices.AsNoTracking().Where(i => i.Status == InvoiceStatus.Stamped && i.Balance > 0).Select(i => new { i.Balance, i.DueOn }).ToListAsync(cancellationToken);
        var overdue = openInvoices.Where(i => i.DueOn < today).Sum(i => i.Balance);

        var previousStart = monthStart.AddMonths(-1);
        var monthTickets = await db.Tickets.AsNoTracking()
            .Where(t => t.ServiceDate >= previousStart && t.ServiceDate <= today && t.Status != TicketStatus.Draft && t.Status != TicketStatus.Cancelled)
            .Select(t => new { t.ServiceDate, t.Subtotal })
            .ToListAsync(cancellationToken);
        var current = monthTickets.Where(t => t.ServiceDate >= monthStart).ToList();
        var previous = monthTickets.Where(t => t.ServiceDate < monthStart && t.ServiceDate <= previousStart.AddDays(today.Day - 1)).ToList();

        var types = await CatalogNames.LoadAsync<ServiceType>(db, tickets.Select(t => t.ServiceTypeId), cancellationToken);
        var customers = await CatalogNames.LoadAsync<Customer>(db, tickets.Select(t => t.CustomerId), cancellationToken);
        var fleetStatus = units.GroupBy(u => u.Status.ToString()).ToDictionary(g => g.Key, g => g.Count());

        return new DashboardDto(
            from, to, utilization, revenuePerUnit, averageDays, overdue,
            current.Count, current.Sum(t => t.Subtotal), previous.Count, previous.Sum(t => t.Subtotal),
            unitRows,
            tickets.GroupBy(t => t.ServiceTypeId).Select(g => new KpiDto(types.NameOf(g.Key) ?? "—", g.Sum(t => t.Subtotal))).OrderByDescending(k => k.Value).ToList(),
            tickets.GroupBy(t => t.CustomerId).Select(g => new KpiDto(customers.NameOf(g.Key) ?? "—", g.Sum(t => t.Subtotal))).OrderByDescending(k => k.Value).Take(10).ToList(),
            tickets.GroupBy(t => t.ServiceDate).OrderBy(g => g.Key).Select(g => new KpiDto(g.Key.ToString("yyyy-MM-dd"), g.Count())).ToList(),
            [
                new KpiDto("Por vencer", openInvoices.Where(i => i.DueOn >= today).Sum(i => i.Balance)),
                new KpiDto("1-30", openInvoices.Where(i => today.DayNumber - i.DueOn.DayNumber is >= 1 and <= 30).Sum(i => i.Balance)),
                new KpiDto("31-60", openInvoices.Where(i => today.DayNumber - i.DueOn.DayNumber is >= 31 and <= 60).Sum(i => i.Balance)),
                new KpiDto("61-90", openInvoices.Where(i => today.DayNumber - i.DueOn.DayNumber is >= 61 and <= 90).Sum(i => i.Balance)),
                new KpiDto("+90", openInvoices.Where(i => today.DayNumber - i.DueOn.DayNumber > 90).Sum(i => i.Balance))
            ],
            fleetStatus);
    }
}

// ---- Shell widgets (summary of the month) ---------------------------------------------------------------

public sealed record MonthSummaryDto(int SignedTickets, decimal Amount, int PendingInvoice, decimal PendingAmount, int Drafts, int OpenWorkOrders);

public sealed record GetMonthSummaryQuery : IQuery<MonthSummaryDto>;

internal sealed class GetMonthSummaryQueryHandler(IApplicationDbContext db, ITenantClock clock) : IQueryHandler<GetMonthSummaryQuery, MonthSummaryDto>
{
    public async Task<Result<MonthSummaryDto>> Handle(GetMonthSummaryQuery query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var month = await db.Tickets.AsNoTracking().Where(t => t.ServiceDate >= monthStart && t.Status != TicketStatus.Cancelled)
            .Select(t => new { t.Status, t.Subtotal }).ToListAsync(cancellationToken);
        var pending = await db.Tickets.AsNoTracking().Where(t => t.Status == TicketStatus.Signed || t.Status == TicketStatus.Approved)
            .Select(t => t.Subtotal).ToListAsync(cancellationToken);
        var openOrders = await db.WorkOrders.AsNoTracking().CountAsync(w => w.Status == WorkOrderStatus.Scheduled || w.Status == WorkOrderStatus.Dispatched || w.Status == WorkOrderStatus.InProgress, cancellationToken);

        return new MonthSummaryDto(
            month.Count(t => t.Status != TicketStatus.Draft),
            month.Where(t => t.Status != TicketStatus.Draft).Sum(t => t.Subtotal),
            pending.Count,
            pending.Sum(),
            month.Count(t => t.Status == TicketStatus.Draft),
            openOrders);
    }
}

// ---- Customer portal (module 19) ------------------------------------------------------------------------

public sealed record PortalTicketDto(Guid Id, string Folio, DateOnly ServiceDate, string? SiteName, TicketStatus Status, decimal Total);

public sealed record PortalSummaryDto(
    Guid CustomerId, string CustomerName, int TicketsThisMonth, decimal AmountThisMonth, int TicketsLastMonth, int ScheduledOrders, int OpenInvoices, decimal Balance,
    decimal OverdueBalance, IReadOnlyList<PortalTicketDto> RecentTickets);

/// <summary>Home of the customer portal: services of the month, scheduled orders and account status of the customer.</summary>
public sealed record GetPortalSummaryQuery(Guid? CustomerId) : IQuery<PortalSummaryDto>;

internal sealed class GetPortalSummaryQueryHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock) : IQueryHandler<GetPortalSummaryQuery, PortalSummaryDto>
{
    public async Task<Result<PortalSummaryDto>> Handle(GetPortalSummaryQuery query, CancellationToken cancellationToken)
    {
        var customerId = tenant.Membership?.CustomerId ?? query.CustomerId;
        var customer = customerId is null ? null : await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return Error.Validation("Portal.CustomerRequired", "El usuario del portal no tiene un cliente asignado.");
        }

        var today = clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);
        var tickets = db.Tickets.AsNoTracking().Where(t => t.CustomerId == customer.Id && t.Status != TicketStatus.Draft && t.Status != TicketStatus.Cancelled);
        var month = await tickets.Where(t => t.ServiceDate >= monthStart).Select(t => t.Subtotal).ToListAsync(cancellationToken);
        var lastMonth = await tickets.CountAsync(t => t.ServiceDate >= lastMonthStart && t.ServiceDate < monthStart, cancellationToken);
        var scheduled = await db.WorkOrders.AsNoTracking().CountAsync(w => w.CustomerId == customer.Id
            && (w.Status == WorkOrderStatus.Scheduled || w.Status == WorkOrderStatus.Dispatched || w.Status == WorkOrderStatus.InProgress), cancellationToken);
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.CustomerId == customer.Id && i.Status == InvoiceStatus.Stamped && i.Balance > 0)
            .Select(i => new { i.Balance, i.DueOn }).ToListAsync(cancellationToken);
        var recent = await tickets.OrderByDescending(t => t.ServiceDate).ThenByDescending(t => t.FolioNumber).Take(10)
            .Select(t => new PortalTicketDto(t.Id, t.Folio, t.ServiceDate, db.Sites.Where(s => s.Id == t.SiteId).Select(s => s.Name).FirstOrDefault(), t.Status, t.Total))
            .ToListAsync(cancellationToken);

        return new PortalSummaryDto(customer.Id, customer.Name, month.Count, month.Sum(), lastMonth, scheduled, invoices.Count, invoices.Sum(i => i.Balance),
            invoices.Where(i => i.DueOn < today).Sum(i => i.Balance), recent);
    }
}
