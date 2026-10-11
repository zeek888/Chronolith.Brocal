using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;

namespace ChronoLith.Brocal.Domain.Fleet;

public enum UnitType
{
    TankTruck = 0,
    Tractor = 1,
    Trailer = 2,
    VacuumTruck = 3,
    PumpUnit = 4,
    Crane = 5,
    Pickup = 6,
    Equipment = 7,
    Other = 8
}

public enum UnitStatus
{
    Available = 0,
    Operating = 1,
    Workshop = 2,
    OutOfService = 3
}

/// <summary>Which meter drives maintenance and fuel efficiency.</summary>
public enum MeterKind
{
    Odometer = 0,
    HourMeter = 1,
    Both = 2
}

public enum MeterSource
{
    Manual = 0,
    Ticket = 1,
    Fuel = 2,
    Maintenance = 3,
    Gps = 4
}

public sealed record UnitData(
    string Code,
    string Name,
    UnitType Type,
    string? Brand,
    string? Model,
    int? Year,
    string? Plates,
    string? SerialNumber,
    MeterKind MeterKind,
    decimal? Capacity,
    string? CapacityUnit,
    string? FuelType,
    decimal? FuelTankLiters,
    decimal? ExpectedEfficiency,
    string? VehicleConfig,
    decimal? GrossWeightTons,
    string? SctPermitType,
    string? SctPermitNumber,
    string? InsurerName,
    string? InsurancePolicy,
    string? EnvironmentalInsurer,
    string? EnvironmentalPolicy,
    Guid? DefaultOperatorId,
    Guid? PhotoFileId,
    DateOnly? AcquiredOn,
    decimal? AcquisitionCost,
    string? Notes);

public sealed record UnitReleasedEvent(Guid UnitId, Guid? MaintenanceOrderId) : IDomainEvent;

/// <summary>
/// Ficha de unidad o equipo (autotanque, tractocamión, unidad de bombeo, compresor…): estatus, horómetro,
/// kilometraje, datos para Carta Porte y rendimiento esperado de combustible.
/// </summary>
public sealed class Unit : CatalogAggregate
{
    private Unit()
    {
    }

    public UnitType Type { get; private set; }

    public UnitStatus Status { get; private set; } = UnitStatus.Available;

    public string? Brand { get; private set; }

    public string? Model { get; private set; }

    public int? Year { get; private set; }

    public string? Plates { get; private set; }

    public string? SerialNumber { get; private set; }

    public MeterKind MeterKind { get; private set; }

    public decimal CurrentOdometer { get; private set; }

    public decimal CurrentHours { get; private set; }

    public DateTimeOffset? MetersUpdatedAt { get; private set; }

    public decimal? Capacity { get; private set; }

    /// <summary>m³, bbl, l, t…</summary>
    public string? CapacityUnit { get; private set; }

    public string? FuelType { get; private set; }

    public decimal? FuelTankLiters { get; private set; }

    /// <summary>km/l for odometer units, l/h for hour-meter equipment.</summary>
    public decimal? ExpectedEfficiency { get; private set; }

    /// <summary>Configuración vehicular SAT (C2, C3, T3S2…), Carta Porte.</summary>
    public string? VehicleConfig { get; private set; }

    public decimal? GrossWeightTons { get; private set; }

    /// <summary>Tipo de permiso SCT (TPAF01…), Carta Porte.</summary>
    public string? SctPermitType { get; private set; }

    public string? SctPermitNumber { get; private set; }

    public string? InsurerName { get; private set; }

    public string? InsurancePolicy { get; private set; }

    public string? EnvironmentalInsurer { get; private set; }

    public string? EnvironmentalPolicy { get; private set; }

    public Guid? DefaultOperatorId { get; private set; }

    public Guid? PhotoFileId { get; private set; }

    public DateOnly? AcquiredOn { get; private set; }

    public decimal? AcquisitionCost { get; private set; }

    public decimal? LastLatitude { get; private set; }

    public decimal? LastLongitude { get; private set; }

    public DateTimeOffset? LastPositionAt { get; private set; }

    public bool UsesOdometer => MeterKind is MeterKind.Odometer or MeterKind.Both;

    public bool UsesHourMeter => MeterKind is MeterKind.HourMeter or MeterKind.Both;

    public static Result<Unit> Create(UnitData data)
    {
        var unit = new Unit();
        var result = unit.Update(data);
        return result.IsFailure ? result.Error : unit;
    }

    public Result Update(UnitData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if (data.Year is < 1950 or > 2100)
        {
            return UnitErrors.InvalidYear;
        }

        if (data.Capacity is < 0 || data.FuelTankLiters is < 0 || data.ExpectedEfficiency is < 0 || data.GrossWeightTons is < 0 || data.AcquisitionCost is < 0)
        {
            return UnitErrors.NegativeValue;
        }

        Type = data.Type;
        Brand = Clean(data.Brand);
        Model = Clean(data.Model);
        Year = data.Year;
        Plates = Clean(data.Plates)?.ToUpperInvariant();
        SerialNumber = Clean(data.SerialNumber)?.ToUpperInvariant();
        MeterKind = data.MeterKind;
        Capacity = data.Capacity;
        CapacityUnit = Clean(data.CapacityUnit);
        FuelType = Clean(data.FuelType);
        FuelTankLiters = data.FuelTankLiters;
        ExpectedEfficiency = data.ExpectedEfficiency;
        VehicleConfig = Clean(data.VehicleConfig)?.ToUpperInvariant();
        GrossWeightTons = data.GrossWeightTons;
        SctPermitType = Clean(data.SctPermitType)?.ToUpperInvariant();
        SctPermitNumber = Clean(data.SctPermitNumber);
        InsurerName = Clean(data.InsurerName);
        InsurancePolicy = Clean(data.InsurancePolicy);
        EnvironmentalInsurer = Clean(data.EnvironmentalInsurer);
        EnvironmentalPolicy = Clean(data.EnvironmentalPolicy);
        DefaultOperatorId = data.DefaultOperatorId;
        PhotoFileId = data.PhotoFileId;
        AcquiredOn = data.AcquiredOn;
        AcquisitionCost = data.AcquisitionCost;
        return Result.Success();
    }

