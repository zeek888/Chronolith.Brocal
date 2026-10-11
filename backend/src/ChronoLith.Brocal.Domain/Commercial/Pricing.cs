using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;

namespace ChronoLith.Brocal.Domain.Commercial;

/// <summary>
/// Billable concept of the catalog (day of tanker rental, m³ of water transported, pumping hour…) with the SAT
/// codes that the CFDI needs.
/// </summary>
public sealed class Service : CatalogAggregate
{
    private Service()
    {
    }

    public string? Description { get; private set; }

    public string? Category { get; private set; }

    /// <summary>Unit shown to people (Día, m³, Hora, Viaje, Servicio).</summary>
    public string UnitName { get; private set; } = null!;

    /// <summary>c_ClaveProdServ (p. ej. 71121000 servicios de perforación).</summary>
    public string SatProductCode { get; private set; } = null!;

    /// <summary>c_ClaveUnidad (p. ej. E48 unidad de servicio, DAY día, HUR hora, MTQ m³).</summary>
    public string SatUnitCode { get; private set; } = null!;

    /// <summary>c_ObjetoImp: 02 = sí objeto de impuesto.</summary>
    public string TaxObject { get; private set; } = "02";

    public decimal VatRate { get; private set; } = 0.16m;

    /// <summary>List price used when no price list applies.</summary>
    public decimal? BasePrice { get; private set; }

    public static Result<Service> Create(ServiceData data)
    {
        var service = new Service();
        var result = service.Update(data);
        return result.IsFailure ? result.Error : service;
    }

    public Result Update(ServiceData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if (string.IsNullOrWhiteSpace(data.UnitName) || data.UnitName.Trim().Length > 30)
        {
            return ServiceErrors.InvalidUnit;
        }

        if (string.IsNullOrWhiteSpace(data.SatProductCode) || data.SatProductCode.Trim().Length != 8 || !data.SatProductCode.Trim().All(char.IsDigit))
        {
            return ServiceErrors.InvalidSatProductCode;
        }

        if (string.IsNullOrWhiteSpace(data.SatUnitCode) || data.SatUnitCode.Trim().Length > 3)
        {
            return ServiceErrors.InvalidSatUnitCode;
        }

        if (data.VatRate is not (0m or 0.08m or 0.16m))
        {
            return ServiceErrors.InvalidVatRate;
        }

        if (data.BasePrice is < 0)
        {
            return PricingErrors.NegativePrice;
        }

        Description = string.IsNullOrWhiteSpace(data.Description) ? null : data.Description.Trim();
        Category = string.IsNullOrWhiteSpace(data.Category) ? null : data.Category.Trim();
        UnitName = data.UnitName.Trim();
        SatProductCode = data.SatProductCode.Trim();
        SatUnitCode = data.SatUnitCode.Trim().ToUpperInvariant();
        TaxObject = string.IsNullOrWhiteSpace(data.TaxObject) ? "02" : data.TaxObject.Trim();
        VatRate = data.VatRate;
        BasePrice = data.BasePrice;
        return Result.Success();
    }
}

public sealed record ServiceData(
    string Code,
    string Name,
    string? Description,
    string? Category,
    string UnitName,
    string SatProductCode,
    string SatUnitCode,
    string? TaxObject,
    decimal VatRate,
    decimal? BasePrice,
    string? Notes);

public static class ServiceErrors
{
    public const string Catalog = "Services";

    public static readonly Error InvalidUnit = Error.Validation("Services.InvalidUnit", "La unidad es obligatoria (máximo 30 caracteres).");

    public static readonly Error InvalidSatProductCode = Error.Validation("Services.InvalidSatProductCode", "La clave de producto o servicio del SAT tiene 8 dígitos.");

    public static readonly Error InvalidSatUnitCode = Error.Validation("Services.InvalidSatUnitCode", "La clave de unidad del SAT tiene hasta 3 caracteres (E48, DAY, HUR…).");

    public static readonly Error InvalidVatRate = Error.Validation("Services.InvalidVatRate", "La tasa de IVA es 0%, 8% o 16%.");
}

/// <summary>Price of a service inside a price list.</summary>
public sealed class PriceListItem : TenantEntity
{
    private PriceListItem()
    {
    }

    internal PriceListItem(Guid serviceId, decimal unitPrice, string? notes)
    {
        ServiceId = serviceId;
        UnitPrice = unitPrice;
        Notes = notes;
    }

    public Guid PriceListId { get; private set; }

    public Guid ServiceId { get; private set; }

    public decimal UnitPrice { get; private set; }

    public string? Notes { get; private set; }
}

/// <summary>
/// Agreed prices. Resolution order for a ticket: price list of its contract, then of its customer, then the general
/// list, always the one valid on the service date; if none applies, the base price of the service.
/// </summary>
public sealed class PriceList : CatalogAggregate
{
    private readonly List<PriceListItem> _items = [];

    private PriceList()
    {
    }

    /// <summary>Null = general price list.</summary>
    public Guid? CustomerId { get; private set; }

