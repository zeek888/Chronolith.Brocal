using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Multi-tenant guard: new rows receive the company of the request and no write may touch another company's rows.
/// Platform operations act inside a company with <see cref="ITenantContext.BeginScope"/>.
/// </summary>
internal sealed class TenantInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            var tenantProperty = entry.Property(nameof(ITenantOwned.TenantId));
            var current = (Guid)tenantProperty.CurrentValue!;

            switch (entry.State)
            {
                case EntityState.Added when current == Guid.Empty:
                    if (!tenantContext.HasTenant)
                    {
                        throw new InvalidOperationException($"No hay empresa en contexto para guardar {entry.Metadata.ClrType.Name}.");
                    }

                    tenantProperty.CurrentValue = tenantContext.TenantId;
                    break;

                case EntityState.Added or EntityState.Modified or EntityState.Deleted when tenantContext.HasTenant && current != tenantContext.TenantId:
                    throw new InvalidOperationException($"Escritura entre empresas bloqueada ({entry.Metadata.ClrType.Name}).");
            }
        }
    }
}

/// <summary>Stamps audit columns. A change in a child collection also touches the aggregate root.</summary>
internal sealed class AuditableEntityInterceptor(IUserContext userContext, TimeProvider timeProvider) : SaveChangesInterceptor
{
    private const string SystemUser = "system";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var user = userContext.IsAuthenticated ? userContext.DisplayName : SystemUser;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(IAuditable.CreatedAt)).CurrentValue = now;
                entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue = user;
            }
            else if (entry.State == EntityState.Modified || HasChangedChildren(entry))
            {
                entry.Property(nameof(IAuditable.UpdatedAt)).CurrentValue = now;
                entry.Property(nameof(IAuditable.UpdatedBy)).CurrentValue = user;
            }
        }
    }

    private static bool HasChangedChildren(EntityEntry entry) =>
        entry.State == EntityState.Unchanged && entry.Navigations.Any(n =>
            n.IsModified ||
            n is CollectionEntry { CurrentValue: not null } collection && collection.CurrentValue.Cast<object>().Any(child =>
                entry.Context.Entry(child).State is EntityState.Added or EntityState.Modified or EntityState.Deleted));
}

/// <summary>
/// Bitácora automática: one <see cref="AuditEntry"/> per created, modified or deleted aggregate with the changed
/// fields (old and new values). Explicit business actions (sign, cancel…) are recorded through <see cref="IAuditLog"/>.
/// </summary>
internal sealed class AuditTrailInterceptor(IUserContext userContext, ITenantContext tenantContext, TimeProvider timeProvider) : SaveChangesInterceptor
{
    /// <summary>Technical columns; the last access changes on every sign-in and would flood the log.</summary>
    private static readonly HashSet<string> IgnoredProperties =
    [
        nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy), nameof(IAuditable.UpdatedAt), nameof(IAuditable.UpdatedBy),
        nameof(AggregateRoot.RowVersion), nameof(ITenantOwned.TenantId), nameof(TenantMembership.LastAccessAt)
    ];

    /// <summary>Technical tables that would only add noise to the log.</summary>
    private static readonly HashSet<Type> IgnoredTypes =
    [
        typeof(StoredFile), typeof(OutboxMessage), typeof(UserNotification), typeof(AuditEntry), typeof(FolioSequence)
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var entries = context.ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => !IgnoredTypes.Contains(e.Entity.GetType()))
            .ToList();

        foreach (var entry in entries)
        {
            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Deleted => AuditAction.Deleted,
                _ => AuditAction.Updated
            };

            string? changes = null;
            if (action == AuditAction.Updated)
            {
                var changed = entry.Properties
                    .Where(p => p.IsModified && !IgnoredProperties.Contains(p.Metadata.Name))
                    .Where(p => !Equals(p.OriginalValue, p.CurrentValue))
                    .ToDictionary(p => p.Metadata.Name, p => new[] { Format(p.OriginalValue), Format(p.CurrentValue) });

                if (changed.Count == 0 && !entry.Navigations.Any(n => n.IsModified))
                {
                    continue;
                }

                changes = changed.Count == 0 ? null : JsonSerializer.Serialize(changed, Json);
            }

            var tenantId = entry.Entity is ITenantOwned owned
                ? (Guid?)(Guid)entry.Property(nameof(ITenantOwned.TenantId)).CurrentValue!
                : tenantContext.HasTenant ? tenantContext.TenantId : null;

            context.Add(new AuditEntry(
                tenantId == Guid.Empty ? null : tenantId,
                now,
                userContext.IsAuthenticated ? userContext.IdentityId : null,
                userContext.IsAuthenticated ? userContext.DisplayName : "system",
                action,
                entry.Entity.GetType().Name,
                entry.Entity.Id,
                Summary(entry.Entity),
                changes,
                null,
                userContext.CorrelationId,
                userContext.IpAddress));
        }
    }

    internal static string? Summary(object entity) => entity switch
    {
        IAuditReference reference => reference.AuditReference,
        CatalogAggregate catalog => $"{catalog.Code} · {catalog.Name}",
        TenantMembership membership => membership.Email,
        TenantRole role => role.Name,
        Tenant tenant => tenant.Slug,
        _ => null
    };

    private static string? Format(object? value) => value switch
    {
        null => null,
        string text => text.Length > 300 ? text[..300] + "…" : text,
        DateTimeOffset date => date.ToString("O"),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        System.Collections.IEnumerable list => JsonSerializer.Serialize(list, Json),
        _ => JsonSerializer.Serialize(value, Json)
    };
}
