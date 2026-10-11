using System.IO.Compression;
using System.Linq.Expressions;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Compliance;

// ---- Document types and requirements --------------------------------------------------------------------

public sealed record DocumentTypeDto(
    Guid Id, string Code, string Name, DocumentSubject AppliesTo, string? Authority, int? ValidityMonths, int? WarningDays, bool IsMandatory,
    string? Notes, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

internal sealed class DocumentTypeDataValidator : AbstractValidator<DocumentTypeData>
{
    public DocumentTypeDataValidator()
    {
        RuleFor(t => t.Code).BusinessCode();
        RuleFor(t => t.Name).RequiredName();
        RuleFor(t => t.ValidityMonths).InclusiveBetween(1, 240).When(t => t.ValidityMonths is not null);
        RuleFor(t => t.WarningDays).InclusiveBetween(1, 365).When(t => t.WarningDays is not null);
    }
}

internal sealed class DocumentTypeSpec(IApplicationDbContext db) : ICatalogSpec<DocumentType, DocumentTypeData, DocumentTypeDto>
{
    public string Name => "DocumentTypes";

    public Expression<Func<DocumentType, DocumentTypeDto>> Projection => t => new DocumentTypeDto(
        t.Id, t.Code, t.Name, t.AppliesTo, t.Authority, t.ValidityMonths, t.WarningDays, t.IsMandatory, t.Notes, t.IsActive, t.CreatedAt, t.UpdatedAt);

    public Expression<Func<DocumentType, string?>>? LookupExtra => t => t.AppliesTo == DocumentSubject.Unit ? "unit" : "employee";

    public string CodeOf(DocumentTypeData data) => data.Code;

    public Result<DocumentType> Create(DocumentTypeData data) => DocumentType.Create(data);

    public Result Update(DocumentType entity, DocumentTypeData data) => entity.Update(data);

    public Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) => db.ComplianceDocuments.AnyAsync(d => d.DocumentTypeId == id, cancellationToken);

    public IQueryable<DocumentType> Filter(IQueryable<DocumentType> query, IReadOnlyDictionary<string, string?> filters) =>
        filters.TryGetValue("appliesTo", out var subject) && Enum.TryParse<DocumentSubject>(subject, true, out var value)
            ? query.Where(t => t.AppliesTo == value)
            : query;
}

