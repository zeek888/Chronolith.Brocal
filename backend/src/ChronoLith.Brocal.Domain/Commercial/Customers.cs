using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.Commercial;

/// <summary>Person of the customer that receives tickets, invoices or reminders.</summary>
public sealed record CustomerContact(
    string Name,
    string? Position,
    string? Email,
    string? Phone,
    bool NotifyTickets,
    bool NotifyInvoices,
    bool NotifyCollections);

/// <summary>
/// Customer of the contractor (Pemex, private operators…): fiscal data for CFDI 4.0, credit terms and contacts.
/// </summary>
public sealed class Customer : CatalogAggregate
{
    private List<CustomerContact> _contacts = [];

    private Customer()
    {
    }

    public string LegalName { get; private set; } = null!;

    public string Rfc { get; private set; } = null!;

    /// <summary>Régimen fiscal del receptor (c_RegimenFiscal, p. ej. 601).</summary>
    public string? TaxRegime { get; private set; }

    /// <summary>Código postal del domicilio fiscal del receptor.</summary>
    public string? PostalCode { get; private set; }

    /// <summary>Uso del CFDI por omisión (c_UsoCFDI, p. ej. G03).</summary>
    public string CfdiUse { get; private set; } = "G03";

    /// <summary>Forma de pago por omisión (c_FormaPago, p. ej. 99 por definir, 03 transferencia).</summary>
    public string PaymentForm { get; private set; } = "99";

    /// <summary>Método de pago: PUE (una exhibición) o PPD (parcialidades o diferido).</summary>
    public string PaymentMethod { get; private set; } = "PPD";

    public int CreditDays { get; private set; } = 30;

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? Address { get; private set; }

    /// <summary>Color used in the dispatch board and charts.</summary>
    public string? Color { get; private set; }

    /// <summary>Company of the ChronoLith shared catalog this customer corresponds to (module mode).</summary>
    public Guid? ShellCompanyId { get; private set; }

    /// <summary>Customer that validates estimates through COPADE (Pemex).</summary>
    public bool RequiresCopade { get; private set; }

    public IReadOnlyList<CustomerContact> Contacts => _contacts;

    public static Result<Customer> Create(CustomerData data)
    {
        var customer = new Customer();
        var result = customer.Update(data);
        return result.IsFailure ? result.Error : customer;
    }

    public Result Update(CustomerData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        var rfc = MexicanIds.NormalizeRfc(data.Rfc ?? string.Empty);
        if (!MexicanIds.IsValidRfc(rfc))
        {
            return CustomerErrors.InvalidRfc;
        }

        if (data.PostalCode is not null && !MexicanIds.IsValidPostalCode(data.PostalCode))
        {
            return CustomerErrors.InvalidPostalCode;
        }

        if (data.PaymentMethod is not ("PUE" or "PPD"))
        {
            return CustomerErrors.InvalidPaymentMethod;
        }

        if (data.CreditDays is < 0 or > 365)
        {
            return CustomerErrors.InvalidCreditDays;
        }

        if (data.Color is not null && !HexColor.IsValid(data.Color))
        {
            return CustomerErrors.InvalidColor;
        }

        var contacts = new List<CustomerContact>();
        foreach (var contact in data.Contacts ?? [])
        {
            if (string.IsNullOrWhiteSpace(contact.Name))
            {
                return CustomerErrors.InvalidContact;
            }

            var email = string.IsNullOrWhiteSpace(contact.Email) ? null : TenantMembership.NormalizeEmail(contact.Email);
            if (!string.IsNullOrWhiteSpace(contact.Email) && email is null)
            {
                return MembershipErrors.InvalidEmail;
            }

            var phone = string.IsNullOrWhiteSpace(contact.Phone) ? null : PhoneNumbers.Normalize(contact.Phone);
            if (!string.IsNullOrWhiteSpace(contact.Phone) && phone is null)
            {
                return NotificationErrors.InvalidPhone;
            }

            contacts.Add(contact with { Name = contact.Name.Trim(), Position = contact.Position?.Trim(), Email = email, Phone = phone });
        }

        LegalName = string.IsNullOrWhiteSpace(data.LegalName) ? Name.ToUpperInvariant() : data.LegalName.Trim().ToUpperInvariant();
        Rfc = rfc;
        TaxRegime = string.IsNullOrWhiteSpace(data.TaxRegime) ? null : data.TaxRegime.Trim();
        PostalCode = data.PostalCode;
        CfdiUse = string.IsNullOrWhiteSpace(data.CfdiUse) ? "G03" : data.CfdiUse.Trim().ToUpperInvariant();
        PaymentForm = string.IsNullOrWhiteSpace(data.PaymentForm) ? "99" : data.PaymentForm.Trim();
        PaymentMethod = data.PaymentMethod;
        CreditDays = data.CreditDays;
        Email = string.IsNullOrWhiteSpace(data.Email) ? null : TenantMembership.NormalizeEmail(data.Email) ?? data.Email.Trim();
        Phone = string.IsNullOrWhiteSpace(data.Phone) ? null : data.Phone.Trim();
        Address = string.IsNullOrWhiteSpace(data.Address) ? null : data.Address.Trim();
        Color = data.Color is null ? null : HexColor.Normalize(data.Color);
        ShellCompanyId = data.ShellCompanyId;
        RequiresCopade = data.RequiresCopade;
        _contacts = contacts;

        return Result.Success();
    }
}

