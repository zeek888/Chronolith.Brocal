using ChronoLith.Brocal.Domain.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Configurations;

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ConfigureCatalog("units");
        builder.Property(u => u.Brand).HasMaxLength(80);
        builder.Property(u => u.Model).HasMaxLength(80);
        builder.Property(u => u.Plates).HasMaxLength(20);
        builder.Property(u => u.SerialNumber).HasMaxLength(40);
        builder.Property(u => u.CapacityUnit).HasMaxLength(20);
        builder.Property(u => u.FuelType).HasMaxLength(30);
        builder.Property(u => u.VehicleConfig).HasMaxLength(10);
        builder.Property(u => u.SctPermitType).HasMaxLength(10);
        builder.Property(u => u.SctPermitNumber).HasMaxLength(60);
        builder.Property(u => u.InsurerName).HasMaxLength(150);
        builder.Property(u => u.InsurancePolicy).HasMaxLength(60);
        builder.Property(u => u.EnvironmentalInsurer).HasMaxLength(150);
        builder.Property(u => u.EnvironmentalPolicy).HasMaxLength(60);
        builder.Property(u => u.CurrentOdometer).HasPrecision(12, 1);
        builder.Property(u => u.CurrentHours).HasPrecision(12, 1);
        builder.Property(u => u.LastLatitude).HasPrecision(9, 6);
        builder.Property(u => u.LastLongitude).HasPrecision(9, 6);
        builder.HasIndex(u => new { u.TenantId, u.Status });
    }
}

internal sealed class UnitMeterReadingConfiguration : IEntityTypeConfiguration<UnitMeterReading>
{
    public void Configure(EntityTypeBuilder<UnitMeterReading> builder)
    {
        builder.ToTable("unit_meter_readings");
        builder.ConfigureEntity();
        builder.Property(r => r.Odometer).HasPrecision(12, 1);
        builder.Property(r => r.Hours).HasPrecision(12, 1);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.Property(r => r.RecordedBy).HasMaxLength(150);
        builder.HasIndex(r => new { r.TenantId, r.UnitId, r.RecordedAt });
    }
}

internal sealed class UnitStatusChangeConfiguration : IEntityTypeConfiguration<UnitStatusChange>
{
    public void Configure(EntityTypeBuilder<UnitStatusChange> builder)
    {
        builder.ToTable("unit_status_changes");
        builder.ConfigureEntity();
        builder.Property(c => c.Reason).HasMaxLength(500);
        builder.Property(c => c.ChangedBy).HasMaxLength(150);
        builder.HasIndex(c => new { c.TenantId, c.UnitId, c.ChangedAt });
    }
}

internal sealed class UnitPositionConfiguration : IEntityTypeConfiguration<UnitPosition>
{
    public void Configure(EntityTypeBuilder<UnitPosition> builder)
    {
        builder.ToTable("unit_positions");
        builder.ConfigureEntity();
        builder.Property(p => p.Latitude).HasPrecision(9, 6);
        builder.Property(p => p.Longitude).HasPrecision(9, 6);
        builder.Property(p => p.SpeedKmh).HasPrecision(7, 2);
        builder.Property(p => p.Odometer).HasPrecision(12, 1);
        builder.Property(p => p.Source).HasMaxLength(60).IsRequired();
        builder.HasIndex(p => new { p.TenantId, p.UnitId, p.RecordedAt });
    }
}

internal sealed class MaintenancePlanConfiguration : IEntityTypeConfiguration<MaintenancePlan>
{
    public void Configure(EntityTypeBuilder<MaintenancePlan> builder)
    {
        builder.ConfigureCatalog("maintenance_plans");
        builder.Property(p => p.Interval).HasPrecision(12, 1);
        builder.Property(p => p.WarningBefore).HasPrecision(12, 1);
        builder.HasArray(p => p.Tasks, "_tasks");
        builder.HasMany(p => p.Units).WithOne().HasForeignKey(u => u.PlanId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Units).HasField("_units").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class MaintenancePlanUnitConfiguration : IEntityTypeConfiguration<MaintenancePlanUnit>
{
    public void Configure(EntityTypeBuilder<MaintenancePlanUnit> builder)
    {
        builder.ToTable("maintenance_plan_units");
        builder.ConfigureEntity();
        builder.Property(u => u.LastOdometer).HasPrecision(12, 1);
        builder.Property(u => u.LastHours).HasPrecision(12, 1);
        builder.HasIndex(u => new { u.PlanId, u.UnitId }).IsUnique();
        builder.HasIndex(u => u.UnitId);
    }
}

internal sealed class MaintenanceOrderConfiguration : IEntityTypeConfiguration<MaintenanceOrder>
{
    public void Configure(EntityTypeBuilder<MaintenanceOrder> builder)
    {
        builder.ToTable("maintenance_orders");
        builder.ConfigureAggregateRoot();
        builder.Property(o => o.Folio).HasMaxLength(30).IsRequired();
        builder.Property(o => o.ReportedFailure).HasMaxLength(2000);
        builder.Property(o => o.Diagnosis).HasMaxLength(2000);
        builder.Property(o => o.WorkPerformed).HasMaxLength(4000);
        builder.Property(o => o.Technicians).HasMaxLength(300);
        builder.Property(o => o.ExternalProvider).HasMaxLength(200);
        builder.Property(o => o.ExternalInvoice).HasMaxLength(60);
        builder.Property(o => o.Notes).HasMaxLength(2000);
        builder.Property(o => o.CancellationReason).HasMaxLength(1000);
        builder.Property(o => o.OdometerAtService).HasPrecision(12, 1);
        builder.Property(o => o.HoursAtService).HasPrecision(12, 1);
        builder.Property(o => o.PartsCost).HasPrecision(18, 2);
        builder.Property(o => o.LaborCost).HasPrecision(18, 2);
        builder.Property(o => o.ExternalCost).HasPrecision(18, 2);
        builder.Property(o => o.TotalCost).HasPrecision(18, 2);
        builder.HasJson(o => o.Tasks).HasField("_tasks").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(o => o.Parts).WithOne().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Parts).HasField("_parts").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(o => new { o.TenantId, o.FolioNumber }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.UnitId, o.Status });
    }
}

internal sealed class MaintenancePartConfiguration : IEntityTypeConfiguration<MaintenancePart>
{
    public void Configure(EntityTypeBuilder<MaintenancePart> builder)
    {
        builder.ToTable("maintenance_parts");
        builder.ConfigureEntity();
        builder.Property(p => p.Description).HasMaxLength(300).IsRequired();
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.HasIndex(p => p.ItemId);
    }
}

internal sealed class FuelLoadConfiguration : IEntityTypeConfiguration<FuelLoad>
{
    public void Configure(EntityTypeBuilder<FuelLoad> builder)
    {
        builder.ToTable("fuel_loads");
        builder.ConfigureAggregateRoot();
        builder.Property(f => f.Liters).HasPrecision(10, 2);
        builder.Property(f => f.Amount).HasPrecision(18, 2);
        builder.Property(f => f.Odometer).HasPrecision(12, 1);
        builder.Property(f => f.Hours).HasPrecision(12, 1);
        builder.Property(f => f.Station).HasMaxLength(200);
        builder.Property(f => f.Notes).HasMaxLength(1000);
        builder.Property(f => f.Efficiency).HasPrecision(10, 2);
        builder.Property(f => f.EfficiencyUnit).HasMaxLength(10);
        builder.Property(f => f.DeviationPercent).HasPrecision(7, 1);
        builder.HasIndex(f => new { f.TenantId, f.UnitId, f.LoadedAt });
    }
}
