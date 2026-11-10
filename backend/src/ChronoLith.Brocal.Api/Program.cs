using ChronoLith.Brocal.Api;
using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Api.Infrastructure;
using ChronoLith.Brocal.Application;
using ChronoLith.Brocal.Infrastructure;
using ChronoLith.Brocal.Infrastructure.Persistence;
using ChronoLith.Brocal.Infrastructure.Tenancy;
using ChronoLith.Licensing;
using ChronoLith.Licensing.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Standalone mode validates the ChronoLith license of the installation; as a module of ChronoLith the Shell does.
var standalone = builder.Configuration.IsStandaloneMode();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPresentation(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

if (standalone)
{
    // Without a valid license the API does not start: it logs the installation id (to request one) and stops.
    await app.Services.InitializeChronoLithLicenseAsync();
}

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

if (standalone)
{
    // Expired license (after the grace period): read-only. Writes that are not business data are marked with
    // AllowWhenLicenseExpired on their endpoints (session, notifications read, document packages).
    app.UseChronoLithLicenseEnforcement();
}

app.UseMiddleware<EnumQueryNormalizer>();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

var api = app.MapGroup("api").RequireAuthorization();
app.MapEndpoints(api);

if (standalone)
{
    // GET status: any signed-in user (notice in the layout). Detail and loading a new license: platform administrators
    // of the installation (not the administrator of a company).
    var license = api.MapChronoLithLicense("license");
    license.Details.RequirePlatformAdmin();
    license.Update.RequirePlatformAdmin();
}

await app.RunAsync();

/// <summary>Entry point (exposed for integration tests).</summary>
public partial class Program;