public sealed record CustomerData(
    string Code,
    string Name,
    string? LegalName,
    string? Rfc,
    string? TaxRegime,
    string? PostalCode,
    string? CfdiUse,
    string? PaymentForm,
    string PaymentMethod,
    int CreditDays,
    string? Email,
    string? Phone,
    string? Address,
    string? Color,
    Guid? ShellCompanyId,
    bool RequiresCopade,
    IReadOnlyList<CustomerContact>? Contacts,
    string? Notes);

public static class CustomerErrors
{
    public const string Catalog = "Customers";

    public static readonly Error InvalidRfc = Error.Validation("Customers.InvalidRfc", "El RFC del cliente no tiene un formato válido.");

    public static readonly Error InvalidPostalCode = Error.Validation("Customers.InvalidPostalCode", "El código postal debe tener 5 dígitos.");

    public static readonly Error InvalidPaymentMethod = Error.Validation("Customers.InvalidPaymentMethod", "El método de pago es PUE o PPD.");

    public static readonly Error InvalidCreditDays = Error.Validation("Customers.InvalidCreditDays", "Los días de crédito van de 0 a 365.");

    public static readonly Error InvalidColor = Error.Validation("Customers.InvalidColor", "El color debe tener formato #RRGGBB.");

    public static readonly Error InvalidContact = Error.Validation("Customers.InvalidContact", "Cada contacto necesita al menos un nombre.");
}

public enum ContractStatus
{
    Draft = 0,
    Active = 1,
    Suspended = 2,
    Closed = 3
}

/// <summary>Contract with a customer (Pemex contract number, period, ceiling amount and agreed price list).</summary>
public sealed class Contract : CatalogAggregate
{
    private Contract()
    {
    }

    public Guid CustomerId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    /// <summary>Maximum amount of the contract (before VAT), if it has one.</summary>
    public decimal? CeilingAmount { get; private set; }

    public string Currency { get; private set; } = "MXN";

    public ContractStatus Status { get; private set; } = ContractStatus.Active;

    /// <summary>Administrative contact of the customer (residente de obra, administrador del contrato).</summary>
    public string? CustomerRepresentative { get; private set; }

    public bool IsCurrentOn(DateOnly date) => Status == ContractStatus.Active && date >= StartDate && (EndDate is null || date <= EndDate);

    public static Result<Contract> Create(ContractData data)
    {
        var contract = new Contract();
        var result = contract.Update(data);
        return result.IsFailure ? result.Error : contract;
    }

