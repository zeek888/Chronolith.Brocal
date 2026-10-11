using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;

namespace ChronoLith.Brocal.Domain.Fleet;

public enum FuelPaymentMethod
{
    Cash = 0,
    Card = 1,
    Voucher = 2,
    Credit = 3
}

public sealed record FuelLoadData(
    Guid UnitId,
    DateTimeOffset LoadedAt,
    decimal Liters,
    decimal PricePerLiter,
    decimal? Odometer,
    decimal? Hours,
    bool IsFullTank,
    string? Station,
    FuelPaymentMethod PaymentMethod,
    Guid? DriverId,
    Guid? WorkOrderId,
    Guid? ReceiptFileId,
    string? Notes);

/// <summary>
/// Carga de combustible. When it closes a full-tank cycle the efficiency of the cycle is computed (km/l or l/h)
/// and compared with the expected efficiency of the unit; deviations beyond the tolerance are flagged.
/// </summary>
public sealed class FuelLoad : TenantAggregateRoot
{
    private FuelLoad()
    {
    }

    public Guid UnitId { get; private set; }

    public DateTimeOffset LoadedAt { get; private set; }

    public decimal Liters { get; private set; }

    public decimal PricePerLiter { get; private set; }

    public decimal Amount { get; private set; }

    public decimal? Odometer { get; private set; }

    public decimal? Hours { get; private set; }

    public bool IsFullTank { get; private set; }

    public string? Station { get; private set; }

    public FuelPaymentMethod PaymentMethod { get; private set; }

    public Guid? DriverId { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public Guid? ReceiptFileId { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Efficiency of the full-tank cycle that ends with this load.</summary>
    public decimal? Efficiency { get; private set; }

    /// <summary>"km/l" or "l/h".</summary>
    public string? EfficiencyUnit { get; private set; }

    /// <summary>Percentage difference against the expected efficiency (negative = worse).</summary>
    public decimal? DeviationPercent { get; private set; }

    public bool IsAnomaly { get; private set; }

    public static Result<FuelLoad> Create(FuelLoadData data)
    {
        var load = new FuelLoad();
        var result = load.Update(data);
        return result.IsFailure ? result.Error : load;
    }

    public Result Update(FuelLoadData data)
    {
        if (data.Liters <= 0 || data.Liters > 5000)
        {
            return FuelErrors.InvalidLiters;
        }

        if (data.PricePerLiter < 0 || data.Odometer is < 0 || data.Hours is < 0)
        {
            return UnitErrors.NegativeValue;
        }

        UnitId = data.UnitId;
        LoadedAt = data.LoadedAt;
        Liters = data.Liters;
        PricePerLiter = data.PricePerLiter;
        Amount = Money.Round(data.Liters * data.PricePerLiter);
        Odometer = data.Odometer;
        Hours = data.Hours;
        IsFullTank = data.IsFullTank;
        Station = string.IsNullOrWhiteSpace(data.Station) ? null : data.Station.Trim();
        PaymentMethod = data.PaymentMethod;
        DriverId = data.DriverId;
        WorkOrderId = data.WorkOrderId;
        ReceiptFileId = data.ReceiptFileId;
        Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();
        return Result.Success();
    }

    /// <summary>
    /// Computes the efficiency of the cycle between the previous full tank and this one.
    /// <paramref name="litersSincePrevious"/> are the liters loaded after the previous full tank, this load included.
    /// </summary>
    public void Evaluate(Unit unit, FuelLoad? previousFullTank, decimal litersSincePrevious, decimal tolerancePercent)
    {
        Efficiency = null;
        EfficiencyUnit = null;
        DeviationPercent = null;
        IsAnomaly = false;

        if (!IsFullTank || previousFullTank is null || litersSincePrevious <= 0)
        {
            return;
        }

        if (unit.UsesOdometer && Odometer is not null && previousFullTank.Odometer is not null && Odometer > previousFullTank.Odometer)
        {
            Efficiency = Math.Round((Odometer.Value - previousFullTank.Odometer.Value) / litersSincePrevious, 2);
            EfficiencyUnit = "km/l";
            if (unit.ExpectedEfficiency is > 0)
            {
                // Fewer km per liter than expected is worse.
                DeviationPercent = Math.Round((Efficiency.Value - unit.ExpectedEfficiency.Value) / unit.ExpectedEfficiency.Value * 100, 1);
            }
        }
        else if (unit.UsesHourMeter && Hours is not null && previousFullTank.Hours is not null && Hours > previousFullTank.Hours)
        {
            Efficiency = Math.Round(litersSincePrevious / (Hours.Value - previousFullTank.Hours.Value), 2);
            EfficiencyUnit = "l/h";
            if (unit.ExpectedEfficiency is > 0)
            {
                // More liters per hour than expected is worse.
                DeviationPercent = Math.Round((unit.ExpectedEfficiency.Value - Efficiency.Value) / unit.ExpectedEfficiency.Value * 100, 1);
            }
        }

        IsAnomaly = DeviationPercent is not null && DeviationPercent < -tolerancePercent;
    }
}

public static class FuelErrors
{
    public static readonly Error NotFound = Error.NotFound("Fuel.NotFound", "La carga de combustible no existe.");

    public static readonly Error InvalidLiters = Error.Validation("Fuel.InvalidLiters", "Los litros deben ser mayores a cero (máximo 5000).");

    public static readonly Error MeterRequired = Error.Validation("Fuel.MeterRequired", "Captura el odómetro u horómetro de la unidad al cargar.");
}
