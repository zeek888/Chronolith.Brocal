using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Application.Features.Platform;

/// <summary>Look and feel of a company as the frontend needs it (logo URLs signed for the session).</summary>
public sealed record BrandingDto(
    bool UseChronoLithTheme,
    ThemePalette Palette,
    string AppName,
    string? Tagline,
    Guid? LogoFileId,
    string? LogoUrl,
    Guid? LogoDarkFileId,
    string? LogoDarkUrl,
    int Density,
    ColorSchemePreference DefaultColorScheme,
    string FontFamily,
    CornerStyle Corners,
    string? DocumentFooter)
{
    public static BrandingDto From(TenantBranding branding, IFileService files) => new(
        branding.UseChronoLithTheme,
        branding.Palette,
        branding.AppName,
        branding.Tagline,
        branding.LogoFileId,
        branding.LogoFileId is { } logo ? files.GetUrl(logo, TimeSpan.FromHours(12)) : null,
        branding.LogoDarkFileId,
        branding.LogoDarkFileId is { } dark ? files.GetUrl(dark, TimeSpan.FromHours(12)) : null,
        branding.Density,
        branding.DefaultColorScheme,
        branding.FontFamily,
        branding.Corners,
        branding.DocumentFooter);
}

/// <summary>Fiscal profile without secrets (only whether the PAC password is set).</summary>
public sealed record FiscalProfileDto(
    string? Rfc,
    string? LegalName,
    string? TaxRegime,
    string? PostalCode,
    string InvoiceSeries,
    string PaymentSeries,
    string PacProvider,
    string? PacUser,
    bool HasPacSecret,
    bool PacSandbox,
    bool IsComplete)
{
    public static FiscalProfileDto From(TenantFiscalProfile fiscal) => new(
        fiscal.Rfc,
        fiscal.LegalName,
        fiscal.TaxRegime,
        fiscal.PostalCode,
        fiscal.InvoiceSeries,
        fiscal.PaymentSeries,
        fiscal.PacProvider,
        fiscal.PacUser,
        !string.IsNullOrEmpty(fiscal.PacSecretProtected),
        fiscal.PacSandbox,
        fiscal.IsComplete);
}

public sealed record PermissionDto(string Code, string Name, string NameEn, string? Description, string? DescriptionEn);

public sealed record ModuleDto(
    string Code,
    int Number,
    ModuleCategory Category,
    string Name,
    string NameEn,
    string Description,
    string DescriptionEn,
    string Icon,
    bool IsCore,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<PermissionDto> Permissions)
{
    public static ModuleDto From(ModuleDefinition module) => new(
        module.Code,
        module.Number,
        module.Category,
        module.Name,
        module.NameEn,
        module.Description,
        module.DescriptionEn,
        module.Icon,
        module.IsCore,
        module.DependsOn,
        module.Permissions.Select(p => new PermissionDto(p.Code, p.Name, p.NameEn, p.Description, p.DescriptionEn)).ToList());

    public static IReadOnlyList<ModuleDto> Catalog { get; } = BrocalModules.All.Select(From).ToList();
}

/// <summary>Consumption of the contracted quotas.</summary>
public sealed record UsageDto(
    int Users,
    int PortalUsers,
    int Units,
    int Employees,
    decimal StorageMb,
    int TicketsThisMonth,
    int ApiKeys);
