using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.ConfigureAggregateRoot();
        builder.Property(t => t.Slug).HasMaxLength(40).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.Name).HasMaxLength(150).IsRequired();
        builder.Property(t => t.LegalName).HasMaxLength(300);
        builder.Property(t => t.TaxId).HasMaxLength(13);
        builder.Property(t => t.ContactEmail).HasMaxLength(254);
        builder.Property(t => t.ContactPhone).HasMaxLength(30);
        builder.Property(t => t.Plan).HasMaxLength(60).IsRequired();
        builder.Property(t => t.StatusReason).HasMaxLength(500);
        builder.HasJson(t => t.Branding);
        builder.HasJson(t => t.Quotas);
        builder.HasJson(t => t.Settings);
        builder.HasJson(t => t.Fiscal);
        builder.HasArray(t => t.EnabledModules, "_enabledModules");
    }
}

internal sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("memberships");
        builder.ConfigureAggregateRoot();
        builder.Property(m => m.IdentityId).HasMaxLength(100);
        builder.Property(m => m.Email).HasMaxLength(254).IsRequired();
        builder.Property(m => m.FullName).HasMaxLength(150).IsRequired();
        builder.HasArray(m => m.RoleIds, "_roleIds");
        builder.HasIndex(m => new { m.TenantId, m.Email }).IsUnique();
        builder.HasIndex(m => new { m.TenantId, m.IdentityId }).IsUnique().HasFilter("identity_id IS NOT NULL");
        builder.HasIndex(m => m.IdentityId);
        builder.HasIndex(m => m.Email);
    }
}

internal sealed class TenantRoleConfiguration : IEntityTypeConfiguration<TenantRole>
{
    public void Configure(EntityTypeBuilder<TenantRole> builder)
    {
        builder.ToTable("roles");
        builder.ConfigureAggregateRoot();
        builder.Property(r => r.Name).HasMaxLength(80).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(300);
        builder.Property(r => r.TemplateKey).HasMaxLength(40);
        builder.HasArray(r => r.Permissions, "_permissions");
        builder.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
    }
}

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("files");
        builder.ConfigureAggregateRoot();
        builder.Property(f => f.FileName).HasMaxLength(200).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(f => f.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(f => f.StorageKey).HasMaxLength(300).IsRequired();
        builder.Property(f => f.OwnerType).HasMaxLength(60);
        builder.HasIndex(f => new { f.TenantId, f.OwnerType, f.OwnerId });
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.ConfigureEntity();
        builder.Property(a => a.UserId).HasMaxLength(100);
        builder.Property(a => a.UserName).HasMaxLength(150).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(300);
        builder.Property(a => a.Changes).HasColumnType("jsonb");
        builder.Property(a => a.Reason).HasMaxLength(1000);
        builder.Property(a => a.CorrelationId).HasMaxLength(100);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.HasIndex(a => new { a.TenantId, a.OccurredAt });
        builder.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
    }
}

internal sealed class FolioSequenceConfiguration : IEntityTypeConfiguration<FolioSequence>
{
    public void Configure(EntityTypeBuilder<FolioSequence> builder)
    {
        builder.ToTable("folio_sequences");
        builder.ConfigureEntity();
        builder.Property(f => f.Key).HasMaxLength(60).IsRequired();
        builder.HasIndex(f => new { f.TenantId, f.Key }).IsUnique();
    }
}

internal sealed class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> builder)
    {
        builder.ToTable("notification_rules");
        builder.ConfigureAggregateRoot();
        builder.HasArray(r => r.RoleIds, "_roleIds");
        builder.HasArray(r => r.Emails, "_emails");
        builder.HasArray(r => r.Phones, "_phones");
        builder.HasIndex(r => new { r.TenantId, r.Event }).IsUnique();
    }
}

internal sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("user_notifications");
        builder.ConfigureEntity();
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(2000).IsRequired();
        builder.Property(n => n.Link).HasMaxLength(300);
        builder.Property(n => n.DeduplicationKey).HasMaxLength(300);
        builder.HasIndex(n => new { n.TenantId, n.MembershipId, n.ReadAt });
        builder.HasIndex(n => new { n.TenantId, n.DeduplicationKey }).HasFilter("deduplication_key IS NOT NULL");
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.ConfigureEntity();
        builder.Property(o => o.Recipient).HasMaxLength(254).IsRequired();
        builder.Property(o => o.Subject).HasMaxLength(250).IsRequired();
        builder.Property(o => o.Body).IsRequired();
        builder.Property(o => o.DeduplicationKey).HasMaxLength(300);
        builder.Property(o => o.TemplateName).HasMaxLength(100);
        builder.Property(o => o.TemplateParameters).HasColumnType("jsonb");
        builder.Property(o => o.LastError).HasMaxLength(1000);
        builder.HasIndex(o => new { o.Status, o.NextAttemptAt });
        builder.HasIndex(o => new { o.TenantId, o.DeduplicationKey }).IsUnique().HasFilter("deduplication_key IS NOT NULL");
    }
}

internal sealed class NotificationChannelSettingsConfiguration : IEntityTypeConfiguration<NotificationChannelSettings>
{
    public void Configure(EntityTypeBuilder<NotificationChannelSettings> builder)
    {
        builder.ToTable("notification_channels");
        builder.ConfigureAggregateRoot();
        builder.Property(c => c.SmtpHost).HasMaxLength(200);
        builder.Property(c => c.SmtpUser).HasMaxLength(200);
        builder.Property(c => c.FromAddress).HasMaxLength(254);
        builder.Property(c => c.FromName).HasMaxLength(150);
        builder.Property(c => c.WhatsAppPhoneNumberId).HasMaxLength(60);
        builder.Property(c => c.WhatsAppTemplate).HasMaxLength(100);
        builder.Property(c => c.WhatsAppLanguage).HasMaxLength(10);
        builder.HasIndex(c => c.TenantId).IsUnique();
    }
}

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.ConfigureAggregateRoot();
        builder.Property(k => k.Name).HasMaxLength(80).IsRequired();
        builder.Property(k => k.Prefix).HasMaxLength(20).IsRequired();
        builder.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(k => k.KeyHash).IsUnique();
        builder.HasArray(k => k.Scopes, "_scopes");
    }
}

internal sealed class AccountingSettingsConfiguration : IEntityTypeConfiguration<AccountingSettings>
{
    public void Configure(EntityTypeBuilder<AccountingSettings> builder)
    {
        builder.ToTable("accounting_settings");
        builder.ConfigureAggregateRoot();
        builder.HasIndex(a => a.TenantId).IsUnique();
    }
}
