using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Common;

public sealed record CodeName(string Code, string Name);

/// <summary>Resolves code and name of catalog ids in one query (detail views and documents).</summary>
public static class CatalogNames
{
    public static async Task<Dictionary<Guid, CodeName>> LoadAsync<T>(IApplicationDbContext db, IEnumerable<Guid?> ids, CancellationToken cancellationToken)
        where T : CatalogAggregate
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        return await db.Set<T>()
            .AsNoTracking()
            .Where(x => list.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new CodeName(x.Code, x.Name), cancellationToken);
    }

    public static Task<Dictionary<Guid, CodeName>> LoadAsync<T>(IApplicationDbContext db, IEnumerable<Guid> ids, CancellationToken cancellationToken)
        where T : CatalogAggregate =>
        LoadAsync<T>(db, ids.Select(i => (Guid?)i), cancellationToken);

    public static string? NameOf(this Dictionary<Guid, CodeName> names, Guid? id) => id is { } value && names.TryGetValue(value, out var item) ? item.Name : null;

    public static string? CodeOf(this Dictionary<Guid, CodeName> names, Guid? id) => id is { } value && names.TryGetValue(value, out var item) ? item.Code : null;
}