    public Guid? ContractId { get; private set; }

    public string Currency { get; private set; } = "MXN";

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    public IReadOnlyList<PriceListItem> Items => _items;

    public bool IsValidOn(DateOnly date) => IsActive && date >= ValidFrom && (ValidTo is null || date <= ValidTo);

    public static Result<PriceList> Create(PriceListData data)
    {
        var list = new PriceList();
        var result = list.Update(data);
        return result.IsFailure ? result.Error : list;
    }

    public Result Update(PriceListData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if (data.ValidTo is not null && data.ValidTo < data.ValidFrom)
        {
            return PricingErrors.InvalidPeriod;
        }

        if (data.ContractId is not null && data.CustomerId is null)
        {
            return PricingErrors.ContractWithoutCustomer;
        }

        if (data.Currency is not ("MXN" or "USD"))
        {
            return ContractErrors.InvalidCurrency;
        }

        var items = data.Items ?? [];
        if (items.Any(i => i.UnitPrice < 0))
        {
            return PricingErrors.NegativePrice;
        }

        if (items.GroupBy(i => i.ServiceId).Any(g => g.Count() > 1))
        {
            return PricingErrors.DuplicatedService;
        }

        CustomerId = data.CustomerId;
        ContractId = data.ContractId;
        Currency = data.Currency;
        ValidFrom = data.ValidFrom;
        ValidTo = data.ValidTo;

        _items.Clear();
        _items.AddRange(items.Select(i => new PriceListItem(i.ServiceId, Money.RoundRate(i.UnitPrice), string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes.Trim())));
        return Result.Success();
    }

    public decimal? PriceOf(Guid serviceId) => _items.FirstOrDefault(i => i.ServiceId == serviceId)?.UnitPrice;

    /// <summary>Specificity used to break ties: contract (2) beats customer (1) beats general (0).</summary>
    public int Specificity => ContractId is not null ? 2 : CustomerId is not null ? 1 : 0;
}

public sealed record PriceListItemData(Guid ServiceId, decimal UnitPrice, string? Notes);

public sealed record PriceListData(
    string Code,
    string Name,
    Guid? CustomerId,
    Guid? ContractId,
    string Currency,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    IReadOnlyList<PriceListItemData>? Items,
    string? Notes);

public enum PriceSource
{
    Contract = 0,
    Customer = 1,
    General = 2,
    BasePrice = 3,
    None = 4,

    /// <summary>Captured by hand by someone allowed to override prices.</summary>
    Manual = 5
}

public sealed record ResolvedPrice(decimal? UnitPrice, PriceSource Source, Guid? PriceListId, string? PriceListName);

/// <summary>Price resolution rules shared by tickets, quotes and work orders.</summary>
public static class PriceResolver
{
    public static ResolvedPrice Resolve(IEnumerable<PriceList> lists, Guid? customerId, Guid? contractId, Guid serviceId, DateOnly date, decimal? basePrice)
    {
        var candidates = lists
            .Where(l => l.IsValidOn(date))
            .Where(l => l.ContractId is null || l.ContractId == contractId)
            .Where(l => l.CustomerId is null || l.CustomerId == customerId)
            .Select(l => (List: l, Price: l.PriceOf(serviceId)))
            .Where(x => x.Price is not null)
            .OrderByDescending(x => x.List.Specificity)
            .ThenByDescending(x => x.List.ValidFrom)
            .FirstOrDefault();

        if (candidates.List is not null)
        {
            var source = candidates.List.Specificity switch
            {
                2 => PriceSource.Contract,
                1 => PriceSource.Customer,
                _ => PriceSource.General
            };

            return new ResolvedPrice(candidates.Price, source, candidates.List.Id, candidates.List.Name);
        }

        return basePrice is null
            ? new ResolvedPrice(null, PriceSource.None, null, null)
            : new ResolvedPrice(basePrice, PriceSource.BasePrice, null, null);
    }
}

public static class PricingErrors
{
    public const string Catalog = "PriceLists";

    public static readonly Error InvalidPeriod = Error.Validation("PriceLists.InvalidPeriod", "La vigencia final no puede ser anterior a la inicial.");

    public static readonly Error ContractWithoutCustomer = Error.Validation("PriceLists.ContractWithoutCustomer", "Un tarifario de contrato debe indicar el cliente.");

    public static readonly Error NegativePrice = Error.Validation("PriceLists.NegativePrice", "Los precios no pueden ser negativos.");

    public static readonly Error DuplicatedService = Error.Validation("PriceLists.DuplicatedService", "Un servicio aparece más de una vez en el tarifario.");
}

public enum FormFieldType
{
    Text = 0,
    Number = 1,
    Select = 2,
    Checkbox = 3,
    Date = 4,
    Time = 5,
    TextArea = 6
}

/// <summary>Field of the capture form of a service type (volume, fluid type, pressure…).</summary>
public sealed record FormFieldDefinition(
    string Key,
    string Label,
    FormFieldType Type,
    bool Required,
    IReadOnlyList<string>? Options,
    string? Unit,
    int Order);

