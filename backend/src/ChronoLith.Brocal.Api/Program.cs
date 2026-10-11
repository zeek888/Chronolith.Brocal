using ChronoLith.Brocal.Api;
using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Api.Infrastructure;
using ChronoLith.Brocal.Application;
using ChronoLith.Brocal.Infrastructure;
using ChronoLith.Brocal.Infrastructure.Persistence;
using ChronoLith.Brocal.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPresentation(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("OpenApi:Enabled", false))
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar", options => options.WithTitle("ChronoLith Brocal API"));
}

app.UseCors(ChronoLith.Brocal.Api.DependencyInjection.CorsPolicy);
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.UseMiddleware<EnumQueryNormalizer>();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapEndpoints(app.MapGroup("api").RequireAuthorization());

await app.RunAsync();

/// <summary>Entry point (exposed for integration tests).</summary>
public partial class Program;
