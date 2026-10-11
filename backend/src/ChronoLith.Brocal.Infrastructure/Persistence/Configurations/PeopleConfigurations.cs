using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Hse;
using ChronoLith.Brocal.Domain.Inventory;
using ChronoLith.Brocal.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Configurations;

internal sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        builder.ConfigureCatalog("document_types");
        builder.Property(t => t.Authority).HasMaxLength(150);
    }
}

internal sealed class ComplianceDocumentConfiguration : IEntityTypeConfiguration<ComplianceDocument>
{
    public void Configure(EntityTypeBuilder<ComplianceDocument> builder)
    {
        builder.ToTable("compliance_documents");
        builder.ConfigureAggregateRoot();
        builder.Property(d => d.Number).HasMaxLength(100);
        builder.Property(d => d.Issuer).HasMaxLength(200);
        builder.Property(d => d.Hours).HasPrecision(8, 1);
        builder.Property(d => d.Notes).HasMaxLength(1000);
        builder.HasIndex(d => new { d.TenantId, d.Subject, d.SubjectId });
        builder.HasIndex(d => new { d.TenantId, d.ExpiresOn });
        builder.HasIndex(d => d.DocumentTypeId);
    }
}

internal sealed class ComplianceRequirementConfiguration : IEntityTypeConfiguration<ComplianceRequirement>
{
    public void Configure(EntityTypeBuilder<ComplianceRequirement> builder)
    {
        builder.ConfigureCatalog("compliance_requirements");
        builder.HasArray(r => r.EmployeeDocumentTypeIds, "_employeeDocumentTypeIds");
        builder.HasArray(r => r.UnitDocumentTypeIds, "_unitDocumentTypeIds");
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ConfigureCatalog("employees");
        builder.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.LastName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Curp).HasMaxLength(18);
        builder.Property(e => e.Rfc).HasMaxLength(13);
        builder.Property(e => e.Nss).HasMaxLength(15);
        builder.Property(e => e.Position).HasMaxLength(100);
        builder.Property(e => e.Phone).HasMaxLength(30);
        builder.Property(e => e.Email).HasMaxLength(254);
        builder.Property(e => e.EmergencyContact).HasMaxLength(300);
        builder.Property(e => e.BloodType).HasMaxLength(5);
        builder.Property(e => e.LicenseNumber).HasMaxLength(40);
        builder.Property(e => e.LicenseType).HasMaxLength(10);
        builder.Property(e => e.HourlyCost).HasPrecision(12, 2);
        builder.Ignore(e => e.FullName);
    }
}

internal sealed class CrewConfiguration : IEntityTypeConfiguration<Crew>
{
    public void Configure(EntityTypeBuilder<Crew> builder)
    {
        builder.ConfigureCatalog("crews");
        builder.Property(c => c.Shift).HasMaxLength(60);
        builder.HasArray(c => c.MemberIds, "_memberIds");
        builder.HasArray(c => c.UnitIds, "_unitIds");
    }
}

internal sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable("attendance");
        builder.ConfigureAggregateRoot();
        builder.Property(a => a.Hours).HasPrecision(5, 2);
        builder.Property(a => a.OvertimeHours).HasPrecision(5, 2);
        builder.Property(a => a.Latitude).HasPrecision(9, 6);
        builder.Property(a => a.Longitude).HasPrecision(9, 6);
        builder.Property(a => a.Notes).HasMaxLength(500);
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.Date }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.Date });
        builder.HasIndex(a => a.TicketId);
    }
}

internal sealed class ExpenseReportConfiguration : IEntityTypeConfiguration<ExpenseReport>
{
    public void Configure(EntityTypeBuilder<ExpenseReport> builder)
    {
        builder.ToTable("expense_reports");
        builder.ConfigureAggregateRoot();
        builder.Property(r => r.Folio).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Purpose).HasMaxLength(500).IsRequired();
        builder.Property(r => r.AdvanceAmount).HasPrecision(18, 2);
        builder.Property(r => r.Total).HasPrecision(18, 2);
        builder.Property(r => r.Balance).HasPrecision(18, 2);
        builder.Property(r => r.ReviewedBy).HasMaxLength(150);
        builder.Property(r => r.RejectionReason).HasMaxLength(1000);
        builder.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.ReportId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(r => new { r.TenantId, r.FolioNumber }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.EmployeeId });
    }
}

