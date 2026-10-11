using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Platform;

public sealed record AuditEntryDto(Guid Id, DateTimeOffset OccurredAt, string? UserId, string UserName, AuditAction Action, string EntityType, Guid? EntityId, string? Summary,
    string? Changes, string? Reason, string? IpAddress);

/// <summary>Bitácora: who captured, edited or cancelled what, filtered by record, user, action and period.</summary>
public sealed record SearchAuditQuery(string? EntityType, Guid? EntityId, string? User, AuditAction? Action, DateOnly? From, DateOnly? To, string? Search, int Page = 1, int PageSize = 50)
    : IQuery<PagedResult<AuditEntryDto>>;

internal sealed class SearchAuditQueryHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock) : IQueryHandler<SearchAuditQuery, PagedResult<AuditEntryDto>>
{
    public async Task<Result<PagedResult<AuditEntryDto>>> Handle(SearchAuditQuery query, CancellationToken cancellationToken)
    {
        var from = query.From is null ? (DateTimeOffset?)null : clock.StartOfDay(query.From.Value);
        var to = query.To is null ? (DateTimeOffset?)null : clock.StartOfDay(query.To.Value.AddDays(1));
        var tenantId = tenant.TenantId;
        var entries = db.AuditEntries.AsNoTracking().Where(a => a.TenantId == tenantId)
            .WhereIf(!string.IsNullOrWhiteSpace(query.EntityType), a => a.EntityType == query.EntityType)
            .WhereIf(query.EntityId is not null, a => a.EntityId == query.EntityId)
            .WhereIf(query.Action is not null, a => a.Action == query.Action)
            .WhereIf(from is not null, a => a.OccurredAt >= from)
            .WhereIf(to is not null, a => a.OccurredAt < to);

        if (!string.IsNullOrWhiteSpace(query.User))
        {
            var user = query.User.Trim().ToLowerInvariant();
            entries = entries.Where(a => a.UserName.ToLower().Contains(user));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            entries = entries.Where(a => (a.Summary != null && a.Summary.ToLower().Contains(term)) || (a.Reason != null && a.Reason.ToLower().Contains(term)));
        }

        return await entries.OrderByDescending(a => a.OccurredAt)
            .Select(a => new AuditEntryDto(a.Id, a.OccurredAt, a.UserId, a.UserName, a.Action, a.EntityType, a.EntityId, a.Summary, a.Changes, a.Reason, a.IpAddress))
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);
    }
}

/// <summary>Types of records present in the log (for the filter).</summary>
public sealed record GetAuditEntityTypesQuery : IQuery<IReadOnlyList<string>>;

internal sealed class GetAuditEntityTypesQueryHandler(IApplicationDbContext db, ITenantContext tenant) : IQueryHandler<GetAuditEntityTypesQuery, IReadOnlyList<string>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(GetAuditEntityTypesQuery query, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        return await db.AuditEntries.AsNoTracking().Where(a => a.TenantId == tenantId).Select(a => a.EntityType).Distinct().OrderBy(t => t).ToListAsync(cancellationToken);
    }
}
