using ChronoLith.Brocal.Api.Extensions;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Compliance;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Api.Endpoints;

/// <summary>
/// Permisos y certificaciones por unidad (SCT, ASEA, pólizas) and expedientes de personal (DC-3, H2S, exámenes, SSPA):
/// document types, documents with expiry traffic lights, compliance matrix and document packages per contract/customer.
/// </summary>
internal sealed class ComplianceEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        string[] read = [Permissions.PermitsRead, Permissions.PermitsManage, Permissions.PersonnelRead, Permissions.PersonnelManage, Permissions.PersonnelPackages];
        string[] manage = [Permissions.PermitsManage, Permissions.PersonnelManage];

        app.MapCatalog<DocumentType, DocumentTypeData, DocumentTypeDto>("compliance/document-types", "Permisos y expedientes", [.. read, Permissions.FleetRead], manage);
        app.MapCatalog<ComplianceRequirement, ComplianceRequirementData, ComplianceRequirementDto>("compliance/requirements", "Permisos y expedientes", read,
            [.. manage, Permissions.PersonnelPackages]);

        var group = app.MapGroup("compliance").WithTags("Permisos y expedientes");

        group.MapGet("documents", async (DocumentSubject subject, Guid? subjectId, Guid? documentTypeId, ComplianceStatus? status, string? search, int? page, int? pageSize,
                IQueryHandler<SearchComplianceDocumentsQuery, PagedResult<ComplianceDocumentDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new SearchComplianceDocumentsQuery(subject, subjectId, documentTypeId, status, search, page ?? 1, pageSize ?? 50), ct)).ToOk())
            .RequirePermission(read);

        group.MapPost("documents", async (SaveDocumentRequest body, ICommandHandler<SaveComplianceDocumentCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveComplianceDocumentCommand(null, body.Subject, body.SubjectId, body.Data), ct)).ToCreated("/api/compliance/documents"))
            .RequirePermission(manage);

        group.MapPut("documents/{id:guid}", async (Guid id, SaveDocumentRequest body, ICommandHandler<SaveComplianceDocumentCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new SaveComplianceDocumentCommand(id, body.Subject, body.SubjectId, body.Data), ct)).ToNoContent())
            .RequirePermission(manage);

        group.MapDelete("documents/{id:guid}", async (Guid id, DocumentSubject subject, ICommandHandler<DeleteComplianceDocumentCommand> handler, CancellationToken ct) =>
                (await handler.Handle(new DeleteComplianceDocumentCommand(subject, id), ct)).ToNoContent())
            .RequirePermission(manage);

        group.MapGet("matrix", async (DocumentSubject subject, Guid? requirementId, Guid[]? subjectIds, IQueryHandler<GetComplianceMatrixQuery, ComplianceMatrixDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetComplianceMatrixQuery(subject, requirementId, subjectIds), ct)).ToOk())
            .RequirePermission(read)
            .WithSummary("Matriz de cumplimiento (personas o unidades × documentos requeridos) con semáforo de vencimiento.");

        group.MapPost("package", async (PackageRequest body, IQueryHandler<GenerateDocumentPackageQuery, FileContent> handler, CancellationToken ct) =>
                (await handler.Handle(new GenerateDocumentPackageQuery(body.RequirementId, body.EmployeeIds, body.UnitIds, body.Title), ct)).ToFile())
            .RequirePermission(Permissions.PersonnelPackages, Permissions.PermitsManage, Permissions.PersonnelManage)
            .WithSummary("Paquete documental (ZIP con carátula PDF y archivos) por contrato o cliente.");

        group.MapGet("expiring", async (int? days, int? take, IQueryHandler<GetExpiringDocumentsQuery, IReadOnlyList<ComplianceDocumentDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetExpiringDocumentsQuery(days ?? 30, take ?? 20), ct)).ToOk())
            .RequirePermission([.. read, Permissions.FleetRead, Permissions.DashboardRead]);
    }

    internal sealed record SaveDocumentRequest(DocumentSubject Subject, Guid SubjectId, ComplianceDocumentData Data);

    internal sealed record PackageRequest(Guid? RequirementId, IReadOnlyList<Guid>? EmployeeIds, IReadOnlyList<Guid>? UnitIds, string? Title);
}