    /// <summary>Changes the operating status (the history is recorded by the application).</summary>
    public Result ChangeStatus(UnitStatus status, Guid? maintenanceOrderId = null)
    {
        if (!IsActive && status != UnitStatus.OutOfService)
        {
            return UnitErrors.Inactive;
        }

        var previous = Status;
        Status = status;
        if (previous == UnitStatus.Workshop && status == UnitStatus.Available)
        {
            Raise(new UnitReleasedEvent(Id, maintenanceOrderId));
        }

        return Result.Success();
    }

    /// <summary>
    /// Records odometer and/or hour meter. Readings cannot go backwards unless it is an explicit correction.
    /// </summary>
    public Result RecordMeters(decimal? odometer, decimal? hours, DateTimeOffset at, bool isCorrection = false)
    {
        if (odometer is < 0 || hours is < 0)
        {
            return UnitErrors.NegativeValue;
        }

        if (!isCorrection && (odometer is not null && odometer < CurrentOdometer || hours is not null && hours < CurrentHours))
        {
            return UnitErrors.MeterBackwards;
        }

        if (odometer is not null)
        {
            CurrentOdometer = odometer.Value;
        }

        if (hours is not null)
        {
            CurrentHours = hours.Value;
        }

        if (odometer is not null || hours is not null)
        {
            MetersUpdatedAt = at;
        }

        return Result.Success();
    }

    public void RecordPosition(decimal latitude, decimal longitude, DateTimeOffset at)
    {
        if (LastPositionAt is not null && at < LastPositionAt)
        {
            return;
        }

        LastLatitude = latitude;
        LastLongitude = longitude;
        LastPositionAt = at;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>History of meter readings of a unit (manual, from tickets, fuel loads, workshop or GPS).</summary>
public sealed class UnitMeterReading : Entity, ITenantOwned
{
    private UnitMeterReading()
    {
    }

    public UnitMeterReading(Guid unitId, DateTimeOffset recordedAt, decimal? odometer, decimal? hours, MeterSource source, Guid? sourceId, string? notes, string? recordedBy)
    {
        UnitId = unitId;
        RecordedAt = recordedAt;
        Odometer = odometer;
        Hours = hours;
        Source = source;
        SourceId = sourceId;
        Notes = notes;
        RecordedBy = recordedBy;
    }

    public Guid TenantId { get; private set; }

    public Guid UnitId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public decimal? Odometer { get; private set; }

    public decimal? Hours { get; private set; }

    public MeterSource Source { get; private set; }

    public Guid? SourceId { get; private set; }

    public string? Notes { get; private set; }

    public string? RecordedBy { get; private set; }
}

/// <summary>History of status changes of a unit (operating, workshop…); input for utilization.</summary>
public sealed class UnitStatusChange : Entity, ITenantOwned
{
    private UnitStatusChange()
    {
    }

    public UnitStatusChange(Guid unitId, DateTimeOffset changedAt, UnitStatus from, UnitStatus to, string? reason, string? changedBy)
    {
        UnitId = unitId;
        ChangedAt = changedAt;
        From = from;
        To = to;
        Reason = reason;
        ChangedBy = changedBy;
    }

    public Guid TenantId { get; private set; }

    public Guid UnitId { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public UnitStatus From { get; private set; }

    public UnitStatus To { get; private set; }

    public string? Reason { get; private set; }

    public string? ChangedBy { get; private set; }
}

/// <summary>GPS position received from the fleet tracking provider.</summary>
public sealed class UnitPosition : Entity, ITenantOwned
{
    private UnitPosition()
    {
    }

    public UnitPosition(Guid unitId, DateTimeOffset recordedAt, decimal latitude, decimal longitude, decimal? speedKmh, decimal? odometer, string source)
    {
        UnitId = unitId;
        RecordedAt = recordedAt;
        Latitude = latitude;
        Longitude = longitude;
        SpeedKmh = speedKmh;
        Odometer = odometer;
        Source = source;
    }

    public Guid TenantId { get; private set; }

    public Guid UnitId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public decimal Latitude { get; private set; }

    public decimal Longitude { get; private set; }

    public decimal? SpeedKmh { get; private set; }

    public decimal? Odometer { get; private set; }

    public string Source { get; private set; } = null!;
}

public static class UnitErrors
{
    public const string Catalog = "Units";

    public static readonly Error InvalidYear = Error.Validation("Units.InvalidYear", "El año del modelo no es válido.");

    public static readonly Error NegativeValue = Error.Validation("Units.NegativeValue", "Capacidades, lecturas y costos no pueden ser negativos.");

    public static readonly Error MeterBackwards = Error.Validation(
        "Units.MeterBackwards", "La lectura es menor a la actual. Si es una corrección, márcala como tal.");

    public static readonly Error Inactive = Error.Conflict("Units.Inactive", "La unidad está dada de baja.");

    public static readonly Error InWorkshop = Error.Conflict("Units.InWorkshop", "La unidad está en taller.");
}
