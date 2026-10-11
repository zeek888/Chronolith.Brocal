using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Tenancy;

/// <summary>Configuration of the current company (Administración → Mi empresa).</summary>
public sealed record CompanySettingsDto(
    Guid Id,
    string Slug,
    string Name,
    string? LegalName,
    string? TaxId,
    string Plan,
    TenantStatus Status,
    BrandingDto Branding,
    TenantSettings Settings,
    FiscalProfileDto Fiscal,
    TenantQuotas Quotas,
    UsageDto Usage,
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<string> PacProviders);

public sealed record GetCompanySettingsQuery : IQuery<CompanySettingsDto>;

internal sealed class GetCompanySettingsQueryHandler(ITenantContext tenantContext, IFileService files, IQuotaGuard quotas, IPacGateway pac)
    : IQueryHandler<GetCompanySettingsQuery, CompanySettingsDto>
{
    public async Task<Result<CompanySettingsDto>> Handle(GetCompanySettingsQuery query, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Tenant;
        return new CompanySettingsDto(
            tenant.Id,
            tenant.Slug,
            tenant.Name,
            tenant.LegalName,
            tenant.TaxId,
            tenant.Plan,
            tenant.Status,
            BrandingDto.From(tenant.Branding, files),
            tenant.Settings,
            FiscalProfileDto.From(tenant.Fiscal),
            tenant.Quotas,
            await quotas.UsageAsync(cancellationToken),
            tenant.EnabledModules,
            pac.Providers);
    }
}

public sealed record UpdateCompanySettingsCommand(TenantSettings Settings) : ICommand;

internal sealed class UpdateCompanySettingsCommandHandler(IApplicationDbContext db, ITenantContext tenantContext) : ICommandHandler<UpdateCompanySettingsCommand>
{
    public async Task<Result> Handle(UpdateCompanySettingsCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        var result = tenant.UpdateSettings(command.Settings);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>Emisor data and PAC credentials. The secret is encrypted; null keeps the stored one.</summary>
public sealed record UpdateFiscalProfileCommand(
    string? Rfc,
    string? LegalName,
    string? TaxRegime,
    string? PostalCode,
    string InvoiceSeries,
    string PaymentSeries,
    string PacProvider,
    string? PacUser,
    string? PacSecret,
    bool PacSandbox) : ICommand;

internal sealed class UpdateFiscalProfileCommandValidator : AbstractValidator<UpdateFiscalProfileCommand>
{
    public UpdateFiscalProfileCommandValidator()
    {
        RuleFor(c => c.Rfc).Rfc();
        RuleFor(c => c.PostalCode).PostalCode();
        RuleFor(c => c.LegalName).OptionalText(300);
        RuleFor(c => c.TaxRegime).Matches("^\\d{3}$").When(c => !string.IsNullOrWhiteSpace(c.TaxRegime))
            .WithMessage("El régimen fiscal es la clave de 3 dígitos del SAT (p. ej. 601).");
        RuleFor(c => c.PacProvider).NotEmpty();
    }
}

internal sealed class UpdateFiscalProfileCommandHandler(IApplicationDbContext db, ITenantContext tenantContext, ISecretProtector protector, IPacGateway pac)
    : ICommandHandler<UpdateFiscalProfileCommand>
{
    public async Task<Result> Handle(UpdateFiscalProfileCommand command, CancellationToken cancellationToken)
    {
        if (!pac.Providers.Contains(command.PacProvider))
        {
            return Error.Validation("Fiscal.UnknownPac", "Proveedor de timbrado no disponible.");
        }

        var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        var secret = string.IsNullOrEmpty(command.PacSecret) ? tenant.Fiscal.PacSecretProtected : protector.Protect(command.PacSecret);
        var result = tenant.UpdateFiscalProfile(new TenantFiscalProfile(
            command.Rfc,
            command.LegalName,
            string.IsNullOrWhiteSpace(command.TaxRegime) ? null : command.TaxRegime.Trim(),
            string.IsNullOrWhiteSpace(command.PostalCode) ? null : command.PostalCode.Trim(),
            command.InvoiceSeries,
            command.PaymentSeries,
            command.PacProvider,
            string.IsNullOrWhiteSpace(command.PacUser) ? null : command.PacUser.Trim(),
            secret,
            command.PacSandbox));

        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
