using ChronoLith.Brocal.Domain.Fiscal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.ConfigureAggregateRoot();
        builder.Property(i => i.Series).HasMaxLength(10).IsRequired();
        builder.Property(i => i.PaymentForm).HasMaxLength(2).IsRequired();
        builder.Property(i => i.PaymentMethod).HasMaxLength(3).IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.ExchangeRate).HasPrecision(12, 6);
        builder.Property(i => i.CopadeNumber).HasMaxLength(40);
        builder.Property(i => i.Uuid).HasMaxLength(36);
        builder.Property(i => i.PacProvider).HasMaxLength(60);
        builder.Property(i => i.CancellationMotive).HasMaxLength(2);
        builder.Property(i => i.SubstitutionUuid).HasMaxLength(36);
        builder.Property(i => i.Notes).HasMaxLength(2000);
        foreach (var amount in new[] { nameof(Invoice.Subtotal), nameof(Invoice.Discount), nameof(Invoice.Vat), nameof(Invoice.Total), nameof(Invoice.PaidAmount), nameof(Invoice.Balance) })
        {
            builder.Property(amount).HasPrecision(18, 2);
        }

        builder.HasJson(i => i.Receiver);
        builder.HasJson(i => i.Issuer, required: false);
        builder.HasArray(i => i.TicketIds, "_ticketIds");
        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(i => new { i.TenantId, i.Series, i.Folio }).IsUnique().HasFilter("folio IS NOT NULL");
        builder.HasIndex(i => i.Uuid);
        builder.HasIndex(i => new { i.TenantId, i.CustomerId, i.Status });
        builder.HasIndex(i => new { i.TenantId, i.DueOn });
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines");
        builder.ConfigureEntity();
        builder.Property(l => l.SatProductCode).HasMaxLength(8).IsRequired();
        builder.Property(l => l.SatUnitCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.UnitName).HasMaxLength(30).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(1000).IsRequired();
        builder.Property(l => l.TaxObject).HasMaxLength(2).IsRequired();
        builder.Property(l => l.VatRate).HasPrecision(5, 4);
        builder.Property(l => l.Amount).HasPrecision(18, 2);
        builder.Property(l => l.Discount).HasPrecision(18, 2);
        builder.Property(l => l.Vat).HasPrecision(18, 2);
        builder.HasIndex(l => l.TicketId);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.ConfigureAggregateRoot();
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.PaymentForm).HasMaxLength(2).IsRequired();
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.ComplementSeries).HasMaxLength(10);
        builder.Property(p => p.ComplementUuid).HasMaxLength(36);
        builder.HasMany(p => p.Applications).WithOne().HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Applications).HasField("_applications").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(p => new { p.TenantId, p.CustomerId, p.PaidAt });
    }
}

internal sealed class PaymentApplicationConfiguration : IEntityTypeConfiguration<PaymentApplication>
{
    public void Configure(EntityTypeBuilder<PaymentApplication> builder)
    {
        builder.ToTable("payment_applications");
        builder.ConfigureEntity();
        builder.Property(a => a.Amount).HasPrecision(18, 2);
        builder.Property(a => a.PreviousBalance).HasPrecision(18, 2);
        builder.Property(a => a.RemainingBalance).HasPrecision(18, 2);
        builder.HasIndex(a => a.InvoiceId);
    }
}

internal sealed class EstimateConfiguration : IEntityTypeConfiguration<Estimate>
{
    public void Configure(EntityTypeBuilder<Estimate> builder)
    {
        builder.ToTable("estimates");
        builder.ConfigureAggregateRoot();
        builder.Property(e => e.Folio).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.CopadeNumber).HasMaxLength(40);
        builder.Property(e => e.RejectionReason).HasMaxLength(1000);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.HasArray(e => e.TicketIds, "_ticketIds");
        builder.HasIndex(e => new { e.TenantId, e.FolioNumber }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.CustomerId, e.Status });
    }
}

internal sealed class CollectionNoteConfiguration : IEntityTypeConfiguration<CollectionNote>
{
    public void Configure(EntityTypeBuilder<CollectionNote> builder)
    {
        builder.ToTable("collection_notes");
        builder.ConfigureAggregateRoot();
        builder.Property(n => n.ContactName).HasMaxLength(150);
        builder.Property(n => n.Comment).HasMaxLength(2000).IsRequired();
        builder.HasIndex(n => new { n.TenantId, n.CustomerId, n.ContactedAt });
    }
}

internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("trips");
        builder.ConfigureAggregateRoot();
        builder.Property(t => t.Folio).HasMaxLength(30).IsRequired();
        builder.Property(t => t.IdCcp).HasMaxLength(36).IsRequired();
        builder.Property(t => t.DistanceKm).HasPrecision(10, 2);
        builder.Property(t => t.TotalWeightKg).HasPrecision(14, 3);
        builder.Property(t => t.FreightAmount).HasPrecision(18, 2);
        builder.Property(t => t.Uuid).HasMaxLength(36);
        builder.Property(t => t.Notes).HasMaxLength(2000);
        builder.Property(t => t.CancellationReason).HasMaxLength(1000);
        builder.HasJson(t => t.Origin);
        builder.HasJson(t => t.Destination);
        builder.HasJson(t => t.Goods).HasField("_goods").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(t => t.HasHazardousGoods);
        builder.HasIndex(t => new { t.TenantId, t.FolioNumber }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.UnitId });
    }
}
