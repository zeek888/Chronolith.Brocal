using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Fleet;

namespace ChronoLith.Brocal.Domain.UnitTests;

public sealed class FleetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    private static Unit NewUnit(MeterKind meters = MeterKind.Odometer, decimal? expected = 2.0m) =>
        Unit.Create(new UnitData("AT-01", "Autotanque 20 m³", UnitType.TankTruck, "Kenworth", "T800", 2019, "XA-1234-A", null, meters, 20, "m3", "Diésel", 400, expected,
            "C3", 30, "TPAF01", "SCT-123", null, null, null, null, null, null, null, null, null)).Value;

    private static FuelLoad Load(decimal liters, decimal? odometer, decimal? hours = null, bool full = true) =>
        FuelLoad.Create(new FuelLoadData(Guid.NewGuid(), Now, liters, 24.35m, odometer, hours, full, null, FuelPaymentMethod.Card, null, null, null, null)).Value;

    [Fact]
    public void Odometer_cannot_go_backwards_except_as_a_correction()
    {
        var unit = NewUnit();
        Assert.True(unit.RecordMeters(1_000, null, Now).IsSuccess);

        Assert.Equal(UnitErrors.MeterBackwards, unit.RecordMeters(900, null, Now).Error);
        Assert.True(unit.RecordMeters(900, null, Now, isCorrection: true).IsSuccess);
        Assert.Equal(900, unit.CurrentOdometer);
    }

    [Fact]
    public void Full_tank_cycles_compute_km_per_liter_and_flag_anomalies()
    {
        var unit = NewUnit(expected: 2.0m);
        var previous = Load(300, 10_000);
        var current = Load(250, 10_500);

        current.Evaluate(unit, previous, 250, tolerancePercent: 15);

        Assert.Equal(2.0m, current.Efficiency);
        Assert.Equal("km/l", current.EfficiencyUnit);
        Assert.False(current.IsAnomaly);

        var bad = Load(250, 10_350);
        bad.Evaluate(unit, previous, 250, tolerancePercent: 15);
        Assert.Equal(1.4m, bad.Efficiency);
        Assert.Equal(-30.0m, bad.DeviationPercent);
        Assert.True(bad.IsAnomaly);
    }

    [Fact]
    public void Pump_units_measure_liters_per_hour()
    {
        var unit = NewUnit(MeterKind.HourMeter, expected: 20m);
        var current = Load(120, null, hours: 506);

        current.Evaluate(unit, Load(200, null, hours: 500), 120, tolerancePercent: 15);

        Assert.Equal(20m, current.Efficiency);
        Assert.Equal("l/h", current.EfficiencyUnit);
        Assert.False(current.IsAnomaly);
    }

    [Fact]
    public void Partial_loads_do_not_compute_efficiency()
    {
        var partial = Load(80, 10_200, full: false);

        partial.Evaluate(NewUnit(), Load(300, 10_000), 80, 15);

        Assert.Null(partial.Efficiency);
    }
}

public sealed class InvoiceTests
{
    private static readonly InvoiceReceiver Pemex = new("PEP920716AB1", "PEMEX EXPLORACION Y PRODUCCION", "603", "86030", "G03");

    private static InvoiceDraftData Draft(string method = "PPD", string form = "99", CfdiType type = CfdiType.Income) =>
        new(Guid.NewGuid(), Pemex, type, form, method, "MXN", 1, new DateOnly(2026, 10, 1), 60,
            [new InvoiceLineData(null, null, "78101802", "E48", "Servicio", "Transporte de agua congénita", 120, 168.5m, 0, "02", 0.16m)], null, null, null, "COP-1", null);

    [Fact]
    public void Draft_totals_and_due_date_follow_the_credit_terms()
    {
        var invoice = Invoice.CreateDraft("A", Draft()).Value;

        Assert.Equal(20_220.00m, invoice.Subtotal);
        Assert.Equal(3_235.20m, invoice.Vat);
        Assert.Equal(23_455.20m, invoice.Total);
        Assert.Equal(new DateOnly(2026, 11, 30), invoice.DueOn);
        Assert.Equal(new DateOnly(2026, 10, 1), Invoice.CreateDraft("A", Draft("PUE", "03")).Value.DueOn);
    }

    [Fact]
    public void Ppd_invoices_must_use_payment_form_99()
    {
        Assert.Equal(InvoiceErrors.PpdRequires99, Invoice.CreateDraft("A", Draft("PPD", "03")).Error);
    }

    [Fact]
    public void Payments_reduce_the_balance_and_cannot_exceed_it()
    {
        var invoice = Invoice.CreateDraft("A", Draft()).Value;
        invoice.MarkStamped(7, new InvoiceIssuer("SPS120315AB1", "SERVICIOS PETROLEROS", "601", "86035"), "uuid", DateTimeOffset.UtcNow, "Simulado", Guid.NewGuid());

        Assert.True(invoice.ApplyPayment(10_000, DateTimeOffset.UtcNow).IsSuccess);
        Assert.Equal(13_455.20m, invoice.Balance);
        Assert.Equal("UUID", invoice.Uuid);
        Assert.True(invoice.ApplyPayment(20_000, DateTimeOffset.UtcNow).IsFailure);
    }

    [Fact]
    public void Cancellation_with_relation_requires_the_substitute_and_no_payments()
    {
        var invoice = Invoice.CreateDraft("A", Draft()).Value;
        invoice.MarkStamped(8, new InvoiceIssuer("SPS120315AB1", "SERVICIOS PETROLEROS", "601", "86035"), "uuid", DateTimeOffset.UtcNow, "Simulado", Guid.NewGuid());

        Assert.Equal(InvoiceErrors.SubstitutionRequired, invoice.RequestCancellation("01", null, DateTimeOffset.UtcNow).Error);

        invoice.ApplyPayment(100, DateTimeOffset.UtcNow);
        Assert.Equal(InvoiceErrors.HasPayments, invoice.RequestCancellation("02", null, DateTimeOffset.UtcNow).Error);
    }

    [Fact]
    public void Transfer_cfdi_has_no_amount_to_collect()
    {
        var invoice = Invoice.CreateDraft("T", Draft(type: CfdiType.Transfer)).Value;
        invoice.MarkStamped(1, new InvoiceIssuer("SPS120315AB1", "SERVICIOS PETROLEROS", "601", "86035"), "uuid", DateTimeOffset.UtcNow, "Simulado", Guid.NewGuid());

        Assert.Equal(0, invoice.Total);
        Assert.Equal(0, invoice.Balance);
    }
}
