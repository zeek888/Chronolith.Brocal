using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Files;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Application.Features.Tenancy;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.AspNetCore.Mvc;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>Administration of the current company: settings, look and feel, fiscal data, users and roles.</summary>
internal sealed class CompanyEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("company").WithTags("Mi empresa");

        group.MapGet("settings", async (IQueryHandler<GetCompanySettingsQuery, CompanySettingsDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetCompanySettingsQuery(), ct)).ToOk())
            .RequirePermission(Permissions.AccessSettings, Permissions.InvoicingSettings);

        group.MapPut("settings", async (TenantSettings settings, ICommandHandler<UpdateCompanySettingsCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new UpdateCompanySettingsCommand(settings), ct)).ToNoContent())
            .RequirePermission(Permissions.AccessSettings);

        group.MapPut("branding", async (TenantBranding branding, ICommandHandler<UpdateBrandingCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new UpdateBrandingCommand(null, branding), ct)).ToNoContent())
            .RequirePermission(Permissions.AccessSettings);

        group.MapPut("fiscal", async (UpdateFiscalProfileCommand command, ICommandHandler<UpdateFiscalProfileCommand> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToNoContent())
            .RequirePermission(Permissions.InvoicingSettings);

        group.MapGet("usage", async (IQuotaGuard quotas, CancellationToken ct) => Results.Ok(await quotas.UsageAsync(ct)))
            .RequireTenant();

        var members = group.MapGroup("members").RequirePermission(Permissions.AccessUsers);

        members.MapGet("", async (string? search, MembershipStatus? status, bool? portalUsers, Guid? roleId, int? page, int? pageSize,
                IQueryHandler<SearchMembersQuery, PagedResult<MemberDto>> handler, CancellationToken ct) =>
            (await handler.Handle(new SearchMembersQuery(search, status, portalUsers, roleId, page ?? 1, pageSize ?? 50), ct)).ToOk());

        members.MapPost("", async (InviteMemberCommand command, ICommandHandler<InviteMemberCommand, InviteMemberResult> handler, CancellationToken ct) =>
            (await handler.Handle(command, ct)).ToCreated(r => $"/api/company/members/{r.Id}"));

        members.MapPut("{id:guid}", async (Guid id, MemberRequest body, ICommandHandler<UpdateMemberCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new UpdateMemberCommand(id, body.FullName, body.IsTenantAdmin, body.RoleIds, body.EmployeeId, body.CustomerId), ct)).ToNoContent());

        members.MapPut("{id:guid}/status", async (Guid id, MemberStatusRequest body, ICommandHandler<SetMemberStatusCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new SetMemberStatusCommand(id, body.Enabled), ct)).ToNoContent());

        members.MapDelete("{id:guid}", async (Guid id, ICommandHandler<RemoveMemberCommand> handler, CancellationToken ct) =>
            (await handler.Handle(new RemoveMemberCommand(id), ct)).ToNoContent());

        var roles = group.MapGroup("roles");

        roles.MapGet("", async (IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetRolesQuery(), ct)).ToOk())
            .RequirePermission(Permissions.AccessRoles, Permissions.AccessUsers, Permissions.NotificationsManage);

        roles.MapPost("", async (RoleRequest body, ICommandHandler<SaveRoleCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveRoleCommand(null, body.Name, body.Description, body.Permissions), ct)).ToCreated("/api/company/roles"))
            .RequirePermission(Permissions.AccessRoles);

        roles.MapPut("{id:guid}", async (Guid id, RoleRequest body, ICommandHandler<SaveRoleCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveRoleCommand(id, body.Name, body.Description, body.Permissions), ct)).ToNoContent())
            .RequirePermission(Permissions.AccessRoles);

        roles.MapDelete("{id:guid}", async (Guid id, ICommandHandler<DeleteRoleCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new DeleteRoleCommand(id), ct)).ToNoContent())
            .RequirePermission(Permissions.AccessRoles);
    }

    internal sealed record MemberRequest(string FullName, bool IsTenantAdmin, IReadOnlyList<Guid> RoleIds, Guid? EmployeeId, Guid? CustomerId);

    internal sealed record MemberStatusRequest(bool Enabled);

    internal sealed record RoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
}

/// <summary>Uploads (any member of the company) and signed downloads (no session).</summary>
internal sealed class FileEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("files").WithTags("Archivos");

        group.MapPost("", async (IFormFile file, [FromForm] string? category, ICommandHandler<UploadFileCommand, UploadedFileDto> handler, CancellationToken ct) =>
            {
                // Same names as the JSON payloads ("photo", "signature"…), case-insensitive.
                var kind = Enum.TryParse<FileCategory>(category, ignoreCase: true, out var parsed) ? parsed : FileCategory.Other;
                await using var stream = file.OpenReadStream();
                return (await handler.Handle(new UploadFileCommand(stream, file.FileName, file.ContentType, kind), ct)).ToOk();
            })
            .RequireTenant()
            .DisableAntiforgery()
            .WithSummary("Sube un archivo de la empresa (fotos, firmas, permisos, comprobantes, logos).");

        group.MapGet("{id:guid}", async (Guid id, IQueryHandler<GetFileUrlQuery, UploadedFileDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetFileUrlQuery(id), ct)).ToOk())
            .RequireTenant()
            .WithSummary("Datos del archivo con un enlace firmado vigente.");

        group.MapGet("{id:guid}/content", async (Guid id, string t, long e, string s, bool? download, IFileService files, HttpContext http, CancellationToken ct) =>
            {
                if (!Guid.TryParseExact(t, "N", out var tenantId) && !Guid.TryParse(t, out tenantId))
                {
                    return Results.NotFound();
                }

                var file = await files.OpenSignedAsync(id, tenantId, e, s, ct);
                if (file is null)
                {
                    return Results.Problem(title: "Enlace inválido", detail: "El enlace del archivo expiró o no es válido.", statusCode: StatusCodes.Status403Forbidden);
                }

                http.Response.Headers.CacheControl = "private, max-age=3600";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                var inline = download != true && (file.Value.ContentType.StartsWith("image/", StringComparison.Ordinal) || file.Value.ContentType == FileContent.Pdf);
                return Results.Stream(file.Value.Content, file.Value.ContentType, inline ? null : file.Value.FileName, enableRangeProcessing: true);
            })
            .AllowAnonymous()
            .WithSummary("Contenido de un archivo por enlace firmado (imágenes y PDF se muestran en línea).");
    }
}