    public Result Update(ContractData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if (data.EndDate is not null && data.EndDate < data.StartDate)
        {
            return ContractErrors.InvalidPeriod;
        }

        if (data.CeilingAmount is < 0)
        {
            return ContractErrors.InvalidAmount;
        }

        if (data.Currency is not ("MXN" or "USD"))
        {
            return ContractErrors.InvalidCurrency;
        }

        CustomerId = data.CustomerId;
        StartDate = data.StartDate;
        EndDate = data.EndDate;
        CeilingAmount = data.CeilingAmount;
        Currency = data.Currency;
        Status = data.Status;
        CustomerRepresentative = string.IsNullOrWhiteSpace(data.CustomerRepresentative) ? null : data.CustomerRepresentative.Trim();
        return Result.Success();
    }
}

public sealed record ContractData(
    string Code,
    string Name,
    Guid CustomerId,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? CeilingAmount,
    string Currency,
    ContractStatus Status,
    string? CustomerRepresentative,
    string? Notes);

public static class ContractErrors
{
    public const string Catalog = "Contracts";

    public static readonly Error InvalidPeriod = Error.Validation("Contracts.InvalidPeriod", "La fecha de término no puede ser anterior al inicio.");

    public static readonly Error InvalidAmount = Error.Validation("Contracts.InvalidAmount", "El monto máximo no puede ser negativo.");

    public static readonly Error InvalidCurrency = Error.Validation("Contracts.InvalidCurrency", "La moneda es MXN o USD.");

    public static readonly Error CustomerMismatch = Error.Validation("Contracts.CustomerMismatch", "El contrato pertenece a otro cliente.");
}

public enum SiteType
{
    Well = 0,
    Field = 1,
    Facility = 2,
    Yard = 3,
    Other = 4
}

/// <summary>
/// Place where services are delivered (well, field, facility, yard). In module mode it can mirror a well or field of
/// the ChronoLith shared catalog (reference by id, name kept as snapshot).
/// </summary>
public sealed class Site : CatalogAggregate
{
    private Site()
    {
    }

    public SiteType Type { get; private set; }

    public Guid? CustomerId { get; private set; }

    /// <summary>Field (campo) or asset the site belongs to, as text.</summary>
    public string? FieldName { get; private set; }

    public string? Municipality { get; private set; }

    public string? State { get; private set; }

    /// <summary>Postal code (needed for Carta Porte locations).</summary>
    public string? PostalCode { get; private set; }

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    /// <summary>Well or facility of the ChronoLith shared catalog.</summary>
    public Guid? ShellCatalogId { get; private set; }

    public static Result<Site> Create(SiteData data)
    {
        var site = new Site();
        var result = site.Update(data);
        return result.IsFailure ? result.Error : site;
    }

    public Result Update(SiteData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if (data.Latitude is < -90 or > 90 || data.Longitude is < -180 or > 180)
        {
            return SiteErrors.InvalidCoordinates;
        }

        if (data.PostalCode is not null && !MexicanIds.IsValidPostalCode(data.PostalCode))
        {
            return CustomerErrors.InvalidPostalCode;
        }

        Type = data.Type;
        CustomerId = data.CustomerId;
        FieldName = string.IsNullOrWhiteSpace(data.FieldName) ? null : data.FieldName.Trim();
        Municipality = string.IsNullOrWhiteSpace(data.Municipality) ? null : data.Municipality.Trim();
        State = string.IsNullOrWhiteSpace(data.State) ? null : data.State.Trim();
        PostalCode = data.PostalCode;
        Latitude = data.Latitude;
        Longitude = data.Longitude;
        ShellCatalogId = data.ShellCatalogId;
        return Result.Success();
    }
}

public sealed record SiteData(
    string Code,
    string Name,
    SiteType Type,
    Guid? CustomerId,
    string? FieldName,
    string? Municipality,
    string? State,
    string? PostalCode,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ShellCatalogId,
    string? Notes);

public static class SiteErrors
{
    public const string Catalog = "Sites";

    public static readonly Error InvalidCoordinates = Error.Validation("Sites.InvalidCoordinates", "Latitud entre -90 y 90, longitud entre -180 y 180.");
}
