using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Invoicing;

public sealed record TripSummaryDto(Guid Id, string Folio, TripStatus Status, CfdiType CfdiType, string? UnitCode, string? OperatorName, string OriginName, string DestinationName,
    DateTimeOffset Departure, decimal DistanceKm, decimal TotalWeightKg, bool HasHazardousGoods, string? Uuid);

public sealed record TripDetailDto(TripSummaryDto Summary, Guid? CustomerId, Guid? WorkOrderId, Guid UnitId, Guid? TrailerUnitId, Guid OperatorId, TripLocation Origin,
    TripLocation Destination, IReadOnlyList<TripGoods> Goods, Guid? FreightServiceId, decimal? FreightAmount, string IdCcp, Guid? InvoiceId, string? Notes,
    string? CancellationReason, IReadOnlyList<string> MissingData);

public sealed record SearchTripsQuery(TripStatus? Status, Guid? UnitId, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 25) : IQuery<PagedResult<TripSummaryDto>>;

internal sealed class SearchTripsQueryHandler(IApplicationDbContext db) : IQueryHandler<SearchTripsQuery, PagedResult<TripSummaryDto>>
{
    public async Task<Result<PagedResult<TripSummaryDto>>> Handle(SearchTripsQuery query, CancellationToken cancellationToken)
    {
        var page = await db.Trips.AsNoTracking()
            .WhereIf(query.Status is not null, t => t.Status == query.Status)
            .WhereIf(query.UnitId is not null, t => t.UnitId == query.UnitId)
            .OrderByDescending(t => t.FolioNumber)
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);

        var units = await CatalogNames.LoadAsync<Unit>(db, page.Items.Select(t => t.UnitId), cancellationToken);
        var people = await CatalogNames.LoadAsync<Employee>(db, page.Items.Select(t => t.OperatorId), cancellationToken);
        var items = page.Items
            .Where(t => (query.From is null || DateOnly.FromDateTime(t.Origin.DateTime.UtcDateTime) >= query.From) && (query.To is null || DateOnly.FromDateTime(t.Origin.DateTime.UtcDateTime) <= query.To))
            .Select(t => TripProjection.Summary(t, units, people)).ToList();
        return new PagedResult<TripSummaryDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

internal static class TripProjection
{
    public static TripSummaryDto Summary(Trip t, Dictionary<Guid, CodeName> units, Dictionary<Guid, CodeName> people) =>
        new(t.Id, t.Folio, t.Status, t.CfdiType, units.CodeOf(t.UnitId), people.NameOf(t.OperatorId), t.Origin.Name, t.Destination.Name, t.Origin.DateTime, t.DistanceKm,
            t.TotalWeightKg, t.HasHazardousGoods, t.Uuid);
}

public sealed record GetTripQuery(Guid Id) : IQuery<TripDetailDto>;

internal sealed class GetTripQueryHandler(IApplicationDbContext db) : IQueryHandler<GetTripQuery, TripDetailDto>
{
    public async Task<Result<TripDetailDto>> Handle(GetTripQuery query, CancellationToken cancellationToken)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == query.Id, cancellationToken);
        if (trip is null)
        {
            return TripErrors.NotFound;
        }

        var units = await db.Units.AsNoTracking().Where(u => u.Id == trip.UnitId || u.Id == trip.TrailerUnitId).ToListAsync(cancellationToken);
        var driver = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == trip.OperatorId, cancellationToken);
        var missing = new List<string>();
        var unit = units.FirstOrDefault(u => u.Id == trip.UnitId);
        if (unit is not null && driver is not null && trip.Status == TripStatus.Draft)
        {
            var check = trip.ValidateForStamping(unit, units.FirstOrDefault(u => u.Id == trip.TrailerUnitId), driver);
            if (check.IsFailure)
            {
                missing.Add(check.Error.Description);
            }
        }

        var unitNames = units.ToDictionary(u => u.Id, u => new CodeName(u.Code, u.Name));
        var people = driver is null ? [] : new Dictionary<Guid, CodeName> { [driver.Id] = new(driver.Code, driver.Name) };
        return new TripDetailDto(TripProjection.Summary(trip, unitNames, people), trip.CustomerId, trip.WorkOrderId, trip.UnitId, trip.TrailerUnitId, trip.OperatorId, trip.Origin,
            trip.Destination, trip.Goods, trip.FreightServiceId, trip.FreightAmount, trip.IdCcp, trip.InvoiceId, trip.Notes, trip.CancellationReason, missing);
    }
}