public sealed record ComplianceRequirementDto(
    Guid Id, string Code, string Name, Guid? CustomerId, string? CustomerName, Guid? ContractId, string? ContractName,
    IReadOnlyList<Guid> EmployeeDocumentTypeIds, IReadOnlyList<Guid> UnitDocumentTypeIds, string? Notes, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

internal sealed class ComplianceRequirementDataValidator : AbstractValidator<ComplianceRequirementData>
{
    public ComplianceRequirementDataValidator()
    {
        RuleFor(r => r.Code).BusinessCode();
        RuleFor(r => r.Name).RequiredName();
    }
}

internal sealed class ComplianceRequirementSpec(IApplicationDbContext db) : ICatalogSpec<ComplianceRequirement, ComplianceRequirementData, ComplianceRequirementDto>
{
    public string Name => "ComplianceRequirements";

    public Expression<Func<ComplianceRequirement, ComplianceRequirementDto>> Projection => r => new ComplianceRequirementDto(
        r.Id, r.Code, r.Name, r.CustomerId, db.Customers.Where(c => c.Id == r.CustomerId).Select(c => c.Name).FirstOrDefault(),
        r.ContractId, db.Contracts.Where(c => c.Id == r.ContractId).Select(c => c.Name).FirstOrDefault(),
        r.EmployeeDocumentTypeIds, r.UnitDocumentTypeIds, r.Notes, r.IsActive, r.CreatedAt, r.UpdatedAt);

    public string CodeOf(ComplianceRequirementData data) => data.Code;

    public Result<ComplianceRequirement> Create(ComplianceRequirementData data) => ComplianceRequirement.Create(data);

    public Result Update(ComplianceRequirement entity, ComplianceRequirementData data) => entity.Update(data);

    public async Task<Result> ValidateAsync(ComplianceRequirementData data, ComplianceRequirement? existing, CancellationToken cancellationToken) => Result.FirstFailure(
        await References.EnsureAsync<Customer>(db, data.CustomerId, CustomerErrors.Catalog, cancellationToken),
        await References.EnsureAsync<Contract>(db, data.ContractId, ContractErrors.Catalog, cancellationToken),
        await References.EnsureAllAsync<DocumentType>(db, (data.EmployeeDocumentTypeIds ?? []).Concat(data.UnitDocumentTypeIds ?? []), "DocumentTypes", cancellationToken));

    public Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
}

// ---- Documents ------------------------------------------------------------------------------------------

public sealed record ComplianceDocumentDto(
    Guid Id,
    DocumentSubject Subject,
    Guid SubjectId,
    string? SubjectCode,
    string? SubjectName,
    Guid DocumentTypeId,
    string? DocumentTypeName,
    string? Number,
    string? Issuer,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    decimal? Hours,
    Guid? FileId,
    string? FileUrl,
    string? Notes,
    ComplianceStatus Status,
    int? DaysLeft);

internal static class ComplianceAccess
{
    public static string ReadPermission(DocumentSubject subject) => subject == DocumentSubject.Unit ? Permissions.PermitsRead : Permissions.PersonnelRead;

    public static string ManagePermission(DocumentSubject subject) => subject == DocumentSubject.Unit ? Permissions.PermitsManage : Permissions.PersonnelManage;

    public static Result Ensure(ITenantContext tenant, DocumentSubject subject, bool manage)
    {
        var permission = manage ? ManagePermission(subject) : ReadPermission(subject);
        return tenant.Has(permission) || !manage && tenant.Has(ManagePermission(subject))
            ? Result.Success()
            : Error.Forbidden("Compliance.Forbidden", "No tienes permiso para estos documentos.");
    }
}

public sealed record SearchComplianceDocumentsQuery(
    DocumentSubject Subject,
    Guid? SubjectId,
    Guid? DocumentTypeId,
    ComplianceStatus? Status,
    string? Search,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedResult<ComplianceDocumentDto>>;

internal sealed class SearchComplianceDocumentsQueryHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock, IFileService files)
    : IQueryHandler<SearchComplianceDocumentsQuery, PagedResult<ComplianceDocumentDto>>
{
    public async Task<Result<PagedResult<ComplianceDocumentDto>>> Handle(SearchComplianceDocumentsQuery query, CancellationToken cancellationToken)
    {
        var access = ComplianceAccess.Ensure(tenant, query.Subject, manage: false);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var today = clock.Today;
        var warning = tenant.Tenant.Settings.ExpiryWarningDays;
        var documents = await db.ComplianceDocuments.AsNoTracking()
            .Where(d => d.Subject == query.Subject)
            .WhereIf(query.SubjectId is not null, d => d.SubjectId == query.SubjectId)
            .WhereIf(query.DocumentTypeId is not null, d => d.DocumentTypeId == query.DocumentTypeId)
            .OrderBy(d => d.ExpiresOn ?? DateOnly.MaxValue)
            .ToListAsync(cancellationToken);

        var rows = await ComplianceRows.BuildAsync(db, files, documents, today, warning, cancellationToken);
        if (query.Status is { } status)
        {
            rows = rows.Where(r => r.Status == status).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(r => (r.SubjectName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                                   || (r.SubjectCode ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                                   || (r.DocumentTypeName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                                   || (r.Number ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var request = new PageRequest(null, query.Page, query.PageSize);
        var page = rows.Skip((request.NormalizedPage - 1) * request.NormalizedPageSize).Take(request.NormalizedPageSize).ToList();
        return new PagedResult<ComplianceDocumentDto>(page, request.NormalizedPage, request.NormalizedPageSize, rows.Count);
    }
}

internal static class ComplianceRows
{
    public static async Task<List<ComplianceDocumentDto>> BuildAsync(
        IApplicationDbContext db,
        IFileService files,
        IReadOnlyList<ComplianceDocument> documents,
        DateOnly today,
        int warningDays,
        CancellationToken cancellationToken)
    {
        var typeIds = documents.Select(d => d.DocumentTypeId).Distinct().ToList();
        var types = await db.DocumentTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        var units = await CatalogNames.LoadAsync<Unit>(db, documents.Where(d => d.Subject == DocumentSubject.Unit).Select(d => d.SubjectId), cancellationToken);
        var employees = await CatalogNames.LoadAsync<Employee>(db, documents.Where(d => d.Subject == DocumentSubject.Employee).Select(d => d.SubjectId), cancellationToken);

        return documents.Select(d =>
        {
            var names = d.Subject == DocumentSubject.Unit ? units : employees;
            var type = types.GetValueOrDefault(d.DocumentTypeId);
            return new ComplianceDocumentDto(
                d.Id, d.Subject, d.SubjectId, names.CodeOf(d.SubjectId), names.NameOf(d.SubjectId), d.DocumentTypeId, type?.Name, d.Number, d.Issuer, d.IssuedOn, d.ExpiresOn, d.Hours,
                d.FileId, d.FileId is { } file ? files.GetUrl(file) : null, d.Notes,
                d.StatusOn(today, type?.WarningDays ?? warningDays), ComplianceRules.DaysLeft(d.ExpiresOn, today));
        }).ToList();
    }
}

public sealed record SaveComplianceDocumentCommand(Guid? Id, DocumentSubject Subject, Guid SubjectId, ComplianceDocumentData Data) : ICommand<Guid>;

internal sealed class SaveComplianceDocumentCommandHandler(IApplicationDbContext db, ITenantContext tenant) : ICommandHandler<SaveComplianceDocumentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveComplianceDocumentCommand command, CancellationToken cancellationToken)
    {
        var access = ComplianceAccess.Ensure(tenant, command.Subject, manage: true);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var type = await db.DocumentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == command.Data.DocumentTypeId, cancellationToken);
        if (type is null)
        {
            return CatalogErrors.ReferenceNotFound("DocumentTypes");
        }

        if (type.AppliesTo != command.Subject)
        {
            return ComplianceErrors.WrongSubject;
        }

        var subjectExists = command.Subject == DocumentSubject.Unit
            ? await db.Units.AnyAsync(u => u.Id == command.SubjectId, cancellationToken)
            : await db.Employees.AnyAsync(e => e.Id == command.SubjectId, cancellationToken);
        if (!subjectExists)
        {
            return CatalogErrors.ReferenceNotFound(command.Subject == DocumentSubject.Unit ? UnitErrors.Catalog : EmployeeErrors.Catalog);
        }

        if (command.Data.FileId is { } fileId)
        {
            var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
            if (file is null)
            {
                return FileErrors.NotFound;
            }

            file.AttachTo(nameof(ComplianceDocument), command.SubjectId);
        }

        // Default expiry from the validity of the type when only the issue date is captured.
        var data = command.Data;
        if (data.ExpiresOn is null && data.IssuedOn is not null && type.ValidityMonths is { } months)
        {
            data = data with { ExpiresOn = data.IssuedOn.Value.AddMonths(months) };
        }

        if (command.Id is { } id)
        {
            var existing = await db.ComplianceDocuments.FirstOrDefaultAsync(d => d.Id == id && d.Subject == command.Subject, cancellationToken);
            if (existing is null)
            {
                return ComplianceErrors.NotFound;
            }

            var updated = existing.Update(data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var created = ComplianceDocument.Create(command.Subject, command.SubjectId, data);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.ComplianceDocuments.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value.Id;
    }
}

public sealed record DeleteComplianceDocumentCommand(DocumentSubject Subject, Guid Id) : ICommand;

internal sealed class DeleteComplianceDocumentCommandHandler(IApplicationDbContext db, ITenantContext tenant, IAuditLog audit) : ICommandHandler<DeleteComplianceDocumentCommand>
{
    public async Task<Result> Handle(DeleteComplianceDocumentCommand command, CancellationToken cancellationToken)
    {
        var access = ComplianceAccess.Ensure(tenant, command.Subject, manage: true);
        if (access.IsFailure)
        {
            return access;
        }

        var document = await db.ComplianceDocuments.FirstOrDefaultAsync(d => d.Id == command.Id && d.Subject == command.Subject, cancellationToken);
        if (document is null)
        {
            return ComplianceErrors.NotFound;
        }

        db.ComplianceDocuments.Remove(document);
        audit.Record(AuditAction.Deleted, nameof(ComplianceDocument), document.Id, document.Number);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---- Compliance matrix ----------------------------------------------------------------------------------

public sealed record MatrixCellDto(Guid DocumentTypeId, ComplianceStatus Status, DateOnly? ExpiresOn, int? DaysLeft, Guid? DocumentId);

public sealed record MatrixRowDto(Guid SubjectId, string Code, string Name, string? Detail, ComplianceStatus Worst, IReadOnlyList<MatrixCellDto> Cells);

public sealed record ComplianceMatrixDto(DocumentSubject Subject, IReadOnlyList<LookupDto> DocumentTypes, IReadOnlyList<MatrixRowDto> Rows, IReadOnlyDictionary<ComplianceStatus, int> Totals);

/// <summary>
/// Semáforo de cumplimiento: units or employees against the document types required (all mandatory types, or the
/// ones of a customer/contract requirement), with the worst status per row.
/// </summary>
public sealed record GetComplianceMatrixQuery(DocumentSubject Subject, Guid? RequirementId, IReadOnlyList<Guid>? SubjectIds) : IQuery<ComplianceMatrixDto>;

internal sealed class GetComplianceMatrixQueryHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock) : IQueryHandler<GetComplianceMatrixQuery, ComplianceMatrixDto>
{
    public async Task<Result<ComplianceMatrixDto>> Handle(GetComplianceMatrixQuery query, CancellationToken cancellationToken)
    {
        var access = ComplianceAccess.Ensure(tenant, query.Subject, manage: false);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var matrix = await ComplianceMatrixBuilder.BuildAsync(db, query.Subject, query.RequirementId, query.SubjectIds, clock.Today, tenant.Tenant.Settings.ExpiryWarningDays, cancellationToken);
        return matrix;
    }
}

internal static class ComplianceMatrixBuilder
{
    public static async Task<Result<ComplianceMatrixDto>> BuildAsync(
        IApplicationDbContext db,
        DocumentSubject subject,
        Guid? requirementId,
        IReadOnlyList<Guid>? subjectIds,
        DateOnly today,
        int warningDays,
        CancellationToken cancellationToken)
    {
        List<Guid> typeIds;
        if (requirementId is not null)
        {
            var requirement = await db.ComplianceRequirements.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requirementId, cancellationToken);
            if (requirement is null)
            {
                return CatalogErrors.NotFound("ComplianceRequirements");
            }

            typeIds = (subject == DocumentSubject.Unit ? requirement.UnitDocumentTypeIds : requirement.EmployeeDocumentTypeIds).ToList();
        }
        else
        {
            typeIds = await db.DocumentTypes.AsNoTracking().Where(t => t.AppliesTo == subject && t.IsActive && t.IsMandatory).Select(t => t.Id).ToListAsync(cancellationToken);
        }

        var types = await db.DocumentTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).OrderBy(t => t.Name).ToListAsync(cancellationToken);

        List<(Guid Id, string Code, string Name, string? Detail)> subjects;
        if (subject == DocumentSubject.Unit)
        {
            subjects = (await db.Units.AsNoTracking().Where(u => u.IsActive).WhereIf(subjectIds is { Count: > 0 }, u => subjectIds!.Contains(u.Id))
                .OrderBy(u => u.Code).Select(u => new { u.Id, u.Code, u.Name, u.Plates }).ToListAsync(cancellationToken))
                .Select(u => (u.Id, u.Code, u.Name, u.Plates)).ToList();
        }
        else
        {
            subjects = (await db.Employees.AsNoTracking().Where(e => e.IsActive).WhereIf(subjectIds is { Count: > 0 }, e => subjectIds!.Contains(e.Id))
                .OrderBy(e => e.Name).Select(e => new { e.Id, e.Code, e.Name, e.Position }).ToListAsync(cancellationToken))
                .Select(e => (e.Id, e.Code, e.Name, e.Position)).ToList();
        }

        var ids = subjects.Select(s => s.Id).ToList();
        var documents = await db.ComplianceDocuments.AsNoTracking()
            .Where(d => d.Subject == subject && ids.Contains(d.SubjectId) && typeIds.Contains(d.DocumentTypeId))
            .ToListAsync(cancellationToken);

        var rows = subjects.Select(s =>
        {
            var cells = types.Select(type =>
            {
                var latest = documents.Where(d => d.SubjectId == s.Id && d.DocumentTypeId == type.Id).OrderByDescending(d => d.ExpiresOn ?? DateOnly.MaxValue).FirstOrDefault();
                return latest is null
                    ? new MatrixCellDto(type.Id, ComplianceStatus.Missing, null, null, null)
                    : new MatrixCellDto(type.Id, latest.StatusOn(today, type.WarningDays ?? warningDays), latest.ExpiresOn, ComplianceRules.DaysLeft(latest.ExpiresOn, today), latest.Id);
            }).ToList();

            return new MatrixRowDto(s.Id, s.Code, s.Name, s.Detail, ComplianceRules.Worst(cells.Select(c => c.Status)), cells);
        }).ToList();

        var totals = rows.GroupBy(r => r.Worst).ToDictionary(g => g.Key, g => g.Count());
        return new ComplianceMatrixDto(subject, types.Select(t => new LookupDto(t.Id, t.Code, t.Name, t.IsActive)).ToList(), rows, totals);
    }
}

// ---- Document package (per contract or customer) -------------------------------------------------------

/// <summary>
/// Paquete documental: ZIP with an index PDF (compliance matrix of the selected people and units) and every
/// document file organized by person or unit, ready to deliver to the customer for site access.
/// </summary>
public sealed record GenerateDocumentPackageQuery(Guid? RequirementId, IReadOnlyList<Guid>? EmployeeIds, IReadOnlyList<Guid>? UnitIds, string? Title) : IQuery<FileContent>;

internal sealed class GenerateDocumentPackageQueryHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    ITenantClock clock,
    IFileService files,
    IPdfRenderer renderer,
    IDocumentBrandingProvider branding,
    IAuditLog audit) : IQueryHandler<GenerateDocumentPackageQuery, FileContent>
{
    public async Task<Result<FileContent>> Handle(GenerateDocumentPackageQuery query, CancellationToken cancellationToken)
    {
        if ((query.EmployeeIds?.Count ?? 0) + (query.UnitIds?.Count ?? 0) == 0)
        {
            return Error.Validation("Compliance.EmptyPackage", "Selecciona al menos una persona o unidad.");
        }

        var today = clock.Today;
        var warning = tenant.Tenant.Settings.ExpiryWarningDays;
        var sections = new List<DocumentTable>();
        var entries = new List<(string Path, Guid FileId)>();

        foreach (var (subject, ids) in new[] { (DocumentSubject.Employee, query.EmployeeIds), (DocumentSubject.Unit, query.UnitIds) })
        {
            if (ids is not { Count: > 0 })
            {
                continue;
            }

            var matrix = await ComplianceMatrixBuilder.BuildAsync(db, subject, query.RequirementId, ids, today, warning, cancellationToken);
            if (matrix.IsFailure)
            {
                return matrix.Error;
            }

            var value = matrix.Value;
            sections.Add(new DocumentTable(
                subject == DocumentSubject.Employee ? "Personal" : "Unidades",
                ["Clave", "Nombre", .. value.DocumentTypes.Select(t => t.Code), "Estatus"],
                value.Rows.Select(r => (IReadOnlyList<string>)[r.Code, r.Name, .. r.Cells.Select(CellText), StatusText(r.Worst)]).ToList()));

            var documentIds = value.Rows.SelectMany(r => r.Cells).Where(c => c.DocumentId is not null).Select(c => c.DocumentId!.Value).ToList();
            var documents = await db.ComplianceDocuments.AsNoTracking().Where(d => documentIds.Contains(d.Id) && d.FileId != null).ToListAsync(cancellationToken);
            foreach (var document in documents)
            {
                var row = value.Rows.First(r => r.SubjectId == document.SubjectId);
                var type = value.DocumentTypes.First(t => t.Id == document.DocumentTypeId);
                entries.Add(($"{(subject == DocumentSubject.Employee ? "Personal" : "Unidades")}/{Safe(row.Code)} {Safe(row.Name)}/{Safe(type.Code)}", document.FileId!.Value));
            }
        }

        var title = string.IsNullOrWhiteSpace(query.Title) ? "Paquete documental" : query.Title.Trim();
        var index = renderer.Render(new BusinessDocument(
            title,
            today.ToString("yyyy-MM-dd"),
            null,
            today.ToString("dd/MM/yyyy"),
            [],
            sections,
            null,
            [("Simbología", "OK = vigente · PV = por vencer · VENC = vencido · FALTA = no capturado · S/V = sin vencimiento. La fecha es el vencimiento.")],
            [],
            []), await branding.GetAsync(cancellationToken));

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var indexEntry = zip.CreateEntry("00_Indice.pdf");
            await using (var stream = indexEntry.Open())
            {
                await stream.WriteAsync(index, cancellationToken);
            }

            foreach (var (path, fileId) in entries)
            {
                var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
                var bytes = await files.ReadAllBytesAsync(fileId, cancellationToken);
                if (file is null || bytes is null)
                {
                    continue;
                }

                var entry = zip.CreateEntry($"{path}{Path.GetExtension(file.FileName)}");
                await using var stream = entry.Open();
                await stream.WriteAsync(bytes, cancellationToken);
            }
        }

        audit.Record(AuditAction.Exported, "DocumentPackage", null, title, details: new { Employees = query.EmployeeIds?.Count ?? 0, Units = query.UnitIds?.Count ?? 0, Files = entries.Count });
        await db.SaveChangesAsync(cancellationToken);
        return new FileContent($"{Safe(title)}_{today:yyyyMMdd}.zip", FileContent.Zip, buffer.ToArray());
    }

    private static string CellText(MatrixCellDto cell) => cell.Status switch
    {
        ComplianceStatus.Valid => $"OK {cell.ExpiresOn:dd/MM/yy}",
        ComplianceStatus.ExpiringSoon => $"PV {cell.ExpiresOn:dd/MM/yy}",
        ComplianceStatus.Expired => $"VENC {cell.ExpiresOn:dd/MM/yy}",
        ComplianceStatus.NoExpiry => "S/V",
        _ => "FALTA"
    };

    private static string StatusText(ComplianceStatus status) => status switch
    {
        ComplianceStatus.Valid or ComplianceStatus.NoExpiry => "Cumple",
        ComplianceStatus.ExpiringSoon => "Por vencer",
        ComplianceStatus.Expired => "Vencido",
        _ => "Incompleto"
    };

    private static string Safe(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}

// ---- Expiring list (dashboard widget) -------------------------------------------------------------------

public sealed record GetExpiringDocumentsQuery(int Days = 30, int Take = 20) : IQuery<IReadOnlyList<ComplianceDocumentDto>>;

internal sealed class GetExpiringDocumentsQueryHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock, IFileService files)
    : IQueryHandler<GetExpiringDocumentsQuery, IReadOnlyList<ComplianceDocumentDto>>
{
    public async Task<Result<IReadOnlyList<ComplianceDocumentDto>>> Handle(GetExpiringDocumentsQuery query, CancellationToken cancellationToken)
    {
        var subjects = new List<DocumentSubject>();
        if (tenant.HasAny(Permissions.PermitsRead, Permissions.PermitsManage))
        {
            subjects.Add(DocumentSubject.Unit);
        }

        if (tenant.HasAny(Permissions.PersonnelRead, Permissions.PersonnelManage))
        {
            subjects.Add(DocumentSubject.Employee);
        }

        var today = clock.Today;
        var horizon = today.AddDays(Math.Clamp(query.Days, 1, 365));
        var documents = await db.ComplianceDocuments.AsNoTracking()
            .Where(d => subjects.Contains(d.Subject) && d.ExpiresOn != null && d.ExpiresOn <= horizon)
            .OrderBy(d => d.ExpiresOn)
            .Take(Math.Clamp(query.Take, 1, 200))
            .ToListAsync(cancellationToken);

        return await ComplianceRows.BuildAsync(db, files, documents, today, tenant.Tenant.Settings.ExpiryWarningDays, cancellationToken);
    }
}
