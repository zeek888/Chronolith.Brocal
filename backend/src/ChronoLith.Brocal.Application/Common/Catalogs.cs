using System.Linq.Expressions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Behaviors;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Common;

/// <summary>Search of a catalog: paging, text, active flag and extra filters from the query string.</summary>
public sealed record CatalogQuery(
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 25,
    string? Sort = null,
    bool Descending = false) : PageRequest(Search, Page, PageSize, Sort, Descending);

/// <summary>
/// Describes one catalog for the generic <see cref="CatalogService{TEntity,TData,TDto}"/>: how to project it,
/// create/update it from its data record, which references to validate and when it is in use.
/// </summary>
public interface ICatalogSpec<TEntity, TData, TDto>
    where TEntity : CatalogAggregate
{
    /// <summary>Error prefix (<c>Customers</c>, <c>Units</c>…).</summary>
    string Name { get; }

    Expression<Func<TEntity, TDto>> Projection { get; }

    /// <summary>Parent shown in lookups (customer of a contract, field of a site…).</summary>
    Expression<Func<TEntity, Guid?>>? LookupParent => null;

    /// <summary>Extra text of a lookup item (unit name, RFC…).</summary>
    Expression<Func<TEntity, string?>>? LookupExtra => null;

    string CodeOf(TData data);

    Result<TEntity> Create(TData data);

    Result Update(TEntity entity, TData data);

    /// <summary>Checks referenced records (customer of a contract…) and quotas.</summary>
    Task<Result> ValidateAsync(TData data, TEntity? existing, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

    /// <summary>Whether other records reference it (then it is deactivated instead of deleted).</summary>
    Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Filters beyond text and active flag (<c>customerId</c>, <c>type</c>…).</summary>
    IQueryable<TEntity> Filter(IQueryable<TEntity> query, IReadOnlyDictionary<string, string?> filters) => query;

    /// <summary>Called after create/update before saving (e.g. to bind related data).</summary>
    Task AfterSaveAsync(TEntity entity, bool created, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Generic application service for master data (customers, services, units, warehouses…): search, lookup,
/// create, update, activate/deactivate and delete when not referenced. Codes are unique per company.
/// </summary>
public sealed class CatalogService<TEntity, TData, TDto>(
    IApplicationDbContext db,
    ICatalogSpec<TEntity, TData, TDto> spec,
    IEnumerable<IValidator<TData>> validators,
    IAuditLog audit)
    where TEntity : CatalogAggregate
    where TDto : class
{
    public async Task<Result<PagedResult<TDto>>> SearchAsync(CatalogQuery query, IReadOnlyDictionary<string, string?> filters, CancellationToken cancellationToken)
    {
        var items = db.Set<TEntity>().AsNoTracking();
        if (query.Term is { } term)
        {
            items = items.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term));
        }

        if (query.IsActive is not null)
        {
            items = items.Where(x => x.IsActive == query.IsActive);
        }

        items = spec.Filter(items, filters);
        items = query.Sort?.ToLowerInvariant() switch
        {
            "code" => query.Descending ? items.OrderByDescending(x => x.Code) : items.OrderBy(x => x.Code),
            "updated" => query.Descending ? items.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt) : items.OrderBy(x => x.UpdatedAt ?? x.CreatedAt),
            _ => query.Descending ? items.OrderByDescending(x => x.Name) : items.OrderBy(x => x.Name).ThenBy(x => x.Code)
        };

        return await items.Select(spec.Projection).ToPagedAsync(query, cancellationToken);
    }

    public async Task<Result<TDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.Set<TEntity>().AsNoTracking().Where(x => x.Id == id).Select(spec.Projection).FirstOrDefaultAsync(cancellationToken);
        return item is null ? CatalogErrors.NotFound(spec.Name) : item;
    }

    public async Task<Result<IReadOnlyList<LookupDto>>> LookupAsync(bool activeOnly, IReadOnlyDictionary<string, string?> filters, CancellationToken cancellationToken)
    {
        var items = spec.Filter(db.Set<TEntity>().AsNoTracking(), filters);
        if (activeOnly)
        {
            items = items.Where(x => x.IsActive);
        }

        var ordered = items.OrderBy(x => x.Name).ThenBy(x => x.Code);
        var parent = spec.LookupParent;
        var extra = spec.LookupExtra;

        List<LookupDto> list;
        if (parent is null && extra is null)
        {
            list = await ordered.Select(x => new LookupDto(x.Id, x.Code, x.Name, x.IsActive, null, null)).ToListAsync(cancellationToken);
        }
        else
        {
            var rows = await ordered.Select(BuildLookupProjection(parent, extra)).ToListAsync(cancellationToken);
            list = rows;
        }

        return list;
    }

    public async Task<Result<Guid>> CreateAsync(TData data, CancellationToken cancellationToken)
    {
        var validation = await Validation.ValidateAsync(data, validators, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var unique = await EnsureUniqueCodeAsync(spec.CodeOf(data), null, cancellationToken);
        if (unique.IsFailure)
        {
            return unique.Error;
        }

        var references = await spec.ValidateAsync(data, null, cancellationToken);
        if (references.IsFailure)
        {
            return references.Error;
        }

        var entity = spec.Create(data);
        if (entity.IsFailure)
        {
            return entity.Error;
        }

        db.Set<TEntity>().Add(entity.Value);
        await spec.AfterSaveAsync(entity.Value, created: true, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return entity.Value.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, TData data, CancellationToken cancellationToken)
    {
        var validation = await Validation.ValidateAsync(data, validators, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var entity = await db.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return CatalogErrors.NotFound(spec.Name);
        }

        var unique = await EnsureUniqueCodeAsync(spec.CodeOf(data), id, cancellationToken);
        if (unique.IsFailure)
        {
            return unique;
        }

        var references = await spec.ValidateAsync(data, entity, cancellationToken);
        if (references.IsFailure)
        {
            return references;
        }

        var result = spec.Update(entity, data);
        if (result.IsFailure)
        {
            return result;
        }

        await spec.AfterSaveAsync(entity, created: false, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var entity = await db.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return CatalogErrors.NotFound(spec.Name);
        }

        if (isActive)
        {
            entity.Activate();
        }
        else
        {
            entity.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return CatalogErrors.NotFound(spec.Name);
        }

        if (await spec.IsInUseAsync(id, cancellationToken))
        {
            return CatalogErrors.InUse(spec.Name);
        }

        db.Set<TEntity>().Remove(entity);
        audit.Record(AuditAction.Deleted, typeof(TEntity).Name, entity.Id, $"{entity.Code} · {entity.Name}");
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result> EnsureUniqueCodeAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = Codes.Normalize(code ?? string.Empty);
        var taken = await db.Set<TEntity>().AnyAsync(x => x.Code == normalized && x.Id != exceptId, cancellationToken);
        return taken ? CatalogErrors.CodeAlreadyExists(spec.Name, normalized) : Result.Success();
    }

    /// <summary>x => new LookupDto(x.Id, x.Code, x.Name, x.IsActive, parent(x), extra(x)).</summary>
    private static Expression<Func<TEntity, LookupDto>> BuildLookupProjection(
        Expression<Func<TEntity, Guid?>>? parent,
        Expression<Func<TEntity, string?>>? extra)
    {
        var x = Expression.Parameter(typeof(TEntity), "x");
        var constructor = typeof(LookupDto).GetConstructors().Single();
        var parentBody = parent is null
            ? (Expression)Expression.Constant(null, typeof(Guid?))
            : new ParameterReplacer(parent.Parameters[0], x).Visit(parent.Body);
        var extraBody = extra is null
            ? (Expression)Expression.Constant(null, typeof(string))
            : new ParameterReplacer(extra.Parameters[0], x).Visit(extra.Body);

        var body = Expression.New(
            constructor,
            Expression.Property(x, nameof(CatalogAggregate.Id)),
            Expression.Property(x, nameof(CatalogAggregate.Code)),
            Expression.Property(x, nameof(CatalogAggregate.Name)),
            Expression.Property(x, nameof(CatalogAggregate.IsActive)),
            parentBody,
            extraBody);

        return Expression.Lambda<Func<TEntity, LookupDto>>(body, x);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

/// <summary>Helpers for specs: references must exist in the company (inactive ones are accepted for old data).</summary>
public static class References
{
    public static async Task<Result> EnsureAsync<T>(IApplicationDbContext db, Guid? id, string catalog, CancellationToken cancellationToken, bool requireActive = false)
        where T : CatalogAggregate
    {
        if (id is null)
        {
            return Result.Success();
        }

        var item = await db.Set<T>().AsNoTracking().Where(x => x.Id == id).Select(x => new { x.IsActive }).FirstOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            return CatalogErrors.ReferenceNotFound(catalog);
        }

        return requireActive && !item.IsActive ? CatalogErrors.Inactive(catalog) : Result.Success();
    }

    public static async Task<Result> EnsureAllAsync<T>(IApplicationDbContext db, IEnumerable<Guid>? ids, string catalog, CancellationToken cancellationToken)
        where T : CatalogAggregate
    {
        var list = ids?.Distinct().ToList() ?? [];
        if (list.Count == 0)
        {
            return Result.Success();
        }

        var found = await db.Set<T>().CountAsync(x => list.Contains(x.Id), cancellationToken);
        return found == list.Count ? Result.Success() : CatalogErrors.ReferenceNotFound(catalog);
    }
}
