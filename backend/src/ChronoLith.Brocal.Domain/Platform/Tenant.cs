using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;

namespace ChronoLith.Brocal.Domain.Platform;

public enum TenantStatus
{
    Trial = 0,
    Active = 1,
    Suspended = 2,
    Cancelled = 3
}

/// <summary>
/// A company (contractor) that uses Brocal. It owns its data, users, roles, look and feel, quotas and the set
/// of modules it contracted. Only platform administrators create tenants and change quotas or modules.
/// </summary>
public sealed class Tenant : AggregateRoot
{
    private List<string> _enabledModules = [];

    private Tenant()
    {
    }

    private Tenant(string slug, string name)
    {
        Slug = slug;
        Name = name;
    }

    public string Slug { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? LegalName { get; private set; }

    public string? TaxId { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    public string Plan { get; private set; } = "Profesional";

    public TenantStatus Status { get; private set; } = TenantStatus.Active;

    public string? StatusReason { get; private set; }

    public DateOnly? TrialEndsOn { get; private set; }

    public TenantBranding Branding { get; private set; } = null!;

    public TenantQuotas Quotas { get; private set; } = TenantQuotas.Default;

    public TenantSettings Settings { get; private set; } = TenantSettings.Default;

    public TenantFiscalProfile Fiscal { get; private set; } = TenantFiscalProfile.Empty;

    /// <summary>Contracted modules, always including core modules and dependencies.</summary>
    public IReadOnlyList<string> EnabledModules => _enabledModules;

    /// <summary>Active or in trial (and the trial has not ended).</summary>
    public bool IsOperational(DateOnly today) =>
        Status == TenantStatus.Active || Status == TenantStatus.Trial && (TrialEndsOn is null || TrialEndsOn >= today);

    public static Result<Tenant> Create(string slug, string name, string plan, IEnumerable<string> modules)
    {
        var normalizedSlug = Slugs.Normalize(slug ?? string.Empty);
        if (!Slugs.IsValid(normalizedSlug))
        {
            return TenantErrors.InvalidSlug;
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
        {
            return TenantErrors.InvalidName;
        }

        var tenant = new Tenant(normalizedSlug, name.Trim())
        {
            Plan = string.IsNullOrWhiteSpace(plan) ? "Profesional" : plan.Trim(),
            Branding = TenantBranding.Default(name.Trim())
        };
        tenant.SetModules(modules);

        return tenant;
    }

    public Result UpdateProfile(string name, string? legalName, string? taxId, string? contactEmail, string? contactPhone, string plan)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
        {
            return TenantErrors.InvalidName;
        }

        var rfc = string.IsNullOrWhiteSpace(taxId) ? null : MexicanIds.NormalizeRfc(taxId);
        if (rfc is not null && !MexicanIds.IsValidRfc(rfc))
        {
            return TenantErrors.InvalidRfc;
        }

        Name = name.Trim();
        LegalName = string.IsNullOrWhiteSpace(legalName) ? null : legalName.Trim();
        TaxId = rfc;
        ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim().ToLowerInvariant();
        ContactPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim();
        Plan = string.IsNullOrWhiteSpace(plan) ? Plan : plan.Trim();

        return Result.Success();
    }

    public Result UpdateBranding(TenantBranding branding)
    {
        var normalized = branding.Normalize();
        if (normalized.IsFailure)
        {
            return normalized.Error;
        }

        Branding = normalized.Value;
        return Result.Success();
    }

    public Result UpdateQuotas(TenantQuotas quotas)
    {
        if (!quotas.IsValid)
        {
            return TenantErrors.InvalidQuotas;
        }

        Quotas = quotas;
        return Result.Success();
    }

    public Result UpdateSettings(TenantSettings settings)
    {
        var normalized = settings.Normalize();
        if (normalized.IsFailure)
        {
            return normalized.Error;
        }

        Settings = normalized.Value;
        return Result.Success();
    }

    public Result UpdateFiscalProfile(TenantFiscalProfile fiscal)
    {
        var rfc = string.IsNullOrWhiteSpace(fiscal.Rfc) ? null : MexicanIds.NormalizeRfc(fiscal.Rfc);
        if (rfc is not null && !MexicanIds.IsValidRfc(rfc))
        {
            return TenantErrors.InvalidRfc;
        }

        if (fiscal.PostalCode is not null && !MexicanIds.IsValidPostalCode(fiscal.PostalCode))
        {
            return TenantErrors.InvalidPostalCode;
        }

        if (!Codes.IsValid(Codes.Normalize(fiscal.InvoiceSeries ?? string.Empty)) || !Codes.IsValid(Codes.Normalize(fiscal.PaymentSeries ?? string.Empty)))
        {
            return TenantErrors.InvalidPrefix;
        }

        Fiscal = fiscal with
        {
            Rfc = rfc,
            LegalName = string.IsNullOrWhiteSpace(fiscal.LegalName) ? null : fiscal.LegalName.Trim().ToUpperInvariant(),
            InvoiceSeries = Codes.Normalize(fiscal.InvoiceSeries!),
            PaymentSeries = Codes.Normalize(fiscal.PaymentSeries!),
            PacProvider = string.IsNullOrWhiteSpace(fiscal.PacProvider) ? TenantFiscalProfile.SimulatedPac : fiscal.PacProvider.Trim()
        };

        return Result.Success();
    }

    /// <summary>Replaces the contracted modules; core modules and dependencies are added automatically.</summary>
    public void SetModules(IEnumerable<string> modules) => _enabledModules = BrocalModules.WithDependencies(modules).ToList();

    public bool IsModuleEnabled(string moduleCode) =>
        BrocalModules.CoreModules.Contains(moduleCode) || _enabledModules.Contains(moduleCode);

    public void StartTrial(DateOnly endsOn)
    {
        Status = TenantStatus.Trial;
        TrialEndsOn = endsOn;
        StatusReason = null;
    }

    public void Activate()
    {
        Status = TenantStatus.Active;
        StatusReason = null;
        TrialEndsOn = null;
    }

    public Result Suspend(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return TenantErrors.ReasonRequired;
        }

        Status = TenantStatus.Suspended;
        StatusReason = reason.Trim();
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return TenantErrors.ReasonRequired;
        }

        Status = TenantStatus.Cancelled;
        StatusReason = reason.Trim();
        return Result.Success();
    }
}

