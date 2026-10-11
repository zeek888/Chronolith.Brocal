using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;

namespace ChronoLith.Brocal.Domain.Platform;

public enum ColorSchemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>Corner radius of cards, fields and buttons.</summary>
public enum CornerStyle
{
    Rounded = 0,
    Soft = 1,
    Square = 2
}

/// <summary>
/// Seed colors of a theme (same contract as the ChronoLith Shell): the frontend derives the full Material 3
/// palettes for light and dark mode from them.
/// </summary>
public sealed record ThemePalette(
    string Primary,
    string Secondary,
    string Tertiary,
    string Neutral,
    string Error,
    string Success,
    string Warning,
    string Info)
{
    /// <summary>Corporate palette of the ChronoLith logo (the Shell default theme).</summary>
    public static ThemePalette ChronoLith { get; } = new("#0B365B", "#2874A6", "#E58E38", "#5E6872", "#D64532", "#5A9E34", "#E0A82E", "#2C9EC8");

    public Result<ThemePalette> Normalize()
    {
        string[] colors = [Primary, Secondary, Tertiary, Neutral, Error, Success, Warning, Info];
        if (colors.Any(c => !HexColor.IsValid(c)))
        {
            return TenantErrors.InvalidColor;
        }

        return new ThemePalette(
            HexColor.Normalize(Primary),
            HexColor.Normalize(Secondary),
            HexColor.Normalize(Tertiary),
            HexColor.Normalize(Neutral),
            HexColor.Normalize(Error),
            HexColor.Normalize(Success),
            HexColor.Normalize(Warning),
            HexColor.Normalize(Info));
    }
}

/// <summary>
/// Look and feel of a company. With <see cref="UseChronoLithTheme"/> Brocal keeps the theme configured in
/// ChronoLith (inherited inside the Shell, or read from the Shell API when it runs standalone); otherwise it
/// paints the company palette. Logo and name are always the company's (screens, PDFs and the customer portal).
/// </summary>
public sealed record TenantBranding(
    bool UseChronoLithTheme,
    ThemePalette Palette,
    string AppName,
    string? Tagline,
    Guid? LogoFileId,
    Guid? LogoDarkFileId,
    int Density,
    ColorSchemePreference DefaultColorScheme,
    string FontFamily,
    CornerStyle Corners,
    string? DocumentFooter)
{
    public static readonly IReadOnlyList<string> FontFamilies = ["Inter", "Montserrat", "Roboto", "Sistema"];

    public static TenantBranding Default(string appName) => new(
        UseChronoLithTheme: true,
        Palette: ThemePalette.ChronoLith,
        AppName: appName,
        Tagline: null,
        LogoFileId: null,
        LogoDarkFileId: null,
        Density: 0,
        DefaultColorScheme: ColorSchemePreference.System,
        FontFamily: "Inter",
        Corners: CornerStyle.Rounded,
        DocumentFooter: null);

    public Result<TenantBranding> Normalize()
    {
        if (string.IsNullOrWhiteSpace(AppName) || AppName.Trim().Length > 60)
        {
            return TenantErrors.InvalidAppName;
        }

        if (Density is < -3 or > 0)
        {
            return TenantErrors.InvalidDensity;
        }

        if (!FontFamilies.Contains(FontFamily))
        {
            return TenantErrors.InvalidFont;
        }

        if (Tagline is { Length: > 80 } || DocumentFooter is { Length: > 300 })
        {
            return TenantErrors.BrandingTextTooLong;
        }

        var palette = Palette.Normalize();
        if (palette.IsFailure)
        {
            return palette.Error;
        }

        return this with
        {
            Palette = palette.Value,
            AppName = AppName.Trim(),
            Tagline = string.IsNullOrWhiteSpace(Tagline) ? null : Tagline.Trim(),
            DocumentFooter = string.IsNullOrWhiteSpace(DocumentFooter) ? null : DocumentFooter.Trim()
        };
    }
}

/// <summary>Contracted limits of a company. <c>null</c> means unlimited.</summary>
public sealed record TenantQuotas(
    int? MaxUsers,
    int? MaxPortalUsers,
    int? MaxUnits,
    int? MaxEmployees,
    int? MaxStorageMb,
    int? MaxTicketsPerMonth,
    int? MaxApiKeys)
{
    public static TenantQuotas Default { get; } = new(25, 10, 50, 200, 5120, null, 5);

    public static TenantQuotas Unlimited { get; } = new(null, null, null, null, null, null, null);

    public bool IsValid => new[] { MaxUsers, MaxPortalUsers, MaxUnits, MaxEmployees, MaxStorageMb, MaxTicketsPerMonth, MaxApiKeys }
        .All(v => v is null or >= 0);
}

