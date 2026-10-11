using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ConfigureCatalog("customers");
        builder.Property(c => c.LegalName).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Rfc).HasMaxLength(13).IsRequired();
        builder.Property(c => c.TaxRegime).HasMaxLength(3);
        builder.Property(c => c.PostalCode).HasMaxLength(5);
        builder.Property(c => c.CfdiUse).HasMaxLength(4).IsRequired();
        builder.Property(c => c.PaymentForm).HasMaxLength(2).IsRequired();
        builder.Property(c => c.PaymentMethod).HasMaxLength(3).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.Address).HasMaxLength(500);
        builder.Property(c => c.Color).HasMaxLength(7);
        builder.HasJson(c => c.Contacts).HasField("_contacts").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(c => new { c.TenantId, c.Rfc });
    }
}

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ConfigureCatalog("contracts");
        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.CustomerRepresentative).HasMaxLength(200);
        builder.HasIndex(c => new { c.TenantId, c.CustomerId });
    }
}

internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ConfigureCatalog("sites");
        builder.Property(s => s.FieldName).HasMaxLength(150);
        builder.Property(s => s.Municipality).HasMaxLength(150);
        builder.Property(s => s.State).HasMaxLength(100);
        builder.Property(s => s.PostalCode).HasMaxLength(5);
        builder.Property(s => s.Latitude).HasPrecision(9, 6);
        builder.Property(s => s.Longitude).HasPrecision(9, 6);
        builder.HasIndex(s => new { s.TenantId, s.CustomerId });
    }
}

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ConfigureCatalog("services");
        builder.Property(s => s.Description).HasMaxLength(1000);
        builder.Property(s => s.Category).HasMaxLength(100);
        builder.Property(s => s.UnitName).HasMaxLength(30).IsRequired();
        builder.Property(s => s.SatProductCode).HasMaxLength(8).IsRequired();
        builder.Property(s => s.SatUnitCode).HasMaxLength(3).IsRequired();
        builder.Property(s => s.TaxObject).HasMaxLength(2).IsRequired();
        builder.Property(s => s.VatRate).HasPrecision(5, 4);
    }
}

internal sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.ConfigureCatalog("price_lists");
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.PriceListId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(p => new { p.TenantId, p.CustomerId, p.ContractId });
    }
}

internal sealed class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> builder)
    {
        builder.ToTable("price_list_items");
        builder.ConfigureEntity();
        builder.Property(i => i.Notes).HasMaxLength(300);
        builder.HasIndex(i => new { i.PriceListId, i.ServiceId }).IsUnique();
        builder.HasIndex(i => i.ServiceId);
    }
}

internal sealed class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ConfigureCatalog("service_types");
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.Icon).HasMaxLength(60).IsRequired();
        builder.Property(t => t.Color).HasMaxLength(7);
        builder.HasJson(t => t.Fields).HasField("_fields").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasArray(t => t.DefaultServiceIds, "_defaultServiceIds");
    }
}

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("quotes");
        builder.ConfigureAggregateRoot();
        builder.Property(q => q.Folio).HasMaxLength(30).IsRequired();
        builder.Property(q => q.Currency).HasMaxLength(3).IsRequired();
        builder.Property(q => q.Subject).HasMaxLength(300);
        builder.Property(q => q.Terms).HasMaxLength(4000);
        builder.Property(q => q.Notes).HasMaxLength(2000);
        builder.Property(q => q.RejectionReason).HasMaxLength(1000);
        builder.HasMany(q => q.Lines).WithOne().HasForeignKey(l => l.QuoteId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(q => q.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(q => new { q.TenantId, q.FolioNumber }).IsUnique();
        builder.HasIndex(q => new { q.TenantId, q.CustomerId });
    }
}

internal sealed class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine>
{
    public void Configure(EntityTypeBuilder<QuoteLine> builder)
    {
        builder.ToTable("quote_lines");
        builder.ConfigureEntity();
        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.UnitName).HasMaxLength(30).IsRequired();
        builder.Property(l => l.VatRate).HasPrecision(5, 4);
        builder.HasIndex(l => l.ServiceId);
    }
}