public static class TenantErrors
{
    public static readonly Error NotFound = Error.NotFound("Tenants.NotFound", "La empresa no existe.");

    public static readonly Error InvalidSlug = Error.Validation(
        "Tenants.InvalidSlug", "El identificador usa minúsculas, números y guiones (3 a 40 caracteres), p. ej. 'servicios-golfo'.");

    public static readonly Error SlugTaken = Error.Conflict("Tenants.SlugTaken", "Ya existe una empresa con ese identificador.");

    public static readonly Error InvalidName = Error.Validation("Tenants.InvalidName", "El nombre de la empresa es obligatorio (máximo 150 caracteres).");

    public static readonly Error InvalidRfc = Error.Validation("Tenants.InvalidRfc", "El RFC no tiene un formato válido.");

    public static readonly Error InvalidPostalCode = Error.Validation("Tenants.InvalidPostalCode", "El código postal debe tener 5 dígitos.");

    public static readonly Error InvalidColor = Error.Validation("Tenants.InvalidColor", "Todos los colores deben tener formato hexadecimal #RRGGBB.");

    public static readonly Error InvalidAppName = Error.Validation("Tenants.InvalidAppName", "El nombre visible es obligatorio (máximo 60 caracteres).");

    public static readonly Error InvalidDensity = Error.Validation("Tenants.InvalidDensity", "La densidad va de 0 (cómoda) a -3 (compacta).");

    public static readonly Error InvalidFont = Error.Validation("Tenants.InvalidFont", "Tipografía no disponible.");

    public static readonly Error BrandingTextTooLong = Error.Validation("Tenants.BrandingTextTooLong", "El lema admite 80 caracteres y el pie de documentos 300.");

    public static readonly Error InvalidQuotas = Error.Validation("Tenants.InvalidQuotas", "Las cuotas no pueden ser negativas.");

    public static readonly Error InvalidPrefix = Error.Validation(
        "Tenants.InvalidPrefix", "Los prefijos y series usan letras, números o guiones (máximo 10 caracteres).");

    public static readonly Error InvalidSettings = Error.Validation("Tenants.InvalidSettings", "Revisa los valores de la configuración.");

    public static readonly Error ReasonRequired = Error.Validation("Tenants.ReasonRequired", "Indica el motivo.");

    public static readonly Error NotOperational = Error.Forbidden(
        "Tenants.NotOperational", "La empresa está suspendida o su periodo de prueba terminó. Contacta al administrador de la plataforma.");

    public static readonly Error ModuleDisabled = Error.Forbidden("Tenants.ModuleDisabled", "El módulo no está habilitado para tu empresa.");

    public static Error QuotaExceeded(string what, int limit) =>
        Error.Conflict("Tenants.QuotaExceeded", $"Se alcanzó el límite contratado de {what} ({limit}). Contacta al administrador de la plataforma.");
}
