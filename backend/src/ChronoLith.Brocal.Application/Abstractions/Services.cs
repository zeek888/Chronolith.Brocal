using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Application.Abstractions;

// ---- Files ----------------------------------------------------------------------------------------

/// <summary>Raw byte storage (local disk volume by default).</summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Files of the current company: quota, hashing, metadata and signed download URLs.</summary>
public interface IFileService
{
    Task<Result<StoredFile>> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        FileCategory category,
        CancellationToken cancellationToken,
        string? ownerType = null,
        Guid? ownerId = null);

    Task<Result<StoredFile>> SaveBytesAsync(
        byte[] content,
        string fileName,
        string contentType,
        FileCategory category,
        CancellationToken cancellationToken,
        string? ownerType = null,
        Guid? ownerId = null);

    Task<byte[]?> ReadAllBytesAsync(Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Relative URL (<c>files/{id}/content?…</c>) signed for the company (the current one unless
    /// <paramref name="tenantId"/> is given, e.g. logos of the company selector); valid for a limited time.
    /// </summary>
    string GetUrl(Guid fileId, TimeSpan? lifetime = null, Guid? tenantId = null);

    Task<long> UsedBytesAsync(CancellationToken cancellationToken);

    /// <summary>Opens a file from a signed link (no session needed): null when the signature is invalid or expired.</summary>
    Task<(string FileName, string ContentType, Stream Content)?> OpenSignedAsync(Guid fileId, Guid tenantId, long expires, string signature, CancellationToken cancellationToken);
}

// ---- Documents ------------------------------------------------------------------------------------

/// <summary>Look of generated documents (logo and colors of the company).</summary>
public sealed record DocumentBranding(
    string CompanyName,
    string? LegalName,
    string? TaxId,
    byte[]? Logo,
    string PrimaryColor,
    string AccentColor,
    string? Footer);

public sealed record DocumentField(string Label, string? Value);

public sealed record DocumentTable(string? Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<bool>? RightAligned = null);

public sealed record DocumentImage(byte[] Content, string? Caption);

public sealed record DocumentSignature(string Label, string? Name, string? Position, byte[]? Image, string? Date);

public sealed record DocumentTotals(IReadOnlyList<DocumentField> Lines);

/// <summary>
/// Generic business document (ticket, quote, invoice representation, report): header with folio and status,
/// field groups, tables, totals, photos and signatures. Rendered to PDF with the company branding.
/// </summary>
public sealed record BusinessDocument(
    string Title,
    string Folio,
    string? Status,
    string? Date,
    IReadOnlyList<(string Heading, IReadOnlyList<DocumentField> Fields)> Sections,
    IReadOnlyList<DocumentTable> Tables,
    DocumentTotals? Totals,
    IReadOnlyList<(string Heading, string Text)> Texts,
    IReadOnlyList<DocumentImage> Images,
    IReadOnlyList<DocumentSignature> Signatures,
    string? Watermark = null,
    string? QrText = null);

public interface IPdfRenderer
{
    byte[] Render(BusinessDocument document, DocumentBranding branding);
}

public sealed record ExcelSheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows, string? Title = null);

public interface IExcelWriter
{
    byte[] Write(IReadOnlyList<ExcelSheet> sheets);
}

/// <summary>Builds the branding of documents for the current company (logo bytes and palette).</summary>
public interface IDocumentBrandingProvider
{
    Task<DocumentBranding> GetAsync(CancellationToken cancellationToken);
}

// ---- Fiscal (CFDI 4.0) ----------------------------------------------------------------------------

public sealed record CfdiParty(string Rfc, string Name, string TaxRegime, string? PostalCode, string? CfdiUse);

public sealed record CfdiConcept(
    string SatProductCode,
    decimal Quantity,
    string SatUnitCode,
    string UnitName,
    string Description,
    decimal UnitPrice,
    decimal Amount,
    decimal Discount,
    string TaxObject,
    decimal VatRate,
    decimal Vat);

public sealed record CfdiPaymentDocument(string Uuid, string Series, string Folio, string Currency, int Partiality, decimal PreviousBalance, decimal Paid, decimal Remaining, decimal VatBase, decimal Vat);

public sealed record CfdiPayment(DateTimeOffset PaidAt, string PaymentForm, string Currency, decimal Amount, IReadOnlyList<CfdiPaymentDocument> Documents);

