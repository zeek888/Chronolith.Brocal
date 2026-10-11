using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Platform;

// Platform administration (brocal.platform.manage): companies, their quotas, modules, look and status.

public sealed record TenantSummaryDto(
    Guid Id,
    string Slug,
    string Name,
    string? TaxId,
    TenantStatus Status,
    string Plan,
    DateOnly? TrialEndsOn,
    int ModuleCount,
    int Users,
    string? LogoUrl,
    string PrimaryColor,
    DateTimeOffset CreatedAt);

public sealed record TenantDetailDto(
    Guid Id,
    string Slug,
    string Name,
    string? LegalName,
    string? TaxId,
    string? ContactEmail,
    string? ContactPhone,
    string Plan,
    TenantStatus Status,
    string? StatusReason,
    DateOnly? TrialEndsOn,
    BrandingDto Branding,
    TenantQuotas Quotas,
    TenantSettings Settings,
    FiscalProfileDto Fiscal,
    IReadOnlyList<string> EnabledModules,
    UsageDto Usage,
    DateTimeOffset CreatedAt);

public sealed record SearchTenantsQuery(string? Search, TenantStatus? Status, int Page = 1, int PageSize = 25) : IQuery<PagedResult<TenantSummaryDto>>;

internal sealed class SearchTenantsQueryHandler(IApplicationDbContext db, IFileService files) : IQueryHandler<SearchTenantsQuery, PagedResult<TenantSummaryDto>>
{
    public async Task<Result<PagedResult<TenantSummaryDto>>> Handle(SearchTenantsQuery query, CancellationToken cancellationToken)
    {
        var request = new PageRequest(query.Search, query.Page, query.PageSize);
        var tenants = db.Tenants.AsNoTracking();
        if (request.Term is { } term)
        {
            tenants = tenants.Where(t => t.Name.ToLower().Contains(term) || t.Slug.Contains(term) || (t.TaxId != null && t.TaxId.ToLower().Contains(term)));
        }

        if (query.Status is not null)
        {
            tenants = tenants.Where(t => t.Status == query.Status);
        }

        var total = await tenants.CountAsync(cancellationToken);
        var page = await tenants
            .OrderBy(t => t.Name)
            .Skip((request.NormalizedPage - 1) * request.NormalizedPageSize)
            .Take(request.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var ids = page.Select(t => t.Id).ToList();
        var users = await db.Memberships.IgnoreQueryFilters()
            .Where(m => ids.Contains(m.TenantId) && m.Status != MembershipStatus.Disabled)
            .GroupBy(m => m.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, cancellationToken);

        var items = page.Select(t => new TenantSummaryDto(
            t.Id,
            t.Slug,
            t.Name,
            t.TaxId,
            t.Status,
            t.Plan,
            t.TrialEndsOn,
            t.EnabledModules.Count(m => !BrocalModules.CoreModules.Contains(m)),
            users.GetValueOrDefault(t.Id),
            t.Branding.LogoFileId is { } logo ? files.GetUrl(logo, TimeSpan.FromHours(12), t.Id) : null,
            t.Branding.UseChronoLithTheme ? ThemePalette.ChronoLith.Primary : t.Branding.Palette.Primary,
            t.CreatedAt)).ToList();

        return new PagedResult<TenantSummaryDto>(items, request.NormalizedPage, request.NormalizedPageSize, total);
    }
}

public sealed record GetTenantQuery(Guid Id) : IQuery<TenantDetailDto>;

internal sealed class GetTenantQueryHandler(IApplicationDbContext db, ITenantContext tenantContext, IFileService files, IQuotaGuard quotas)
    : IQueryHandler<GetTenantQuery, TenantDetailDto>
{
    public async Task<Result<TenantDetailDto>> Handle(GetTenantQuery query, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == query.Id, cancellationToken);
        if (tenant is null)
        {
            return TenantErrors.NotFound;
        }

        using var scope = tenantContext.BeginScope(TenantSnapshot.From(tenant));
        var usage = await quotas.UsageAsync(cancellationToken);

        return new TenantDetailDto(
            tenant.Id,
            tenant.Slug,
            tenant.Name,
            tenant.LegalName,
            tenant.TaxId,
            tenant.ContactEmail,
            tenant.ContactPhone,
            tenant.Plan,
            tenant.Status,
            tenant.StatusReason,
            tenant.TrialEndsOn,
            BrandingDto.From(tenant.Branding, files),
            tenant.Quotas,
            tenant.Settings,
            FiscalProfileDto.From(tenant.Fiscal),
            tenant.EnabledModules,
            usage,
            tenant.CreatedAt);
    }
}

