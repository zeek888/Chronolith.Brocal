using System.Reflection;
using ChronoLith.Brocal.Infrastructure.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChronoLith.Brocal.Api.Extensions;

/// <summary>Each feature group maps its own routes; endpoints are discovered by assembly scanning.</summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

public static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        var descriptors = assembly.DefinedTypes
            .Where(t => t is { IsAbstract: false, IsInterface: false } && t.IsAssignableTo(typeof(IEndpoint)))
            .Select(t => ServiceDescriptor.Transient(typeof(IEndpoint), t));

        services.TryAddEnumerable(descriptors);
        return services;
    }

    public static IEndpointRouteBuilder MapEndpoints(this WebApplication app, RouteGroupBuilder routeGroup)
    {
        foreach (var endpoint in app.Services.GetRequiredService<IEnumerable<IEndpoint>>())
        {
            endpoint.MapEndpoint(routeGroup);
        }

        return app;
    }

    /// <summary>Requires any of the company permissions (and that their modules are enabled for the company).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] anyOf)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(BrocalPolicies.Permission(anyOf));

    /// <summary>Requires a selected company (any member).</summary>
    public static TBuilder RequireTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(BrocalPolicies.Tenant);

    public static TBuilder RequirePlatformAdmin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(BrocalPolicies.PlatformAdmin);

    /// <summary>Query string values not bound to parameters, as filters for the catalog specs.</summary>
    public static IReadOnlyDictionary<string, string?> Filters(this HttpRequest request) =>
        request.Query.ToDictionary(q => q.Key, q => (string?)q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
}
