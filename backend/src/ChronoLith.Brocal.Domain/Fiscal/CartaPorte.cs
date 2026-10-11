using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using FleetUnit = ChronoLith.Brocal.Domain.Fleet.Unit;

namespace ChronoLith.Brocal.Domain.Fiscal;

public enum TripStatus
{
    Draft = 0,
    Stamped = 1,
    Cancelled = 2
}

/// <summary>Origen o destino de la Carta Porte.</summary>
public sealed record TripLocation(
    Guid? SiteId,
    string Name,
    string? Rfc,
    string PostalCode,
    string State,
    string? Municipality,
    string? Address,
    DateTimeOffset DateTime);

/// <summary>Mercancía transportada (agua congénita, diésel, lodos, químicos…).</summary>
public sealed record TripGoods(
    string SatGoodsCode,
    string Description,
    decimal Quantity,
    string SatUnitCode,
    decimal WeightKg,
    bool IsHazardous,
    string? HazardousCode,
    string? PackagingCode);

public sealed record TripData(
    CfdiType CfdiType,
    Guid? CustomerId,
    Guid? WorkOrderId,
    Guid UnitId,
    Guid? TrailerUnitId,
    Guid OperatorId,
    TripLocation Origin,
    TripLocation Destination,
    decimal DistanceKm,
    IReadOnlyList<TripGoods> Goods,
    Guid? FreightServiceId,
    decimal? FreightAmount,
    string? Notes);

/// <summary>
/// Viaje de autotanque documentado con el complemento Carta Porte 3.1: unit, trailer, driver, route and goods
/// (hazardous materials included). It is stamped as a CFDI of type T (transfer) or I (freight charged).
/// </summary>
public sealed class Trip : TenantAggregateRoot, IAuditReference
{
    private List<TripGoods> _goods = [];

    private Trip()
    {
    }

    public string Folio { get; private set; } = null!;

    public long FolioNumber { get; private set; }

    public TripStatus Status { get; private set; } = TripStatus.Draft;

    public CfdiType CfdiType { get; private set; }

    public Guid? CustomerId { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public Guid UnitId { get; private set; }

    public Guid? TrailerUnitId { get; private set; }

    public Guid OperatorId { get; private set; }

    public TripLocation Origin { get; private set; } = null!;

    public TripLocation Destination { get; private set; } = null!;

    public decimal DistanceKm { get; private set; }

    public IReadOnlyList<TripGoods> Goods => _goods;

    public decimal TotalWeightKg { get; private set; }

    public bool HasHazardousGoods => _goods.Any(g => g.IsHazardous);

    /// <summary>Service billed as freight when the CFDI is of type I.</summary>
    public Guid? FreightServiceId { get; private set; }

    public decimal? FreightAmount { get; private set; }

    /// <summary>IdCCP: identificador de la Carta Porte (CCC + 33 caracteres de un UUID).</summary>
    public string IdCcp { get; private set; } = null!;

    public Guid? InvoiceId { get; private set; }

    public string? Uuid { get; private set; }

    public DateTimeOffset? StampedAt { get; private set; }

    public string? Notes { get; private set; }

    public string? CancellationReason { get; private set; }

    public string AuditReference => Folio;

    public static Result<Trip> Create(string folio, long folioNumber, TripData data)
    {
        var trip = new Trip
        {
            Folio = folio,
            FolioNumber = folioNumber,
            IdCcp = "CCC" + Guid.NewGuid().ToString().ToUpperInvariant()[3..]
        };

        var result = trip.Apply(data);
        return result.IsFailure ? result.Error : trip;
    }

    public Result Update(TripData data)
    {
        if (Status != TripStatus.Draft)
        {
            return TripErrors.NotDraft;
        }

        return Apply(data);
    }

    /// <summary>
    /// Checks everything the SAT validates for Autotransporte: vehicle configuration, plates, SCT permit, insurance
    /// (environmental policy for hazardous goods), driver RFC and federal license.
    /// </summary>
    public Result ValidateForStamping(FleetUnit unit, FleetUnit? trailer, Employee driver)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(unit.VehicleConfig)) missing.Add("configuración vehicular de la unidad");
        if (string.IsNullOrWhiteSpace(unit.Plates)) missing.Add("placas de la unidad");
        if (unit.Year is null) missing.Add("año del modelo de la unidad");
        if (string.IsNullOrWhiteSpace(unit.SctPermitType) || string.IsNullOrWhiteSpace(unit.SctPermitNumber)) missing.Add("permiso SCT de la unidad");
        if (string.IsNullOrWhiteSpace(unit.InsurerName) || string.IsNullOrWhiteSpace(unit.InsurancePolicy)) missing.Add("póliza de responsabilidad civil");
        if (unit.GrossWeightTons is null) missing.Add("peso bruto vehicular");
        if (HasHazardousGoods && (string.IsNullOrWhiteSpace(unit.EnvironmentalInsurer) || string.IsNullOrWhiteSpace(unit.EnvironmentalPolicy)))
            missing.Add("póliza de daño ambiental (material peligroso)");
        if (trailer is not null && string.IsNullOrWhiteSpace(trailer.Plates)) missing.Add("placas del remolque");
        if (string.IsNullOrWhiteSpace(driver.Rfc)) missing.Add("RFC del operador");
        if (string.IsNullOrWhiteSpace(driver.LicenseNumber)) missing.Add("licencia federal del operador");