public sealed record SaveTripCommand(Guid? Id, TripData Data) : ICommand<Guid>;

internal sealed class SaveTripCommandValidator : AbstractValidator<SaveTripCommand>
{
    public SaveTripCommandValidator()
    {
        RuleFor(c => c.Data.UnitId).RequiredId("la unidad");
        RuleFor(c => c.Data.OperatorId).RequiredId("el operador");
        RuleFor(c => c.Data.Goods).NotEmpty().WithMessage("Registra al menos una mercancía.");
        RuleFor(c => c.Data.DistanceKm).GreaterThan(0);
    }
}

internal sealed class SaveTripCommandHandler(IApplicationDbContext db, ITenantContext tenant, IFolioService folios) : ICommandHandler<SaveTripCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveTripCommand command, CancellationToken cancellationToken)
    {
        var references = Result.FirstFailure(
            await References.EnsureAsync<Unit>(db, command.Data.UnitId, UnitErrors.Catalog, cancellationToken),
            await References.EnsureAsync<Unit>(db, command.Data.TrailerUnitId, UnitErrors.Catalog, cancellationToken),
            await References.EnsureAsync<Employee>(db, command.Data.OperatorId, EmployeeErrors.Catalog, cancellationToken),
            await References.EnsureAsync<Customer>(db, command.Data.CustomerId, CustomerErrors.Catalog, cancellationToken),
            await References.EnsureAsync<Service>(db, command.Data.FreightServiceId, ServiceErrors.Catalog, cancellationToken));
        if (references.IsFailure)
        {
            return references.Error;
        }

        if (command.Id is { } id)
        {
            var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (trip is null)
            {
                return TripErrors.NotFound;
            }

            var updated = trip.Update(command.Data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            await db.SaveChangesAsync(cancellationToken);
            return trip.Id;
        }

        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var number = await folios.NextAsync(FolioKeys.Trip, ct);
            var created = Trip.Create(FolioSequence.Format(tenant.Tenant.Settings.TripPrefix, number), number, command.Data);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error);
            }

            db.Trips.Add(created.Value);
            await db.SaveChangesAsync(ct);
            return Result.Success(created.Value.Id);
        }, cancellationToken);
    }
}

/// <summary>
/// Timbra la Carta Porte: validates unit, trailer and driver data and issues a CFDI of type T (goods only) or I
/// (freight charged to the customer) with the Carta Porte 3.1 complement.
/// </summary>
public sealed record StampTripCommand(Guid Id) : ICommand<string>;

internal sealed class StampTripCommandHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock, InvoiceStamping stamping) : ICommandHandler<StampTripCommand, string>
{
    public async Task<Result<string>> Handle(StampTripCommand command, CancellationToken cancellationToken)
    {
        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == command.Id, ct);
            if (trip is null)
            {
                return Result.Failure<string>(TripErrors.NotFound);
            }