public sealed record CfdiCartaPorte(
    string IdCcp,
    decimal DistanceKm,
    string OriginName,
    string? OriginRfc,
    string OriginPostalCode,
    string OriginState,
    DateTimeOffset Departure,
    string DestinationName,
    string? DestinationRfc,
    string DestinationPostalCode,
    string DestinationState,
    DateTimeOffset Arrival,
    IReadOnlyList<(string SatGoodsCode, string Description, decimal Quantity, string SatUnitCode, decimal WeightKg, bool IsHazardous, string? HazardousCode, string? PackagingCode)> Goods,
    decimal TotalWeightKg,
    string SctPermitType,
    string SctPermitNumber,
    string VehicleConfig,
    decimal GrossWeightTons,
    string Plates,
    int ModelYear,
    string Insurer,
    string Policy,
    string? EnvironmentalInsurer,
    string? EnvironmentalPolicy,
    string? TrailerPlates,
    string DriverRfc,
    string DriverName,
    string DriverLicense);

/// <summary>Everything the PAC needs to issue a CFDI (type I, T or P).</summary>
public sealed record CfdiDocument(
    string Type,
    string Series,
    string Folio,
    DateTimeOffset Date,
    string? PaymentForm,
    string? PaymentMethod,
    string Currency,
    decimal ExchangeRate,
    decimal Subtotal,
    decimal Discount,
    decimal Vat,
    decimal Total,
    string ExpeditionPostalCode,
    CfdiParty Issuer,
    CfdiParty Receiver,
    IReadOnlyList<CfdiConcept> Concepts,
    CfdiCartaPorte? CartaPorte = null,
    CfdiPayment? Payment = null);

public sealed record StampResult(bool Success, string? Uuid, DateTimeOffset? StampedAt, string? Xml, string? Error, bool Simulated);

public sealed record CancelResult(bool Success, bool Accepted, string? Status, string? Error);

/// <summary>Proveedor Autorizado de Certificación. "Simulado" stamps locally for testing and demos.</summary>
public interface IPacGateway
{
    Task<StampResult> StampAsync(TenantFiscalProfile profile, CfdiDocument document, CancellationToken cancellationToken);

    Task<CancelResult> CancelAsync(TenantFiscalProfile profile, string uuid, string receiverRfc, decimal total, string motive, string? substitutionUuid, CancellationToken cancellationToken);

    IReadOnlyList<string> Providers { get; }
}

// ---- Notifications --------------------------------------------------------------------------------

/// <summary>Something happened that may notify people according to the rules of the company.</summary>
public sealed record NotificationMessage(
    NotificationEvent Event,
    NotificationSeverity Severity,
    string Title,
    string Body,
    string? Link,
    IReadOnlyCollection<Guid>? AssigneeMembershipIds = null,
    Guid? CustomerId = null,
    string? DeduplicationKey = null,
    Guid? AttachmentFileId = null);

public interface INotificationPublisher
{
    /// <summary>Adds in-app notifications and outbox messages to the unit of work (saved with the caller).</summary>
    Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody, (string FileName, string ContentType, byte[] Content)? Attachment);

public sealed record SmtpAccount(string Host, int Port, bool UseSsl, string? User, string? Password, string FromAddress, string? FromName);

public interface IEmailSender
{
    /// <summary>Returns false when there is no SMTP account configured (message simulated).</summary>
    Task<bool> SendAsync(EmailMessage message, SmtpAccount? account, CancellationToken cancellationToken);
}

public sealed record WhatsAppAccount(string PhoneNumberId, string Token, string Template, string Language);

public interface IWhatsAppSender
{
    /// <summary>Returns false when there is no account configured (message simulated).</summary>
    Task<bool> SendAsync(string to, string title, string body, WhatsAppAccount? account, CancellationToken cancellationToken);
}

// ---- Identity (optional Keycloak provisioning) ----------------------------------------------------

public sealed record IdentityUser(string Id, string Username, string? Email, string? FirstName, string? LastName, bool Enabled);

public interface IIdentityProvisioning
{
    bool IsEnabled { get; }

    Task<IdentityUser?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Creates the Keycloak account with a temporary password (the user must change it on first login).</summary>
    Task<Result<IdentityUser>> CreateAsync(string email, string firstName, string lastName, string temporaryPassword, CancellationToken cancellationToken);
}

// ---- ChronoLith platform --------------------------------------------------------------------------

public sealed record ShellLookupItem(Guid Id, string Code, string Name, Guid? ParentId, bool IsActive);

/// <summary>Shared catalogs of the ChronoLith Shell (wells, fields, companies) read with the user's token.</summary>
public interface IShellCatalogClient
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<ShellLookupItem>> LookupAsync(string catalog, CancellationToken cancellationToken);
}

public sealed record ChronoLithTheme(string Name, ThemePalette Palette, string? AppName, string? LogoUrl, int Density, string DefaultColorScheme);

/// <summary>Theme configured in ChronoLith (Administración → Apariencia), for standalone mode.</summary>
public interface IChronoLithAppearance
{
    Task<ChronoLithTheme?> GetDefaultThemeAsync(CancellationToken cancellationToken);
}