// ---- Create ------------------------------------------------------------------------------------------

public sealed record CreateTenantCommand(
    string Slug,
    string Name,
    string? LegalName,
    string? TaxId,
    string? ContactEmail,
    string? ContactPhone,
    string Plan,
    IReadOnlyList<string> Modules,
    TenantQuotas? Quotas,
    int? TrialDays,
    string AdminEmail,
    string AdminName,
    bool ProvisionAdminAccount,
    string? AdminTemporaryPassword) : ICommand<CreateTenantResult>;

public sealed record CreateTenantResult(Guid Id, Guid AdminMembershipId, bool AccountCreated);

internal sealed class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(c => c.Slug).NotEmpty().Must(s => Slugs.IsValid(Slugs.Normalize(s)))
            .WithMessage("Usa minúsculas, números y guiones (3 a 40 caracteres).");
        RuleFor(c => c.Name).RequiredName(150);
        RuleFor(c => c.TaxId).Rfc();
        RuleFor(c => c.ContactEmail).Email().When(c => !string.IsNullOrWhiteSpace(c.ContactEmail));
        RuleFor(c => c.AdminEmail).NotEmpty().EmailAddress().WithMessage("Indica el correo del administrador de la empresa.");
        RuleFor(c => c.AdminName).RequiredName(150);
        RuleFor(c => c.TrialDays).InclusiveBetween(1, 365).When(c => c.TrialDays is not null);
        RuleFor(c => c.AdminTemporaryPassword).NotEmpty().MinimumLength(8)
            .When(c => c.ProvisionAdminAccount).WithMessage("La contraseña temporal requiere al menos 8 caracteres.");
    }
}

internal sealed class CreateTenantCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IIdentityProvisioning identity,
    IAuditLog audit,
    ITenantClock clock) : ICommandHandler<CreateTenantCommand, CreateTenantResult>
{
    public async Task<Result<CreateTenantResult>> Handle(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        var slug = Slugs.Normalize(command.Slug);
        if (await db.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken))
        {
            return TenantErrors.SlugTaken;
        }

        var created = Tenant.Create(slug, command.Name, command.Plan, command.Modules);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var tenant = created.Value;
        var profile = tenant.UpdateProfile(command.Name, command.LegalName, command.TaxId, command.ContactEmail, command.ContactPhone, command.Plan);
        if (profile.IsFailure)
        {
            return profile.Error;
        }

        if (command.Quotas is not null)
        {
            var quotas = tenant.UpdateQuotas(command.Quotas);
            if (quotas.IsFailure)
            {
                return quotas.Error;
            }
        }

        if (command.TrialDays is { } days)
        {
            tenant.StartTrial(clock.Today.AddDays(days));
        }

        var accountCreated = false;
        if (command.ProvisionAdminAccount && identity.IsEnabled)
        {
            var existing = await identity.FindByEmailAsync(command.AdminEmail, cancellationToken);
            if (existing is null)
            {
                var (first, last) = SplitName(command.AdminName);
                var account = await identity.CreateAsync(command.AdminEmail, first, last, command.AdminTemporaryPassword!, cancellationToken);
                if (account.IsFailure)
                {
                    return account.Error;
                }

                accountCreated = true;
            }
        }

        db.Tenants.Add(tenant);

        Guid adminId;
        using (tenantContext.BeginScope(TenantSnapshot.From(tenant)))
        {
            TenantStarterKit.Apply(db);
            var admin = TenantMembership.Invite(command.AdminEmail, command.AdminName, isTenantAdmin: true, []);
            if (admin.IsFailure)
            {
                return admin.Error;
            }

            db.Memberships.Add(admin.Value);
            adminId = admin.Value.Id;
            audit.Record(AuditAction.Created, nameof(Tenant), tenant.Id, $"{tenant.Slug} · {tenant.Name}");
            await db.SaveChangesAsync(cancellationToken);
        }

        return new CreateTenantResult(tenant.Id, adminId, accountCreated);
    }

    internal static (string First, string Last) SplitName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 ? parts[1] : parts[0]);
    }
}