internal sealed class ExpenseItemConfiguration : IEntityTypeConfiguration<ExpenseItem>
{
    public void Configure(EntityTypeBuilder<ExpenseItem> builder)
    {
        builder.ToTable("expense_items");
        builder.ConfigureEntity();
        builder.Property(i => i.Description).HasMaxLength(300).IsRequired();
        builder.Property(i => i.Vendor).HasMaxLength(200);
        builder.Property(i => i.Amount).HasPrecision(18, 2);
        builder.Property(i => i.InvoiceUuid).HasMaxLength(36);
    }
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ConfigureCatalog("warehouses");
        builder.Property(w => w.Location).HasMaxLength(300);
        builder.Property(w => w.Responsible).HasMaxLength(150);
    }
}

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ConfigureCatalog("inventory_items");
        builder.Property(i => i.UnitName).HasMaxLength(30).IsRequired();
        builder.Property(i => i.Brand).HasMaxLength(80);
        builder.Property(i => i.PartNumber).HasMaxLength(80);
        builder.Ignore(i => i.IsTool);
    }
}

internal sealed class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.ToTable("stock_levels");
        builder.ConfigureEntity();
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.HasIndex(s => new { s.TenantId, s.ItemId, s.WarehouseId }).IsUnique();
    }
}

internal sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("inventory_movements");
        builder.ConfigureAggregateRoot();
        builder.Property(m => m.TotalCost).HasPrecision(18, 2);
        builder.Property(m => m.Reference).HasMaxLength(200);
        builder.Property(m => m.Notes).HasMaxLength(1000);
        builder.HasIndex(m => new { m.TenantId, m.ItemId, m.MovedAt });
        builder.HasIndex(m => m.MaintenanceOrderId);
    }
}

internal sealed class ToolAssignmentConfiguration : IEntityTypeConfiguration<ToolAssignment>
{
    public void Configure(EntityTypeBuilder<ToolAssignment> builder)
    {
        builder.ToTable("tool_assignments");
        builder.ConfigureAggregateRoot();
        builder.Property(t => t.SerialNumber).HasMaxLength(80);
        builder.Property(t => t.ReturnCondition).HasMaxLength(300);
        builder.Property(t => t.Notes).HasMaxLength(1000);
        builder.Ignore(t => t.IsOpen);
        builder.HasIndex(t => new { t.TenantId, t.CrewId });
        builder.HasIndex(t => new { t.TenantId, t.EmployeeId });
    }
}

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incidents");
        builder.ConfigureAggregateRoot();
        builder.Property(i => i.Folio).HasMaxLength(30).IsRequired();
        builder.Property(i => i.Location).HasMaxLength(300);
        builder.Property(i => i.Description).HasMaxLength(4000).IsRequired();
        builder.Property(i => i.ImmediateActions).HasMaxLength(4000);
        builder.Property(i => i.RootCause).HasMaxLength(4000);
        builder.Property(i => i.ReportedByName).HasMaxLength(150).IsRequired();
        builder.HasArray(i => i.EmployeeIds, "_employeeIds");
        builder.HasArray(i => i.PhotoFileIds, "_photoFileIds");
        builder.HasJson(i => i.Actions).HasField("_actions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(i => new { i.TenantId, i.FolioNumber }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.OccurredAt });
    }
}

internal sealed class InspectionTemplateConfiguration : IEntityTypeConfiguration<InspectionTemplate>
{
    public void Configure(EntityTypeBuilder<InspectionTemplate> builder)
    {
        builder.ConfigureCatalog("inspection_templates");
        builder.HasJson(t => t.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("inspections");
        builder.ConfigureAggregateRoot();
        builder.Property(i => i.TemplateName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.InspectorName).HasMaxLength(150).IsRequired();
        builder.Property(i => i.Score).HasPrecision(5, 1);
        builder.Property(i => i.Notes).HasMaxLength(2000);
        builder.Ignore(i => i.Passed);
        builder.HasJson(i => i.Answers).HasField("_answers").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(i => new { i.TenantId, i.PerformedAt });
        builder.HasIndex(i => i.UnitId);
    }
}

internal sealed class SafetyTalkConfiguration : IEntityTypeConfiguration<SafetyTalk>
{
    public void Configure(EntityTypeBuilder<SafetyTalk> builder)
    {
        builder.ToTable("safety_talks");
        builder.ConfigureAggregateRoot();
        builder.Property(t => t.Topic).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Instructor).HasMaxLength(150).IsRequired();
        builder.Property(t => t.ExternalAttendees).HasMaxLength(2000);
        builder.Property(t => t.Notes).HasMaxLength(2000);
        builder.HasArray(t => t.AttendeeIds, "_attendeeIds");
        builder.HasArray(t => t.EvidenceFileIds, "_evidenceFileIds");
        builder.HasIndex(t => new { t.TenantId, t.HeldAt });
    }
}
