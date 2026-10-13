using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Configurations;

/// <summary>Company and global workflows (tenant_id null): no tenant query filter, the application filters by scope.</summary>
internal sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("workflow_definitions");
        builder.ConfigureAggregateRoot();
        builder.Property(d => d.Name).HasMaxLength(120).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(500);
        builder.HasJson(d => d.Conditions).HasField("_conditions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasJson(d => d.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(d => new { d.TenantId, d.Document });
    }
}

internal sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("workflow_instances");
        builder.ConfigureAggregateRoot();
        builder.Property(i => i.DefinitionName).HasMaxLength(120).IsRequired();
        builder.Property(i => i.DocumentReference).HasMaxLength(200).IsRequired();
        builder.Property(i => i.FactsHash).HasMaxLength(64).IsRequired();
        builder.Property(i => i.RequestedByName).HasMaxLength(150).IsRequired();
        builder.Property(i => i.ClosingComment).HasMaxLength(1000);
        builder.HasJson(i => i.Facts).HasField("_facts").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasJson(i => i.Conditions).HasField("_conditions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasJson(i => i.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasJson(i => i.Decisions).HasField("_decisions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(i => new { i.TenantId, i.Document, i.DocumentId });
        builder.HasIndex(i => new { i.TenantId, i.Status, i.RequestedAt });
        builder.HasIndex(i => new { i.TenantId, i.RequestedById });
    }
}

/// <summary>Company and global rules (tenant_id null), like <see cref="WorkflowDefinitionConfiguration"/>.</summary>
internal sealed class BusinessRuleConfiguration : IEntityTypeConfiguration<BusinessRule>
{
    public void Configure(EntityTypeBuilder<BusinessRule> builder)
    {
        builder.ToTable("business_rules");
        builder.ConfigureAggregateRoot();
        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.Message).HasMaxLength(500).IsRequired();
        builder.HasJson(r => r.Triggers).HasField("_triggers").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasJson(r => r.Conditions).HasField("_conditions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasJson(r => r.Recipients);
        builder.HasIndex(r => new { r.TenantId, r.Document });
    }
}

internal sealed class AutomationOptOutConfiguration : IEntityTypeConfiguration<AutomationOptOut>
{
    public void Configure(EntityTypeBuilder<AutomationOptOut> builder)
    {
        builder.ToTable("automation_opt_outs");
        builder.ConfigureEntity();
        builder.Property(o => o.ChangedBy).HasMaxLength(150).IsRequired();
        builder.HasIndex(o => new { o.TenantId, o.Kind, o.DefinitionId }).IsUnique();
    }
}

internal sealed class ScheduledJobRunConfiguration : IEntityTypeConfiguration<ScheduledJobRun>
{
    public void Configure(EntityTypeBuilder<ScheduledJobRun> builder)
    {
        builder.ToTable("scheduled_job_runs");
        builder.ConfigureEntity();
        builder.Property(r => r.Job).HasMaxLength(40).IsRequired();
        builder.Property(r => r.TriggeredBy).HasMaxLength(150).IsRequired();
        builder.Property(r => r.Details).HasColumnType("jsonb");
        builder.HasIndex(r => r.StartedAt);
    }
}