            var unit = await db.Units.AsNoTracking().FirstAsync(u => u.Id == trip.UnitId, ct);
            var trailer = trip.TrailerUnitId is null ? null : await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == trip.TrailerUnitId, ct);
            var driver = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == trip.OperatorId, ct);
            var valid = trip.ValidateForStamping(unit, trailer, driver);
            if (valid.IsFailure)
            {
                return Result.Failure<string>(valid.Error);
            }

            var fiscal = tenant.Tenant.Fiscal;
            Customer? customer = trip.CustomerId is null ? null : await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == trip.CustomerId, ct);
            var receiver = trip.CfdiType == CfdiType.Transfer || customer is null
                ? new InvoiceReceiver(fiscal.Rfc ?? MexicanIds.GenericRfc, fiscal.LegalName ?? tenant.Tenant.Name, fiscal.TaxRegime, fiscal.PostalCode, "S01")
                : new InvoiceReceiver(customer.Rfc, customer.LegalName, customer.TaxRegime, customer.PostalCode, customer.CfdiUse);

            List<InvoiceLineData> lines;
            if (trip.CfdiType == CfdiType.Income)
            {
                var freight = await db.Services.AsNoTracking().FirstAsync(s => s.Id == trip.FreightServiceId, ct);
                lines = [new InvoiceLineData(null, freight.Id, freight.SatProductCode, freight.SatUnitCode, freight.UnitName,
                    $"{freight.Name} · {trip.Origin.Name} → {trip.Destination.Name} ({trip.Folio})", 1, trip.FreightAmount!.Value, 0, freight.TaxObject, freight.VatRate)];
            }
            else
            {
                lines = trip.Goods.Select(g => new InvoiceLineData(null, null, g.SatGoodsCode, g.SatUnitCode, g.SatUnitCode, g.Description, g.Quantity, 0, 0, "01", 0)).ToList();
            }

            var draft = Invoice.CreateDraft(fiscal.InvoiceSeries, new InvoiceDraftData(customer?.Id ?? Guid.Empty, receiver, trip.CfdiType,
                trip.CfdiType == CfdiType.Income ? customer!.PaymentMethod == "PPD" ? "99" : customer.PaymentForm : "99",
                trip.CfdiType == CfdiType.Income ? customer!.PaymentMethod : "PUE", "MXN", 1, clock.Today, customer?.CreditDays ?? 0, lines, [], null, trip.Id, null,
                $"Carta Porte {trip.Folio} · IdCCP {trip.IdCcp}"));
            if (draft.IsFailure)
            {
                return Result.Failure<string>(draft.Error);
            }

            var invoice = draft.Value;
            db.Invoices.Add(invoice);

            var cartaPorte = new CfdiCartaPorte(trip.IdCcp, trip.DistanceKm, trip.Origin.Name, trip.Origin.Rfc, trip.Origin.PostalCode, trip.Origin.State, trip.Origin.DateTime,
                trip.Destination.Name, trip.Destination.Rfc, trip.Destination.PostalCode, trip.Destination.State, trip.Destination.DateTime,
                trip.Goods.Select(g => (g.SatGoodsCode, g.Description, g.Quantity, g.SatUnitCode, g.WeightKg, g.IsHazardous, g.HazardousCode, g.PackagingCode)).ToList(),
                trip.TotalWeightKg, unit.SctPermitType!, unit.SctPermitNumber!, unit.VehicleConfig!, unit.GrossWeightTons!.Value, unit.Plates!, unit.Year!.Value,
                unit.InsurerName!, unit.InsurancePolicy!, unit.EnvironmentalInsurer, unit.EnvironmentalPolicy, trailer?.Plates, driver.Rfc!, driver.Name, driver.LicenseNumber!);

            var stamped = await stamping.StampAsync(invoice, cartaPorte, ct);
            if (stamped.IsFailure)
            {
                return Result.Failure<string>(stamped.Error);
            }

            var marked = trip.MarkStamped(invoice.Uuid!, invoice.StampedAt!.Value, invoice.Id);
            if (marked.IsFailure)
            {
                return Result.Failure<string>(marked.Error);
            }

            await db.SaveChangesAsync(ct);
            return Result.Success(invoice.Uuid!);
        }, cancellationToken);
    }
}

public sealed record CancelTripCommand(Guid Id, string Reason) : ICommand;

internal sealed class CancelTripCommandHandler(IApplicationDbContext db, ICommandHandler<CancelInvoiceCommand> cancelInvoice) : ICommandHandler<CancelTripCommand>
{
    public async Task<Result> Handle(CancelTripCommand command, CancellationToken cancellationToken)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (trip is null)
        {
            return TripErrors.NotFound;
        }

        if (trip.Status == TripStatus.Stamped && trip.InvoiceId is { } invoiceId)
        {
            var cancelled = await cancelInvoice.Handle(new CancelInvoiceCommand(invoiceId, "02", null), cancellationToken);
            if (cancelled.IsFailure)
            {
                return cancelled;
            }
        }

        var result = trip.Cancel(command.Reason);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