        return missing.Count == 0 ? Result.Success() : TripErrors.MissingData(string.Join(", ", missing));
    }

    public Result MarkStamped(string uuid, DateTimeOffset at, Guid invoiceId)
    {
        if (Status != TripStatus.Draft)
        {
            return TripErrors.NotDraft;
        }

        Uuid = uuid.ToUpperInvariant();
        StampedAt = at;
        InvoiceId = invoiceId;
        Status = TripStatus.Stamped;
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return TripErrors.ReasonRequired;
        }

        if (Status == TripStatus.Cancelled)
        {
            return TripErrors.AlreadyCancelled;
        }

        Status = TripStatus.Cancelled;
        CancellationReason = reason.Trim();
        return Result.Success();
    }

    private Result Apply(TripData data)
    {
        if (data.Goods.Count == 0)
        {
            return TripErrors.NoGoods;
        }

        foreach (var goods in data.Goods)
        {
            if (string.IsNullOrWhiteSpace(goods.SatGoodsCode) || string.IsNullOrWhiteSpace(goods.Description) || goods.Quantity <= 0 || goods.WeightKg <= 0)
            {
                return TripErrors.InvalidGoods;
            }

            if (goods.IsHazardous && string.IsNullOrWhiteSpace(goods.HazardousCode))
            {
                return TripErrors.HazardousCodeRequired;
            }
        }

        if (data.DistanceKm <= 0 || data.DistanceKm > 5000)
        {
            return TripErrors.InvalidDistance;
        }

        if (!MexicanIds.IsValidPostalCode(data.Origin.PostalCode) || !MexicanIds.IsValidPostalCode(data.Destination.PostalCode))
        {
            return TripErrors.InvalidPostalCode;
        }

        if (data.Destination.DateTime <= data.Origin.DateTime)
        {
            return TripErrors.InvalidDates;
        }

        if (data.CfdiType == CfdiType.Income && (data.CustomerId is null || data.FreightServiceId is null || data.FreightAmount is null or <= 0))
        {
            return TripErrors.FreightRequired;
        }

        if (data.TrailerUnitId == data.UnitId)
        {
            return TripErrors.TrailerSameAsUnit;
        }

        CfdiType = data.CfdiType;
        CustomerId = data.CustomerId;
        WorkOrderId = data.WorkOrderId;
        UnitId = data.UnitId;
        TrailerUnitId = data.TrailerUnitId;
        OperatorId = data.OperatorId;
        Origin = data.Origin with { Name = data.Origin.Name.Trim(), Rfc = data.Origin.Rfc?.Trim().ToUpperInvariant() };
        Destination = data.Destination with { Name = data.Destination.Name.Trim(), Rfc = data.Destination.Rfc?.Trim().ToUpperInvariant() };
        DistanceKm = data.DistanceKm;
        _goods = data.Goods.Select(g => g with
        {
            SatGoodsCode = g.SatGoodsCode.Trim(),
            Description = g.Description.Trim(),
            SatUnitCode = g.SatUnitCode.Trim().ToUpperInvariant(),
            HazardousCode = g.IsHazardous ? g.HazardousCode?.Trim().ToUpperInvariant() : null,
            PackagingCode = g.IsHazardous ? g.PackagingCode?.Trim().ToUpperInvariant() : null
        }).ToList();
        TotalWeightKg = Math.Round(_goods.Sum(g => g.WeightKg), 3);
        FreightServiceId = data.CfdiType == CfdiType.Income ? data.FreightServiceId : null;
        FreightAmount = data.CfdiType == CfdiType.Income ? data.FreightAmount : null;
        Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();
        return Result.Success();
    }
}

public static class TripErrors
{
    public static readonly Error NotFound = Error.NotFound("Trips.NotFound", "El viaje no existe.");

    public static readonly Error NotDraft = Error.Conflict("Trips.NotDraft", "El viaje ya fue timbrado o cancelado.");

    public static readonly Error NoGoods = Error.Validation("Trips.NoGoods", "Registra al menos una mercancía.");

    public static readonly Error InvalidGoods = Error.Validation("Trips.InvalidGoods", "Cada mercancía necesita clave SAT, descripción, cantidad y peso mayores a cero.");

    public static readonly Error HazardousCodeRequired = Error.Validation("Trips.HazardousCodeRequired", "El material peligroso necesita su clave (p. ej. UN1202).");

    public static readonly Error InvalidDistance = Error.Validation("Trips.InvalidDistance", "La distancia recorrida debe ser mayor a cero.");

    public static readonly Error InvalidPostalCode = Error.Validation("Trips.InvalidPostalCode", "Origen y destino necesitan código postal de 5 dígitos.");

    public static readonly Error InvalidDates = Error.Validation("Trips.InvalidDates", "La llegada debe ser posterior a la salida.");

    public static readonly Error FreightRequired = Error.Validation("Trips.FreightRequired", "Un viaje de ingreso necesita cliente, servicio de flete e importe.");

    public static readonly Error TrailerSameAsUnit = Error.Validation("Trips.TrailerSameAsUnit", "El remolque no puede ser la misma unidad.");

    public static readonly Error ReasonRequired = Error.Validation("Trips.ReasonRequired", "Indica el motivo.");

    public static readonly Error AlreadyCancelled = Error.Conflict("Trips.AlreadyCancelled", "El viaje ya está cancelado.");

    public static Error MissingData(string what) => Error.Validation("Trips.MissingData", $"Faltan datos para la Carta Porte: {what}.");
}
