using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Application.Features.Platform;

/// <summary>Multi-tenancy rules configured by the platform.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>Invitations bind only to Keycloak accounts whose e-mail is verified.</summary>
    public bool RequireVerifiedEmail { get; init; } = true;

    /// <summary>"ChronoLith" (module of the Shell) or "Standalone".</summary>
    public string Mode { get; init; } = "ChronoLith";
}

// ---- Start session ---------------------------------------------------------------------------------

public sealed record SessionDto(int Memberships, int Bound);

/// <summary>Binds pending invitations to the signed-in Keycloak account and records the access.</summary>
public sealed record StartSessionCommand : ICommand<SessionDto>;

internal sealed class StartSessionCommandHandler(
    IApplicationDbContext db,
    IUserContext user,
    ITenantContext tenantContext,
    IOptions<TenancyOptions> options,
    TimeProvider timeProvider) : ICommandHandler<StartSessionCommand, SessionDto>
{
    public async Task<Result<SessionDto>> Handle(StartSessionCommand command, CancellationToken cancellationToken)
    {
        var email = TenantMembership.NormalizeEmail(user.Email);
        var canBindByEmail = email is not null && (user.EmailVerified || !options.Value.RequireVerifiedEmail);
        var now = timeProvider.GetUtcNow();

        var memberships = await db.Memberships
            .IgnoreQueryFilters()
            .Where(m => m.IdentityId == user.IdentityId || canBindByEmail && m.IdentityId == null && m.Email == email)
            .ToListAsync(cancellationToken);

        var tenantIds = memberships.Select(m => m.TenantId).Distinct().ToList();
        var tenants = await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToListAsync(cancellationToken);

        // The memberships belong to different companies, and the request may already have resolved one of them
        // (users with a single active company): each company's rows are saved inside its own scope so the
        // multi-tenant guard accepts them.
        var bound = 0;
        foreach (var tenant in tenants)
        {
            using var scope = tenantContext.BeginScope(TenantSnapshot.From(tenant));
            foreach (var membership in memberships.Where(m => m.TenantId == tenant.Id))
            {
                if (membership.IdentityId is null)
                {
                    if (membership.Bind(user.IdentityId, user.FullName, now).IsSuccess)
                    {
                        bound++;
                    }
                }
                else
                {
                    membership.Touch(now);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return new SessionDto(memberships.Count(m => m.Status != MembershipStatus.Disabled), bound);
    }
}

// ---- Context ---------------------------------------------------------------------------------------

public sealed record CurrentUserDto(string Id, string Username, string? Email, string? FullName, bool IsPlatformAdmin, bool CanAccess);

public sealed record MyTenantDto(Guid Id, string Slug, string Name, string AppName, string? LogoUrl, TenantStatus Status, bool IsTenantAdmin, bool IsPortalUser);

public sealed record CurrentMembershipDto(Guid? Id, string FullName, bool IsTenantAdmin, Guid? EmployeeId, Guid? CustomerId, string? CustomerName);

public sealed record SettingsSummaryDto(
    string TimeZone,
    string Currency,
    string DefaultLanguage,
    int ExpiryWarningDays,
    int MinTicketPhotos,
    bool RequireCustomerSignature,
    bool RequireApprovalBeforeInvoice,
    decimal DefaultVatRate);

public sealed record CurrentTenantDto(
    Guid Id,
    string Slug,
    string Name,
    string? LegalName,
    TenantStatus Status,
    string Plan,
    DateOnly? TrialEndsOn,
    BrandingDto Branding,
    SettingsSummaryDto Settings,
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<string> Permissions,
    CurrentMembershipDto Membership,
    bool FiscalComplete);

public sealed record MyContextDto(CurrentUserDto User, IReadOnlyList<MyTenantDto> Tenants, CurrentTenantDto? Tenant, string Mode, string Version);

public sealed record GetMyContextQuery : IQuery<MyContextDto>;

internal sealed class GetMyContextQueryHandler(
    IApplicationDbContext db,
    IUserContext user,
    ITenantContext tenantContext,
    IPlatformAccessService platformAccess,
    IFileService files,
    IOptions<TenancyOptions> options) : IQueryHandler<GetMyContextQuery, MyContextDto>
{
    public async Task<Result<MyContextDto>> Handle(GetMyContextQuery query, CancellationToken cancellationToken)
    {
        var access = await platformAccess.GetAsync(cancellationToken);

        var memberships = await db.Memberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.IdentityId == user.IdentityId && m.Status != MembershipStatus.Disabled)
            .Select(m => new { m.TenantId, m.IsTenantAdmin, m.CustomerId })
            .ToListAsync(cancellationToken);

        var tenantIds = memberships.Select(m => m.TenantId).ToList();
        var tenants = await db.Tenants
            .AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id) && t.Status != TenantStatus.Cancelled)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var list = tenants
            .Select(t =>
            {
                var membership = memberships.First(m => m.TenantId == t.Id);
                return new MyTenantDto(
                    t.Id,
                    t.Slug,
                    t.Name,
                    t.Branding.AppName,
                    t.Branding.LogoFileId is { } logo ? SignedUrl(t.Id, logo) : null,
                    t.Status,
                    membership.IsTenantAdmin,
                    membership.CustomerId is not null);
            })
            .ToList();

        CurrentTenantDto? current = null;
        if (tenantContext.HasTenant)
        {
            var tenant = tenantContext.Tenant;
            var membership = tenantContext.Membership;
            string? customerName = null;
            if (membership?.CustomerId is { } customerId)
            {
                customerName = await db.Customers.AsNoTracking().Where(c => c.Id == customerId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
            }

            var settings = tenant.Settings;
            current = new CurrentTenantDto(
                tenant.Id,
                tenant.Slug,
                tenant.Name,
                tenant.LegalName,
                tenant.Status,
                tenant.Plan,
                tenant.TrialEndsOn,
                BrandingDto.From(tenant.Branding, files),
                new SettingsSummaryDto(
                    settings.TimeZone,
                    settings.Currency,
                    settings.DefaultLanguage,
                    settings.ExpiryWarningDays,
                    settings.MinTicketPhotos,
                    settings.RequireCustomerSignature,
                    settings.RequireApprovalBeforeInvoice,
                    settings.DefaultVatRate),
                BrocalModules.All.Where(m => tenant.IsModuleEnabled(m.Code)).Select(m => m.Code).ToList(),
                tenantContext.Permissions.Order(StringComparer.Ordinal).ToList(),
                new CurrentMembershipDto(
                    membership?.Id,
                    membership?.FullName ?? user.DisplayName,
                    membership?.IsTenantAdmin ?? tenantContext.IsPlatformAdmin,
                    membership?.EmployeeId,
                    membership?.CustomerId,
                    customerName),
                tenant.Fiscal.IsComplete);

            if (list.All(t => t.Id != tenant.Id))
            {
                // Platform administrators can open any company without being a member.
                list.Add(new MyTenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.Branding.AppName, current.Branding.LogoUrl, tenant.Status, true, false));
            }
        }

        return new MyContextDto(
            new CurrentUserDto(user.IdentityId, user.Username, user.Email, user.FullName, access.CanManagePlatform, access.CanAccess),
            list,
            current,
            options.Value.Mode,
            typeof(GetMyContextQueryHandler).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
    }

    /// <summary>The signature binds the file to its company, so other companies' logos are signed for their own id.</summary>
    private string SignedUrl(Guid tenantId, Guid fileId) => files.GetUrl(fileId, TimeSpan.FromHours(12), tenantId);
}

// ---- Module catalog --------------------------------------------------------------------------------

public sealed record GetModuleCatalogQuery : IQuery<IReadOnlyList<ModuleDto>>;

internal sealed class GetModuleCatalogQueryHandler : IQueryHandler<GetModuleCatalogQuery, IReadOnlyList<ModuleDto>>
{
    public Task<Result<IReadOnlyList<ModuleDto>>> Handle(GetModuleCatalogQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(ModuleDto.Catalog));
}

// ---- ChronoLith theme ------------------------------------------------------------------------------

public sealed record GetChronoLithThemeQuery : IQuery<ChronoLithTheme>;

internal sealed class GetChronoLithThemeQueryHandler(IChronoLithAppearance appearance) : IQueryHandler<GetChronoLithThemeQuery, ChronoLithTheme>
{
    public async Task<Result<ChronoLithTheme>> Handle(GetChronoLithThemeQuery query, CancellationToken cancellationToken) =>
        await appearance.GetDefaultThemeAsync(cancellationToken)
        ?? new ChronoLithTheme("ChronoLith", ThemePalette.ChronoLith, "ChronoLith", null, 0, "light");
}