internal sealed class ServiceTicketConfiguration : IEntityTypeConfiguration<ServiceTicket>
{
    public void Configure(EntityTypeBuilder<ServiceTicket> builder)
    {
        builder.ToTable("tickets");
        builder.ConfigureAggregateRoot();
        builder.Property(t => t.Folio).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(4000);
        builder.Property(t => t.Observations).HasMaxLength(4000);
        builder.Property(t => t.CustomerRepresentative).HasMaxLength(200);
        builder.Property(t => t.CapturedByName).HasMaxLength(150);
        builder.Property(t => t.SignerName).HasMaxLength(150);
        builder.Property(t => t.SignerPosition).HasMaxLength(150);
        builder.Property(t => t.ApprovedBy).HasMaxLength(150);
        builder.Property(t => t.CancellationReason).HasMaxLength(1000);
        builder.Property(t => t.CaptureLatitude).HasPrecision(9, 6);
        builder.Property(t => t.CaptureLongitude).HasPrecision(9, 6);
        builder.Property(t => t.SignedLatitude).HasPrecision(9, 6);
        builder.Property(t => t.SignedLongitude).HasPrecision(9, 6);
        builder.Property(t => t.Subtotal).HasPrecision(18, 2);
        builder.Property(t => t.Vat).HasPrecision(18, 2);
        builder.Property(t => t.Total).HasPrecision(18, 2);
        builder.HasArray(t => t.UnitIds, "_unitIds");
        builder.HasArray(t => t.EmployeeIds, "_employeeIds");
        builder.HasJson(t => t.Answers).HasField("_answers").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(t => t.Photos).WithOne().HasForeignKey(p => p.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Photos).HasField("_photos").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(t => new { t.TenantId, t.FolioNumber }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.ClientId }).IsUnique().HasFilter("client_id IS NOT NULL");
        builder.HasIndex(t => new { t.TenantId, t.ServiceDate });
        builder.HasIndex(t => new { t.TenantId, t.Status });
        builder.HasIndex(t => new { t.TenantId, t.CustomerId });
        builder.HasIndex(t => t.WorkOrderId);
        builder.HasIndex(t => t.InvoiceId);
    }
}

internal sealed class TicketLineConfiguration : IEntityTypeConfiguration<TicketLine>
{
    public void Configure(EntityTypeBuilder<TicketLine> builder)
    {
        builder.ToTable("ticket_lines");
        builder.ConfigureEntity();
        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.UnitName).HasMaxLength(30).IsRequired();
        builder.Property(l => l.VatRate).HasPrecision(5, 4);
        builder.Property(l => l.Amount).HasPrecision(18, 2);
        builder.Ignore(l => l.Vat);
        builder.HasIndex(l => l.ServiceId);
    }
}

internal sealed class TicketPhotoConfiguration : IEntityTypeConfiguration<TicketPhoto>
{
    public void Configure(EntityTypeBuilder<TicketPhoto> builder)
    {
        builder.ToTable("ticket_photos");
        builder.ConfigureEntity();
        builder.Property(p => p.Caption).HasMaxLength(300);
        builder.Property(p => p.Latitude).HasPrecision(9, 6);
        builder.Property(p => p.Longitude).HasPrecision(9, 6);
    }
}

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders");
        builder.ConfigureAggregateRoot();
        builder.Property(w => w.Folio).HasMaxLength(30).IsRequired();
        builder.Property(w => w.Description).HasMaxLength(2000).IsRequired();
        builder.Property(w => w.Instructions).HasMaxLength(4000);
        builder.Property(w => w.CustomerReference).HasMaxLength(100);
        builder.Property(w => w.CancellationReason).HasMaxLength(1000);
        builder.HasArray(w => w.UnitIds, "_unitIds");
        builder.HasArray(w => w.EmployeeIds, "_employeeIds");
        builder.HasJson(w => w.PlannedServices).HasField("_plannedServices").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(w => new { w.TenantId, w.FolioNumber }).IsUnique();
        builder.HasIndex(w => new { w.TenantId, w.ScheduledStart });
        builder.HasIndex(w => new { w.TenantId, w.Status });
    }
}