/// <summary>
/// Kind of service a ticket documents (fluid transport, high-pressure pumping, pit cleaning…). It defines the
/// capture form, the evidence required and the services billed by default.
/// </summary>
public sealed class ServiceType : CatalogAggregate
{
    private List<FormFieldDefinition> _fields = [];
    private List<Guid> _defaultServiceIds = [];

    private ServiceType()
    {
    }

    public string? Description { get; private set; }

    public string Icon { get; private set; } = "receipt_long";

    public string? Color { get; private set; }

    public bool RequiresSignature { get; private set; } = true;

    public int MinPhotos { get; private set; }

    public IReadOnlyList<FormFieldDefinition> Fields => _fields;

    public IReadOnlyList<Guid> DefaultServiceIds => _defaultServiceIds;

    public static Result<ServiceType> Create(ServiceTypeData data)
    {
        var type = new ServiceType();
        var result = type.Update(data);
        return result.IsFailure ? result.Error : type;
    }

    public Result Update(ServiceTypeData data)
    {
        var identity = SetIdentity(data.Code, data.Name, data.Notes);
        if (identity.IsFailure)
        {
            return identity;
        }

        if (data.MinPhotos is < 0 or > 30)
        {
            return ServiceTypeErrors.InvalidMinPhotos;
        }

        if (data.Color is not null && !HexColor.IsValid(data.Color))
        {
            return CustomerErrors.InvalidColor;
        }

        var fields = (data.Fields ?? []).ToList();
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) || string.IsNullOrWhiteSpace(field.Label) || field.Key.Length > 40 || field.Label.Length > 80)
            {
                return ServiceTypeErrors.InvalidField;
            }

            if (field.Type == FormFieldType.Select && (field.Options is null || field.Options.Count == 0))
            {
                return ServiceTypeErrors.SelectWithoutOptions;
            }
        }

        if (fields.GroupBy(f => f.Key.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            return ServiceTypeErrors.DuplicatedField;
        }

        Description = string.IsNullOrWhiteSpace(data.Description) ? null : data.Description.Trim();
        Icon = string.IsNullOrWhiteSpace(data.Icon) ? "receipt_long" : data.Icon.Trim();
        Color = data.Color is null ? null : HexColor.Normalize(data.Color);
        RequiresSignature = data.RequiresSignature;
        MinPhotos = data.MinPhotos;
        _fields = fields
            .Select((f, i) => f with { Key = f.Key.Trim(), Label = f.Label.Trim(), Order = f.Order == 0 ? i + 1 : f.Order })
            .OrderBy(f => f.Order)
            .ToList();
        _defaultServiceIds = (data.DefaultServiceIds ?? []).Distinct().ToList();
        return Result.Success();
    }

    /// <summary>Checks the answers of a ticket against the form: required fields, numbers and options.</summary>
    public Result ValidateAnswers(IReadOnlyDictionary<string, string?> answers, bool requireComplete)
    {
        foreach (var field in _fields)
        {
            answers.TryGetValue(field.Key, out var value);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (field.Required && requireComplete)
                {
                    return ServiceTypeErrors.MissingAnswer(field.Label);
                }

                continue;
            }

            if (field.Type == FormFieldType.Number && !decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return ServiceTypeErrors.InvalidAnswer(field.Label);
            }

            if (field.Type == FormFieldType.Select && field.Options is not null && !field.Options.Contains(value))
            {
                return ServiceTypeErrors.InvalidAnswer(field.Label);
            }
        }

        return Result.Success();
    }
}

public sealed record ServiceTypeData(
    string Code,
    string Name,
    string? Description,
    string? Icon,
    string? Color,
    bool RequiresSignature,
    int MinPhotos,
    IReadOnlyList<FormFieldDefinition>? Fields,
    IReadOnlyList<Guid>? DefaultServiceIds,
    string? Notes);

public static class ServiceTypeErrors
{
    public const string Catalog = "ServiceTypes";

    public static readonly Error InvalidMinPhotos = Error.Validation("ServiceTypes.InvalidMinPhotos", "Las fotos mínimas van de 0 a 30.");

    public static readonly Error InvalidField = Error.Validation("ServiceTypes.InvalidField", "Cada campo necesita clave (máx. 40) y etiqueta (máx. 80).");

    public static readonly Error SelectWithoutOptions = Error.Validation("ServiceTypes.SelectWithoutOptions", "Los campos de lista necesitan opciones.");

    public static readonly Error DuplicatedField = Error.Validation("ServiceTypes.DuplicatedField", "Hay campos con la misma clave.");

    public static Error MissingAnswer(string label) => Error.Validation("ServiceTypes.MissingAnswer", $"Falta capturar '{label}'.");

    public static Error InvalidAnswer(string label) => Error.Validation("ServiceTypes.InvalidAnswer", $"El valor de '{label}' no es válido.");
}
