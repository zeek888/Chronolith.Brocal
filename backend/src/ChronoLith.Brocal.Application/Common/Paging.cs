using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Paging, text search and sorting shared by every list.</summary>
public record PageRequest(string? Search = null, int Page = 1, int PageSize = 25, string? Sort = null, bool Descending = false)
{
    public const int MaxPageSize = 500;

    public int NormalizedPage => Math.Max(1, Page);

    public int NormalizedPageSize => Math.Clamp(PageSize, 1, MaxPageSize);

    public string? Term => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim().ToLowerInvariant();
}

/// <summary>Labeled figure of a summary (dashboards, detail headers).</summary>
public sealed record KpiDto(string Label, decimal Value, string? Format = null);

/// <summary>Item of a selector: id, code and name (with an optional parent to filter dependent selects).</summary>
public sealed record LookupDto(Guid Id, string Code, string Name, bool IsActive, Guid? ParentId = null, string? Extra = null);

public static class QueryExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageRequest request, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.NormalizedPage - 1) * request.NormalizedPageSize)
            .Take(request.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, request.NormalizedPage, request.NormalizedPageSize, total);
    }

    public static IQueryable<T> WhereIf<T>(this IQueryable<T> query, bool condition, System.Linq.Expressions.Expression<Func<T, bool>> predicate) =>
        condition ? query.Where(predicate) : query;
}
