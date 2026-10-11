using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Common;

namespace ChronoLith.Brocal.Api.Extensions;

/// <summary>
/// Maps the standard routes of a master-data catalog:
/// <c>GET /</c> search, <c>GET /lookup</c>, <c>GET /{id}</c>, <c>POST /</c>, <c>PUT /{id}</c>,
/// <c>PUT /{id}/active</c> and <c>DELETE /{id}</c> (409 when referenced).
/// </summary>
public static class CatalogEndpoints
{
    public sealed record ActiveRequest(bool IsActive);

    public static RouteGroupBuilder MapCatalog<TEntity, TData, TDto>(
        this IEndpointRouteBuilder app,
        string route,
        string tag,
        string[] readPermissions,
        params string[] managePermissions)
        where TEntity : CatalogAggregate
        where TDto : class
    {
        var group = app.MapGroup(route).WithTags(tag);

        group.MapGet("", async (
                string? search,
                bool? isActive,
                int? page,
                int? pageSize,
                string? sort,
                bool? descending,
                HttpRequest request,
                CatalogService<TEntity, TData, TDto> service,
                CancellationToken ct) =>
            (await service.SearchAsync(new CatalogQuery(search, isActive, page ?? 1, pageSize ?? 25, sort, descending ?? false), request.Filters(), ct)).ToOk())
            .RequirePermission(readPermissions);

        group.MapGet("lookup", async (bool? activeOnly, HttpRequest request, CatalogService<TEntity, TData, TDto> service, CancellationToken ct) =>
                (await service.LookupAsync(activeOnly ?? true, request.Filters(), ct)).ToOk())
            .RequireTenant();

        group.MapGet("{id:guid}", async (Guid id, CatalogService<TEntity, TData, TDto> service, CancellationToken ct) =>
                (await service.GetAsync(id, ct)).ToOk())
            .RequirePermission(readPermissions);

        group.MapPost("", async (TData data, CatalogService<TEntity, TData, TDto> service, CancellationToken ct) =>
                (await service.CreateAsync(data, ct)).ToCreated($"/api/{route}"))
            .RequirePermission(managePermissions);

        group.MapPut("{id:guid}", async (Guid id, TData data, CatalogService<TEntity, TData, TDto> service, CancellationToken ct) =>
                (await service.UpdateAsync(id, data, ct)).ToNoContent())
            .RequirePermission(managePermissions);

        group.MapPut("{id:guid}/active", async (Guid id, ActiveRequest body, CatalogService<TEntity, TData, TDto> service, CancellationToken ct) =>
                (await service.SetActiveAsync(id, body.IsActive, ct)).ToNoContent())
            .RequirePermission(managePermissions);

        group.MapDelete("{id:guid}", async (Guid id, CatalogService<TEntity, TData, TDto> service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToNoContent())
            .RequirePermission(managePermissions);

        return group;
    }
}
