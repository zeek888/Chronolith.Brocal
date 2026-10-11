using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Api.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;

namespace ChronoLith.Brocal.Api;

public static class DependencyInjection
{
    public const string CorsPolicy = "brocal-frontends";

    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance ??= $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
            context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        });

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

        services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Content-Disposition")));

        // Behind nginx / a gateway inside Docker networks.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 30L * 1024 * 1024);
        services.AddEndpoints(typeof(DependencyInjection).Assembly);

        return services;
    }
}