/// <summary>Operational preferences of a company: numbering, alerts and capture rules.</summary>
public sealed record TenantSettings(
    string TimeZone,
    string Currency,
    string DefaultLanguage,
    string TicketPrefix,
    string WorkOrderPrefix,
    string QuotePrefix,
    string MaintenancePrefix,
    string IncidentPrefix,
    string TripPrefix,
    string EstimatePrefix,
    string ExpensePrefix,
    int ExpiryWarningDays,
    int UninvoicedAlertDays,
    int MinTicketPhotos,
    bool RequireCustomerSignature,
    bool RequireApprovalBeforeInvoice,
    decimal DefaultVatRate,
    decimal FuelEfficiencyTolerancePercent)
{
    public static TenantSettings Default { get; } = new(
        TimeZone: "America/Mexico_City",
        Currency: "MXN",
        DefaultLanguage: "es",
        TicketPrefix: "BS",
        WorkOrderPrefix: "OT",
        QuotePrefix: "COT",
        MaintenancePrefix: "MT",
        IncidentPrefix: "INC",
        TripPrefix: "CP",
        EstimatePrefix: "EST",
        ExpensePrefix: "VI",
        ExpiryWarningDays: 30,
        UninvoicedAlertDays: 5,
        MinTicketPhotos: 0,
        RequireCustomerSignature: true,
        RequireApprovalBeforeInvoice: false,
        DefaultVatRate: 0.16m,
        FuelEfficiencyTolerancePercent: 15m);

    public Result<TenantSettings> Normalize()
    {
        string[] prefixes = [TicketPrefix, WorkOrderPrefix, QuotePrefix, MaintenancePrefix, IncidentPrefix, TripPrefix, EstimatePrefix, ExpensePrefix];
        if (prefixes.Any(p => string.IsNullOrWhiteSpace(p) || p.Trim().Length > 10 || !Codes.IsValid(Codes.Normalize(p))))
        {
            return TenantErrors.InvalidPrefix;
        }

        if (ExpiryWarningDays is < 1 or > 365 || UninvoicedAlertDays is < 1 or > 365 || MinTicketPhotos is < 0 or > 30)
        {
            return TenantErrors.InvalidSettings;
        }

        if (DefaultVatRate is < 0 or > 1 || FuelEfficiencyTolerancePercent is < 0 or > 100)
        {
            return TenantErrors.InvalidSettings;
        }

        if (string.IsNullOrWhiteSpace(TimeZone) || Currency is not ("MXN" or "USD") || DefaultLanguage is not ("es" or "en"))
        {
            return TenantErrors.InvalidSettings;
        }

        return this with
        {
            TicketPrefix = Codes.Normalize(TicketPrefix),
            WorkOrderPrefix = Codes.Normalize(WorkOrderPrefix),
            QuotePrefix = Codes.Normalize(QuotePrefix),
            MaintenancePrefix = Codes.Normalize(MaintenancePrefix),
            IncidentPrefix = Codes.Normalize(IncidentPrefix),
            TripPrefix = Codes.Normalize(TripPrefix),
            EstimatePrefix = Codes.Normalize(EstimatePrefix),
            ExpensePrefix = Codes.Normalize(ExpensePrefix),
            TimeZone = TimeZone.Trim()
        };
    }
}

/// <summary>Issuer data for CFDI 4.0 and the PAC that stamps the invoices of the company.</summary>
public sealed record TenantFiscalProfile(
    string? Rfc,
    string? LegalName,
    string? TaxRegime,
    string? PostalCode,
    string InvoiceSeries,
    string PaymentSeries,
    string PacProvider,
    string? PacUser,
    string? PacSecretProtected,
    bool PacSandbox)
{
    public const string SimulatedPac = "Simulado";

    public static TenantFiscalProfile Empty { get; } = new(null, null, null, null, "A", "P", SimulatedPac, null, null, true);

    public bool IsComplete =>
        Rfc is not null && MexicanIds.IsValidRfc(Rfc) && !string.IsNullOrWhiteSpace(LegalName) &&
        !string.IsNullOrWhiteSpace(TaxRegime) && MexicanIds.IsValidPostalCode(PostalCode);
}
