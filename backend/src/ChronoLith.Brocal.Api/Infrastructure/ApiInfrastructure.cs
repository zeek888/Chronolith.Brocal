using System.Collections.Concurrent;
using System.Reflection;
using ChronoLith.Brocal.Infrastructure.Platform;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace ChronoLith.Brocal.Api.Infrastructure;

/// <summary>Last line of defense: unexpected exceptions become problem details without leaking internals.</summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "El registro fue modificado por otro usuario. Recarga e intenta de nuevo."),
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" } } => (StatusCodes.Status409Conflict, "Ya existe un registro con esos datos."),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "La solicitud no tiene un formato válido."),
            PlatformUnavailableException unavailable => (StatusCodes.Status503ServiceUnavailable, unavailable.Message),
            _ => (StatusCodes.Status500InternalServerError, "Ocurrió un error inesperado.")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Excepción no controlada procesando {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "Solicitud rechazada: {Title}", title);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title }
        });
    }
}

/// <summary>
/// Minimal APIs bind enum query parameters with a case-sensitive parse, while the JSON payloads use camelCase names.
/// This middleware rewrites the enum query values of the matched endpoint to their declared names, so
/// <c>?status=signed</c>, <c>?status=Signed</c> and <c>?status=2</c> are equivalent.
/// </summary>
internal sealed class EnumQueryNormalizer(RequestDelegate next)
{
    private static readonly ConcurrentDictionary<Endpoint, IReadOnlyDictionary<string, Type>> EnumParameters = new();

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.QueryString.HasValue && context.GetEndpoint() is { } endpoint)
        {
            var parameters = EnumParameters.GetOrAdd(endpoint, Discover);
            if (parameters.Count > 0)
            {
                Normalize(context.Request, parameters);
            }
        }

        return next(context);
    }

    private static IReadOnlyDictionary<string, Type> Discover(Endpoint endpoint)
    {
        var parameters = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        if (endpoint.Metadata.GetMetadata<MethodInfo>() is not { } method)
        {
            return parameters;
        }

        foreach (var parameter in method.GetParameters())
        {
            if (parameter.GetCustomAttribute<AsParametersAttribute>() is not null)
            {
                foreach (var property in parameter.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    AddIfEnum(parameters, property.Name, property.PropertyType);
                }
            }
            else if (parameter.Name is not null)
            {
                AddIfEnum(parameters, parameter.Name, parameter.ParameterType);
            }
        }

        return parameters;
    }

    private static void AddIfEnum(Dictionary<string, Type> parameters, string name, Type type)
    {
        var element = type.IsArray ? type.GetElementType()! : type;
        element = Nullable.GetUnderlyingType(element) ?? element;
        if (element.IsEnum)
        {
            parameters[name] = element;
        }
    }

    private static void Normalize(HttpRequest request, IReadOnlyDictionary<string, Type> parameters)
    {
        if (!request.Query.Keys.Any(parameters.ContainsKey))
        {
            return;
        }

        var query = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in request.Query)
        {
            query[key] = parameters.TryGetValue(key, out var enumType)
                ? new StringValues(values.Select(v => v is not null && Enum.TryParse(enumType, v, ignoreCase: true, out var parsed) ? parsed.ToString() : v).ToArray())
                : values;
        }

        request.Query = new QueryCollection(query);
    }
}

/// <summary>Documents the Keycloak bearer token and the company header in the OpenAPI document.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "ChronoLith Brocal API";
        document.Info.Description = "Documentación operativa de servicios petroleros (multiempresa). Envía el token de Keycloak y la empresa en el encabezado X-Tenant (id o identificador).";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token emitido por Keycloak."
        };

        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, document)] = [] }
        ];

        return Task.CompletedTask;
    }
}
