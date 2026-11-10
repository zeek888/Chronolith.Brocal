using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Configurations;

/// <summary>Outbox of the indicators published to the Shell (not <c>ITenantOwned</c>: a well adds up every company).</summary>
internal sealed class IndicatorMessageConfiguration : IEntityTypeConfiguration<IndicatorMessage>
{
    public void Configure(EntityTypeBuilder<IndicatorMessage> builder)
    {
        builder.ToTable("indicator_messages");
        builder.ConfigureEntity();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(IndicatorMessage.MaxErrorLength);
        builder.HasIndex(m => new { m.Status, m.Kind, m.CreatedAt });
        builder.HasIndex(m => m.CreatedAt);
    }
}

/// <summary>Subjects published to (or retired from) the Shell: the id is the well or facility of the Shell.</summary>
internal sealed class IndicatorSubjectConfiguration : IEntityTypeConfiguration<IndicatorSubject>
{
    public void Configure(EntityTypeBuilder<IndicatorSubject> builder)
    {
        builder.ToTable("indicator_subjects");
        builder.ConfigureEntity();
        builder.Property(s => s.SubjectType).HasMaxLength(IndicatorSubject.MaxTypeLength).IsRequired();
    }
}
