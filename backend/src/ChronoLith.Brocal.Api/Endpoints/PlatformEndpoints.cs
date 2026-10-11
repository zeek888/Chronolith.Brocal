using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

internal sealed class MeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("me").WithTags("Sesión");

        group.MapPost("session", async (ICommandHandler<StartSessionCommand, SessionDto> handler, CancellationToken ct) =>
                (await handler.Handle(new StartSessionCommand(), ct)).ToOk())
            .WithSummary("Liga las invitaciones pendientes a la cuenta de Keycloak y registra el acceso.");

        group.MapGet("context", async (IQueryHandler<GetMyContextQuery, MyContextDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetMyContextQuery(), ct)).ToOk())
            .WithSummary("Usuario, empresas a las que pertenece y, con X-Tenant, marca, módulos y permisos de la empresa.");
    }
}

internal sealed class PlatformEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("platform").WithTags("Plataforma");

        group.MapGet("modules", async (IQueryHandler<GetModuleCatalogQuery, IReadOnlyList<ModuleDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetModuleCatalogQuery(), ct)).ToOk())
            .WithSummary("Catálogo de los 24 módulos con sus permisos y dependencias.");

        group.MapGet("chronolith-theme", async (IQueryHandler<GetChronoLithThemeQuery, ChronoLithTheme> handler, CancellationToken ct) =>
                (await handler.Handle(new GetChronoLithThemeQuery(), ct)).ToOk())
            .WithSummary("Tema configurado en ChronoLith (para el modo independiente).");

        var tenants = group.MapGroup("tenants").RequirePlatformAdmin();

        tenants.MapGet("", async (string? search, TenantStatus? status, int? page, int? pageSize,
                IQueryHandler<SearchTenantsQuery, PagedResult<TenantSummaryDto>> handler, CancellationToken ct) =>
            (await handler.Handle(new SearchTenantsQuery(search, status, page ?? 1, pageSize ?? 25), ct)).ToOk());

        tenants.MapGet("{id:guid}", async (Guid id, IQueryHandler<GetTenantQuery, TenantDetailDto> handler, CancellationToken ct) =>
            (await handler.Handle(new GetTenantQuery(id), ct)).ToOk());

        tenants.MapPost("", async (CreateTenantCommand command, ICommandHandler<CreateTenantCommand, CreateTenantResult> handler, CancellationToken ct) =>
            (await handler.Handle(command, ct)).ToCreated(r => $"/api/platform/tenants/{r.Id}"));

        tenants.MapPut("{id:guid}", async (Guid id, TenantProfileRequest body, ICommandHandler<UpdateTenantProfileCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new UpdateTenantProfileCommand(id, body.Name, body.LegalName, body.TaxId, body.ContactEmail, body.ContactPhone, body.Plan), ct)).ToNoContent());

        tenants.MapPut("{id:guid}/quotas", async (Guid id, TenantQuotas quotas, ICommandHandler<UpdateTenantQuotasCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new UpdateTenantQuotasCommand(id, quotas), ct)).ToNoContent());

        tenants.MapPut("{id:guid}/modules", async (Guid id, ModulesRequest body, ICommandHandler<SetTenantModulesCommand, IReadOnlyList<string>> handler, CancellationToken ct) =>
            (await handler.Handle(new SetTenantModulesCommand(id, body.Modules), ct)).ToOk());

        tenants.MapPut("{id:guid}/branding", async (Guid id, TenantBranding branding, ICommandHandler<UpdateBrandingCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new UpdateBrandingCommand(id, branding), ct)).ToNoContent());

        tenants.MapPut("{id:guid}/status", async (Guid id, StatusRequest body, ICommandHandler<ChangeTenantStatusCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new ChangeTenantStatusCommand(id, body.Action, body.Reason, body.TrialEndsOn), ct)).ToNoContent());
    }

    internal sealed record TenantProfileRequest(string Name, string? LegalName, string? TaxId, string? ContactEmail, string? ContactPhone, string Plan);

    internal sealed record ModulesRequest(IReadOnlyList<string> Modules);

    internal sealed record StatusRequest(TenantStatusAction Action, string? Reason, DateOnly? TrialEndsOn);
}