// ---- Update profile, quotas, modules, branding, status -----------------------------------------------

public sealed record UpdateTenantProfileCommand(Guid Id, string Name, string? LegalName, string? TaxId, string? ContactEmail, string? ContactPhone, string Plan) : ICommand;

internal sealed class UpdateTenantProfileCommandHandler(IApplicationDbContext db) : ICommandHandler<UpdateTenantProfileCommand>
{
    public async Task<Result> Handle(UpdateTenantProfileCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (tenant is null)
        {
            return TenantErrors.NotFound;
        }

        var result = tenant.UpdateProfile(command.Name, command.LegalName, command.TaxId, command.ContactEmail, command.ContactPhone, command.Plan);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record UpdateTenantQuotasCommand(Guid Id, TenantQuotas Quotas) : ICommand;

internal sealed class UpdateTenantQuotasCommandHandler(IApplicationDbContext db) : ICommandHandler<UpdateTenantQuotasCommand>
{
    public async Task<Result> Handle(UpdateTenantQuotasCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (tenant is null)
        {
            return TenantErrors.NotFound;
        }

        var result = tenant.UpdateQuotas(command.Quotas);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record SetTenantModulesCommand(Guid Id, IReadOnlyList<string> Modules) : ICommand<IReadOnlyList<string>>;

internal sealed class SetTenantModulesCommandHandler(IApplicationDbContext db) : ICommandHandler<SetTenantModulesCommand, IReadOnlyList<string>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(SetTenantModulesCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (tenant is null)
        {
            return TenantErrors.NotFound;
        }

        var unknown = command.Modules.FirstOrDefault(m => !BrocalModules.Exists(m));
        if (unknown is not null)
        {
            return Error.Validation("Tenants.UnknownModule", $"El módulo '{unknown}' no existe.");
        }

        tenant.SetModules(command.Modules);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(tenant.EnabledModules);
    }
}

public sealed record UpdateBrandingCommand(Guid? TenantId, TenantBranding Branding) : ICommand;

internal sealed class UpdateBrandingCommandHandler(IApplicationDbContext db, ITenantContext tenantContext) : ICommandHandler<UpdateBrandingCommand>
{
    public async Task<Result> Handle(UpdateBrandingCommand command, CancellationToken cancellationToken)
    {
        var id = command.TenantId ?? tenantContext.TenantId;
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return TenantErrors.NotFound;
        }

        foreach (var fileId in new[] { command.Branding.LogoFileId, command.Branding.LogoDarkFileId }.OfType<Guid>())
        {
            var exists = await db.Files.IgnoreQueryFilters().AnyAsync(f => f.Id == fileId && f.TenantId == tenant.Id, cancellationToken);
            if (!exists)
            {
                return FileErrors.NotFound;
            }
        }

        var result = tenant.UpdateBranding(command.Branding);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public enum TenantStatusAction
{
    Activate = 0,
    StartTrial = 1,
    Suspend = 2,
    Cancel = 3
}

public sealed record ChangeTenantStatusCommand(Guid Id, TenantStatusAction Action, string? Reason, DateOnly? TrialEndsOn) : ICommand;

internal sealed class ChangeTenantStatusCommandHandler(IApplicationDbContext db, IAuditLog audit, ITenantClock clock) : ICommandHandler<ChangeTenantStatusCommand>
{
    public async Task<Result> Handle(ChangeTenantStatusCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (tenant is null)
        {
            return TenantErrors.NotFound;
        }

        var result = command.Action switch
        {
            TenantStatusAction.Activate => Activate(tenant),
            TenantStatusAction.StartTrial => StartTrial(tenant, command.TrialEndsOn ?? clock.Today.AddDays(30)),
            TenantStatusAction.Suspend => tenant.Suspend(command.Reason ?? string.Empty),
            _ => tenant.Cancel(command.Reason ?? string.Empty)
        };

        if (result.IsFailure)
        {
            return result;
        }

        audit.Record(AuditAction.StatusChanged, nameof(Tenant), tenant.Id, $"{tenant.Slug} → {tenant.Status}", command.Reason);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Result Activate(Tenant tenant)
    {
        tenant.Activate();
        return Result.Success();
    }

    private static Result StartTrial(Tenant tenant, DateOnly endsOn)
    {
        tenant.StartTrial(endsOn);
        return Result.Success();
    }
}
